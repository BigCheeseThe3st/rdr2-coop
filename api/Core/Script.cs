//
// Copyright (C) 2015 crosire & contributors
// License: https://github.com/crosire/scripthookvdotnet#license
//
// RDR2 Co-op port: Thread.Abort does not exist on .NET 9, so a script is aborted cooperatively with
// ScriptAbortedException (thrown on the script's own thread by Abort/Wait/ExecuteTask) instead.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Forms;

namespace RDR2DN
{
	/// <summary>
	/// Thrown on a script's thread to unwind it when the script was aborted; the Core equivalent of ThreadAbortException.
	/// </summary>
	internal sealed class ScriptAbortedException : Exception
	{
		public ScriptAbortedException() : base("The script was aborted.") { }
	}

	public sealed class Script
	{
		private Thread _thread; // The thread hosting the execution of the script
		internal SemaphoreSlim _waitEvent = new SemaphoreSlim(0);
		internal SemaphoreSlim _continueEvent = new SemaphoreSlim(0);
		internal readonly ConcurrentQueue<Tuple<bool, KeyEventArgs>> _keyboardEvents = new();
		private bool firstTime = true;
		internal static bool TextPoolInitEnabled = true; // CoreTests turn it off: no ScriptHookRDR2.dll outside the game
		// The script whose thread this is: how ExecuteTask tells a caller apart from the domain's notion of the
		// running script, so an aborted thread (timeout, reload) that still runs user code unwinds at its next
		// native call instead of joining another script's handshake.
		[ThreadStatic] internal static Script Current;

		/// <summary>
		/// Gets or sets the interval in ms between each <see cref="Tick"/>.
		/// Default interval is 0.
		/// </summary>
		public int Interval { get; set; }

		/// <summary>
		/// Gets whether executing of this script is paused or not.
		/// </summary>
		public bool IsPaused { get; private set; }

		/// <summary>
		/// Gets the status of this script.
		/// Returns <c>true</c> if it's running, or <c>false</c> if it was aborted.
		/// </summary>
		public bool IsRunning { get; private set; }

		/// <summary>
		/// Gets whether this is the currently executing script.
		/// </summary>
		public bool IsExecuting => ScriptDomain.ExecutingScript == this;

		/// <summary>
		/// An event that is raised every tick or "frame" of the script.
		/// </summary>
		public event EventHandler Tick;
		/// <summary>
		/// An event that is raised when this script gets aborted for any reason.
		/// </summary>
		public event EventHandler Aborted;

		/// <summary>
		/// An event that is raised when a key is just lifted.
		/// </summary>
		public event KeyEventHandler KeyUp;
		/// <summary>
		/// An event that is raised when a key is just pressed.
		/// </summary>
		public event KeyEventHandler KeyDown;

		/// <summary>
		/// Gets the name of this script.
		/// </summary>
		public string Name { get; internal set; }

		/// <summary>
		/// Gets the path to the file that contains this script.
		/// </summary>
		public string FileName { get; internal set; }

		/// <summary>
		/// Gets the instance of the script.
		/// </summary>
		public object ScriptInstance { get; internal set; }

		/// <summary>
		/// The main execution logic of all scripts.
		/// </summary>
		private void MainLoop()
		{
			Current = this;
			IsRunning = true;

			// Wait for script domain to continue this script
			_continueEvent.Wait();

			while (IsRunning)
			{
				// Process keyboard events
				while (_keyboardEvents.TryDequeue(out Tuple<bool, KeyEventArgs> ev))
				{
					try
					{
						if (!ev.Item1)
							KeyUp?.Invoke(this, ev.Item2);
						else
							KeyDown?.Invoke(this, ev.Item2);
					}
					catch (ScriptAbortedException)
					{
						// Stop main loop immediately when the script was aborted
						return;
					}
					catch (Exception ex)
					{
						ScriptDomain.HandleUnhandledException(this, new UnhandledExceptionEventArgs(ex, false));
						break; // Break out of key event loop, but continue to run script
					}
				}

				try
				{
					Tick?.Invoke(this, EventArgs.Empty);
				}
				catch (ScriptAbortedException)
				{
					// Stop main loop immediately when the script was aborted
					return;
				}
				catch (Exception ex)
				{
					ScriptDomain.HandleUnhandledException(this, new UnhandledExceptionEventArgs(ex, true));

					// An exception during tick is fatal, so abort the script and stop main loop
					try { Abort(); } catch (ScriptAbortedException) { } // Abort throws on its own thread; we are leaving anyway
					return;
				}

				// Yield execution to next tick
				try
				{
					Wait(Interval);
				}
				catch (ScriptAbortedException)
				{
					return;
				}
			}
		}

