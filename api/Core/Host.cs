// RDR2 Co-op: the three entry points the loader (RDR2Coop.asi) resolves with load_assembly_and_get_function_pointer.
// This replaces ScriptHookRDR2DotNet's C++/CLI DllMain.cpp (ManagedInit / ManagedTick / ManagedKeyboardMessage).

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Windows.Forms;
using RDR2DN;

[assembly: InternalsVisibleTo("CoreTests")]

namespace RDR2Coop.Core
{
	public static unsafe class Host
	{
		// SHDN's default ScriptTimeoutThreshold; there is no ScriptHookRDRDotNet.ini in this build
		const uint ScriptTimeoutThreshold = 5000;

		static ScriptDomain domain;
		static string gameDir;
		static bool reloadRequested;
		static bool logCleared;

		static Host()
		{
			// The mod's reference to RDR2Coop.Api (and any dependency DLL beside it) resolves from <game>\RDR2Coop; a
			// ScriptHookRDR2DotNet script's dependencies (enRage's Newtonsoft.Json) from <game>\scripts after that
			AssemblyLoadContext.Default.Resolving += (context, name) =>
			{
				if (gameDir == null)
				{
					return null;
				}

				if (name.Name == "RDR2Coop.Core")
				{
					return typeof(Host).Assembly;   // never a second copy: the API must see this one's ScriptDomain.CurrentDomain
				}

				foreach (var dir in new[] { "RDR2Coop", "scripts" })
				{
					string path = Path.Combine(gameDir, dir, name.Name + ".dll");
					if (File.Exists(path))
					{
						return context.LoadFromAssemblyPath(path);
					}
				}
				return null;
			};
			// NativeMemory's [DllImport("RDR2Coop.asi")]: the loader itself, already in the process; by full path so no search runs
			NativeLibrary.SetDllImportResolver(typeof(Host).Assembly, (name, assembly, searchPath) =>
				name == "RDR2Coop.asi" && gameDir != null ? NativeLibrary.Load(Path.Combine(gameDir, "RDR2Coop.asi")) : IntPtr.Zero);
		}

		// gameDir: UTF-16, the game folder (where RDR2.exe and RDR2Coop.asi are) WITH a trailing backslash.
		// Returns 0 = scripts running; 1 = failed (why is in scripts\RDR2Coop.Core.log); 2 = refused because
		// <game>\scripts\RDR2Coop.dll AND <game>\ScriptHookRDRDotNet.asi both exist (the SHDN build runs instead).
		// Called on the script fiber before the first Tick, and AGAIN on every game script reload (fiber change):
		// a second call = unload the current ScriptDomain, load a fresh one (SHDN ManagedInit semantics).
		[UnmanagedCallersOnly]
		public static int Init(char* gameDirPtr)
		{
			try
			{
				gameDir = new string(gameDirPtr);
				Log.Directory = Path.Combine(gameDir, "scripts");

				// Cleared on the first Init per process only, so a reload's reason stays visible
				if (!logCleared)
				{
					Log.Clear();
					logCleared = true;
				}

				if (File.Exists(Path.Combine(gameDir, "scripts", "RDR2Coop.dll")) && File.Exists(Path.Combine(gameDir, "ScriptHookRDRDotNet.asi")))
				{
					Log.Message(Log.Level.Error, "an older ScriptHookRDR2DotNet copy of the mod is installed (scripts\\RDR2Coop.dll): that one runs; delete it or run the installer to use this version");
					return 2;
				}

				Log.Message(Log.Level.Debug, "Core is ", typeof(Host).Assembly.Location, " in load context ", AssemblyLoadContext.GetLoadContext(typeof(Host).Assembly)?.Name ?? "?");
				Reload();
				return domain != null ? 0 : 1;
			}
			catch (Exception ex)
			{
				Log.Message(Log.Level.Error, "Init failed: ", ex.ToString());
				return 1;
			}
		}

		// One frame on the script fiber, between two scriptWait(0): performs a pending Insert reload, then DoTick().
		[UnmanagedCallersOnly]
		public static void Tick()
		{
			try
			{
				if (reloadRequested)
				{
					reloadRequested = false;
					Log.Message(Log.Level.Info, "Reloading scripts (Insert) ...");
					Reload();
				}

				domain?.DoTick();
			}
			catch (Exception ex)
			{
				Log.Message(Log.Level.Error, "Tick failed: ", ex.ToString());
			}
		}

		// key = Win32 VK code (dropped when key == 0 || key >= 256 like SHDN); down/ctrl/shift/alt = 0 or 1.
		// Same thread as Tick, never concurrent (the loader calls it from ScriptHookRDR2's keyboard handler).
		[UnmanagedCallersOnly]
		public static void Key(uint key, int down, int ctrl, int shift, int alt)
		{
			try
			{
				if (key == 0 || key >= 256)
				{
					return;
				}

				Keys keys = (Keys)key;
				if (ctrl != 0) keys |= Keys.Control;
				if (shift != 0) keys |= Keys.Shift;
				if (alt != 0) keys |= Keys.Alt;

				// Insert (no modifiers) key-down = reload, not dispatched to scripts, as SHDN's ReloadKey
				if (down != 0 && keys == Keys.Insert)
				{
					reloadRequested = true;
					return;
				}

				domain?.DoKeyEvent(keys, down != 0);
			}
			catch (Exception ex)
			{
				Log.Message(Log.Level.Error, "Key failed: ", ex.ToString());
			}
		}

		static void Reload()
		{
			if (domain != null)
			{
				var old = ScriptDomain.Unload(domain);
				domain = null;
				ScriptDomain.WaitCollected(old);
			}

			domain = ScriptDomain.Load(gameDir);
			if (domain == null)
			{
				return;
			}

			domain.ScriptTimeoutThreshold = ScriptTimeoutThreshold;
			domain.Start();
		}
	}
}