		// This is needed because RDR2.UI.TextElement wont work properly without this
		private unsafe void TextPoolInit()
		{
			if (firstTime && TextPoolInitEnabled)
			{
				for (float i = 0.0f; i < 10.24f; i += 0.01f)
				{
					NativeFunc.Invoke(0xA1253A3C870B6843, 0.1f, 0.1f); // UIDEBUG::_BG_SET_TEXT_SCALE
					NativeFunc.Invoke(0x16FA5CE47F184F1E, 255, 255, 255, 255); // UIDEBUG::_BG_SET_TEXT_COLOR
					var res = NativeFunc.Invoke(0xFA925AC00EB830B9, 10, "LITERAL_STRING", " \r\n\t "); // MISC::VAR_STRING
					NativeFunc.Invoke(0x16794E044C9EFB58, *res, i, i); // UIDEBUG::_BG_DISPLAY_TEXT
				}
				firstTime = false;
			}
		}

		/// <summary>
		/// Starts execution of this script.
		/// </summary>
		public void Start()
		{
			_thread = new Thread(new ThreadStart(MainLoop));
			_thread.IsBackground = true; // a leaked script thread must not keep the game process alive at exit
			_thread.Start();

			TextPoolInit();

			Log.Message(Log.Level.Info, "Started script ", Name, ".");
		}
		/// <summary>
		/// Aborts execution of this script.
		/// </summary>
		public void Abort()
		{
			// Aborted runs while the script still counts as running: on the script's own thread its native
			// calls go through ExecuteTask, which refuses a stopped script (the main thread is parked on
			// this script's handshake, so they are served as usual).
			try
			{
				Aborted?.Invoke(this, EventArgs.Empty);
			}
			catch (Exception ex)
			{
				ScriptDomain.HandleUnhandledException(this, new UnhandledExceptionEventArgs(ex, true));
			}

			IsRunning = false;

			_waitEvent.Release();

			if (_thread != null)
			{
				Log.Message(Log.Level.Warning, "Aborted script ", Name, ".");

				Thread thread = _thread;
				_thread = null;

				// Wake a script thread parked in Wait/ExecuteTask; it sees IsRunning == false and unwinds itself
				_continueEvent.Release();

				if (Thread.CurrentThread == thread)
				{
					throw new ScriptAbortedException();
				}
			}
		}

		/// <summary>
		/// Pause execution of this script.
		/// </summary>
		public void Pause()
		{
			if (IsPaused)
				return; // Pause status has not changed, so nothing to do

			IsPaused = true;

			Log.Message(Log.Level.Info, "Paused script ", Name, ".");
		}
		/// <summary>
		/// Resume execution of this script.
		/// </summary>
		public void Resume()
		{
			if (!IsPaused)
				return;

			IsPaused = false;

			Log.Message(Log.Level.Info, "Resumed script ", Name, ".");
		}

		/// <summary>
		/// Pause execution of this script for the specified time.
		/// </summary>
		/// <param name="ms">The time in milliseconds to pause.</param>
		public void Wait(int ms)
		{
			if (!IsRunning)
			{
				// Abort's one wake-up was consumed by an earlier Wait whose exception the script swallowed
				// (the mod's tick catches Exception): parking again would leak the thread for good.
				throw new ScriptAbortedException();
			}

			DateTime resumeTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(ms);

			do
			{
				_waitEvent.Release();
				_continueEvent.Wait();
			}
			while (IsRunning && DateTime.UtcNow < resumeTime);

			if (!IsRunning)
			{
				throw new ScriptAbortedException();
			}
		}
	}
}
