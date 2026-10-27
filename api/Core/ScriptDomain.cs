//
// Copyright (C) 2015 crosire & contributors
// License: https://github.com/crosire/ScriptHookvDotNet#license
//
// RDR2 Co-op port: scripts load from <game>\RDR2Coop\*.dll into a collectible AssemblyLoadContext (from bytes, so the
// file stays unlocked for the in-game updater); the AppDomain, shadow copy, CodeDom and console code is gone.
// ScriptHookRDR2DotNet scripts in <game>\scripts (enRage) run here too when ScriptHookRDRDotNet.asi isn't installed,
// through RDR2Coop\ScriptHookRDRNetAPI.dll and System.Windows.Forms.dll, which forward their types to this API (api/Compat).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security;
using System.Threading;
using System.Windows.Forms;

namespace RDR2DN
{
	/// <summary>
	/// The interface for tasks that must be run on the main thread (e.g. calling native functions) because of thread local storage (TLS).
	/// </summary>
	public interface IScriptTask
	{
		void Run();
	}

	public sealed class ScriptDomain : IDisposable
	{
		/// <summary>
		/// The context script assemblies live in; collectible so a reload starts with fresh statics. Everything else resolves in the default context.
		/// </summary>
		sealed class ScriptLoadContext : AssemblyLoadContext
		{
			public ScriptLoadContext() : base("RDR2Coop scripts", isCollectible: true) { }
			protected override Assembly Load(AssemblyName name) => null;
		}

		// Debugger.IsAttached does not detect a Visual Studio debugger
		[SuppressUnmanagedCodeSecurity]
		[DllImport("Kernel32.dll")]
		internal static extern bool IsDebuggerPresent();

		private int _executingThreadId = Thread.CurrentThread.ManagedThreadId;
		private Script _executingScript = null;
		private List<IntPtr> _pinnedStrings = new();
		private List<Script> _runningScripts = new();
		private Queue<IScriptTask> _taskQueue = new();
		private Dictionary<string, int> _scriptInstances = new();
		private SortedList<string, Tuple<string, Type>> _scriptTypes = new();
		private bool _recordKeyboardEvents = true;
		private bool[] _keyboardState = new bool[256];
		private readonly ScriptLoadContext _alc = new();

		/// <summary>
		/// Gets the friendly name of this script domain.
		/// </summary>
		public string Name => "ScriptDomain";
		/// <summary>
		/// Gets the game folder (where RDR2.exe is), as the loader passed it.
		/// </summary>
		public string GameDirectory { get; }
		/// <summary>
		/// Gets the path to the directory containing scripts: &lt;game&gt;\RDR2Coop, never scripts\ (that folder belongs to ScriptHookRDRDotNet).
		/// </summary>
		public string ScriptPath => Path.Combine(GameDirectory, "RDR2Coop");

		/// <summary>
		/// Gets the application domain that is associated with this script domain.
		/// </summary>
		public AppDomain AppDomain { get; } = AppDomain.CurrentDomain;

		/// <summary>
		/// Gets the scripting domain for the current application domain.
		/// </summary>
		public static ScriptDomain CurrentDomain { get; private set; }

		/// <summary>
		/// Gets the list of currently running scripts in this script domain. This is used by the console implementation.
		/// </summary>
		public Script[] RunningScripts => _runningScripts.ToArray();
		/// <summary>
		/// Gets the currently executing script or <c>null</c> if there is none.
		/// </summary>
		public static Script ExecutingScript => Script.Current ?? (CurrentDomain != null ? CurrentDomain._executingScript : null);

		/// <summary>
		/// Gets or sets the value how long script can execute in one tick without getting terminated after the tick ends.
		/// </summary>
		public uint ScriptTimeoutThreshold { get; set; }


		/// <summary>
		/// Initializes the script domain.
		/// </summary>
		/// <param name="gameDir">The game folder.</param>
		/// <param name="loadApi"><c>false</c> in tests, which have no RDR2Coop.Api.dll.</param>
		internal ScriptDomain(string gameDir, bool loadApi)
		{
			GameDirectory = gameDir;
			CurrentDomain = this;

			AppDomain.UnhandledException += HandleUnhandledException;

			// Initialize and scan memory at a predictable point
			System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(NativeMemory).TypeHandle);

			if (!loadApi)
			{
				return;
			}

			// The API goes into the default context, once per process: one RDR2.Script type for the process lifetime, reloads included
			string apiPath = Path.Combine(ScriptPath, "RDR2Coop.Api.dll");
			Log.Message(Log.Level.Debug, "Loading API from ", apiPath, " ...");

			try
			{
				AssemblyLoadContext.Default.LoadFromAssemblyPath(apiPath);
			}
			catch (Exception ex)
			{
				Log.Message(Log.Level.Error, "Unable to load ", Path.GetFileName(apiPath), ": ", ex.ToString());
			}
		}

		~ScriptDomain()
		{
			DisposeUnmanagedResource();
		}
		public void Dispose()
		{
			AppDomain.UnhandledException -= HandleUnhandledException; // the next domain subscribes again
			DisposeUnmanagedResource();
			GC.SuppressFinalize(this);
		}

		private void DisposeUnmanagedResource()
		{
			// Need to free native strings when disposing the script domain
			CleanupStrings();
			// Need to free unmanaged resources in NativeMemory
			NativeMemory.DisposeUnmanagedResources();
		}

		/// <summary>
		/// Unloads scripts and destroys an existing script domain.
		/// </summary>
		/// <param name="domain">The script domain to unload.</param>
		/// <returns>The unloaded scripts' load context, weakly: <see cref="WaitCollected"/> waits for it to be gone.</returns>
		public static WeakReference Unload(ScriptDomain domain)
		{
			Log.Message(Log.Level.Info, "Unloading script domain ...");

			domain.Abort();
			domain.Dispose();
			ClearShared();

			try
			{
				domain._alc.Unload();
			}
			catch (Exception ex)
			{
				Log.Message(Log.Level.Error, "Failed to unload script domain: ", ex.ToString());
			}
			if (CurrentDomain == domain) CurrentDomain = null;   // or it keeps the old scripts' context alive
			return new WeakReference(domain._alc);
		}

		// AppDomain slots the scripts share (co-op mod and enRage's co-op patch) that hold the unloaded scripts' functions
		// or say which enRage copy is the live one. AppDomain data outlives the scripts' context: a delegate left there
		// kept the whole old context (both mods) in memory after Insert, and enRage then counted it as another copy
		// ("two copies of enRage"). The next scripts set them again.
		static readonly string[] SharedKeys = { "rdr2coop.nameOf", "rdr2coop.encounterResult", "rdr2coop.nemesesFor", "enRAGE.active" };

		static void ClearShared()
		{
			foreach (var key in SharedKeys)
				try { AppDomain.CurrentDomain.SetData(key, null); } catch (Exception) { }
		}

		/// <summary>
		/// Collects until the unloaded scripts are really gone from the process (a collectible context only goes at a
		/// garbage collection). Without it the next load sees the old assemblies still listed: enRage takes its own last
		/// copy for a second install and warns "two copies of enRage" after Insert. True when they went.
		/// </summary>
		public static bool WaitCollected(WeakReference old, int tries = 10)
		{
			for (int i = 0; i < tries && old.IsAlive; i++)
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
			Log.Message(old.IsAlive ? Log.Level.Warning : Log.Level.Debug, old.IsAlive
				? "The unloaded scripts are still in memory (something holds on to them): enRage may see its old copy"
				: "The unloaded scripts are gone from memory");
			return !old.IsAlive;
		}



		/// <summary>
		/// Creates a new script domain.
		/// </summary>
		/// <param name="gameDir">The game folder; scripts are loaded from its RDR2Coop subfolder.</param>
		/// <returns>The script domain or <c>null</c> in case of failure.</returns>
		public static ScriptDomain Load(string gameDir)
		{
			try
			{
				return new ScriptDomain(gameDir, true);
			}
			catch (Exception ex)
			{
				Log.Message(Log.Level.Error, "Failed to create script domain: ", ex.ToString());
				return null;
			}
		}

		/// <summary>
		/// Loads scripts from the specified assembly file.
		/// </summary>
		/// <param name="filename">The path to the assembly file to load.</param>
		/// <returns><c>true</c> on success, <c>false</c> otherwise</returns>
		private bool LoadScriptsFromAssembly(string filename)
		{
			if (!ReferencesApi(filename))
			{
				return false;
			}

			Log.Message(Log.Level.Debug, "Loading assembly ", Path.GetFileName(filename), " ...");

			Assembly assembly = null;

			try
			{
				// Load from bytes so the file stays unlocked: the mod's updater overwrites it in place while the game runs
				string pdb = Path.ChangeExtension(filename, ".pdb");
				byte[] dll = EnragePatch.ForLoad(filename, File.ReadAllBytes(filename), Path.Combine(ScriptPath, "enrage-coop-patch"), m => Log.Message(Log.Level.Info, m));
				using var dllStream = new MemoryStream(dll);
				using var pdbStream = File.Exists(pdb) ? new MemoryStream(File.ReadAllBytes(pdb)) : null;
				assembly = _alc.LoadFromStream(dllStream, pdbStream);
			}
			catch (Exception ex)
			{
				Log.Message(Log.Level.Error, "Unable to load ", Path.GetFileName(filename), ": ", ex.ToString());
				return false;
			}

			return LoadScriptsFromAssembly(assembly, filename);
		}
		/// <summary>
		/// Loads scripts from the specified assembly object.
		/// </summary>
		/// <param name="filename">The path to the file associated with this assembly.</param>
		/// <param name="assembly">The assembly to load.</param>
		/// <returns><c>true</c> on success, <c>false</c> otherwise</returns>
		private bool LoadScriptsFromAssembly(Assembly assembly, string filename)
		{
			int count = 0;
			Version apiVersion = null;

			try
			{
				// Find all script types in the assembly
				foreach (var type in assembly.GetTypes().Where(x => IsSubclassOf(x, "RDR2.Script")))
				{
					count++;

					// This function builds a composite key of all dependencies of a script
					Func<Type, string, string> BuildComparisonString = null;
					BuildComparisonString = (a, b) =>
					{
						b = a.FullName + "%%" + b;
						foreach (var attribute in a.GetCustomAttributesData().Where(x => x.AttributeType.FullName == "RDR2.RequireScript"))
						{
							var dependency = attribute.ConstructorArguments[0].Value as Type;
							// Ignore circular dependencies
							if (dependency != null && !b.Contains("%%" + dependency.FullName))
								b = BuildComparisonString(dependency, b);
						}
						return b;
					};

					var key = BuildComparisonString(type, string.Empty);
					key = assembly.GetName().Name + "-" + assembly.GetName().Version + key;

					if (_scriptTypes.ContainsKey(key))
					{
						Log.Message(Log.Level.Warning, "The script name ", type.FullName, " already exists and was loaded from ", Path.GetFileName(_scriptTypes[key].Item1), ". Ignoring occurrence loaded from ", Path.GetFileName(filename), ".");
						continue; // Skip types that were already added previously are ignored
					}

					_scriptTypes.Add(key, new Tuple<string, Type>(filename, type));

					// Check API version for one of the types (should be the same for all)
					if (apiVersion == null)
					{
						apiVersion = type.BaseType.Assembly.GetName().Version;
					}
				}
			}
			catch (ReflectionTypeLoadException ex)
			{
				// Filter out failure if unable to resolve RDR2DN API, since this was already logged in 'HandleResolve'
				var fileNotFoundException = ex.LoaderExceptions[0] as FileNotFoundException;
				if (fileNotFoundException == null || fileNotFoundException.Message.IndexOf("RDR2Coop.Api", StringComparison.OrdinalIgnoreCase) < 0)
				{
					Log.Message(Log.Level.Error, "Failed to load assembly ", Path.GetFileName(filename), ": ", ex.LoaderExceptions[0].ToString());
				}

				return false;
			}

			Log.Message(Log.Level.Info, "Found ", count.ToString(), " script(s) in ", Path.GetFileName(filename), (apiVersion != null ? " resolved to API " + apiVersion.ToString(3) : string.Empty), ".");

			return count != 0;
		}

		/// <summary>
		/// Creates an instance of a script.
		/// </summary>
		/// <param name="scriptType">The type of the script to instantiate.</param>
		/// <returns>The script instance or <c>null</c> in case of failure.</returns>
		public Script InstantiateScript(Type scriptType)
		{
			if (scriptType.IsAbstract || !IsSubclassOf(scriptType, "RDR2.Script"))
			{
				return null;
			}

			Log.Message(Log.Level.Debug, "Instantiating script ", scriptType.FullName, " ...");

			Script script = new Script();
			// Keep track of current script, so it can be restored down below
			Script previousScript = _executingScript;

			_executingScript = script;

			// Create a name for the new script instance
			if (_scriptInstances.ContainsKey(scriptType.FullName))
			{
				int instanceIndex = _scriptInstances[scriptType.FullName] + 1;
				_scriptInstances[scriptType.FullName] = instanceIndex;

				script.Name = scriptType.FullName + instanceIndex.ToString();
			}
			else
			{
				_scriptInstances.Add(scriptType.FullName, 0);

				// Do not append instance index to the default instance name
				script.Name = scriptType.FullName;
			}

			script.FileName = LookupScriptFilename(scriptType);

			try
			{
				script.ScriptInstance = Activator.CreateInstance(scriptType);
			}
			catch (MissingMethodException)
			{
				Log.Message(Log.Level.Error, "Failed to instantiate script ", scriptType.FullName, " because no public default constructor was found.");
				return null;
			}
			catch (TargetInvocationException ex)
			{
				Log.Message(Log.Level.Error, "Failed to instantiate script ", scriptType.FullName, " because constructor threw an exception: ", ex.InnerException.ToString());
				return null;
			}
			catch (Exception ex)
			{
				Log.Message(Log.Level.Error, "Failed to instantiate script ", scriptType.FullName, ": ", ex.ToString());

				if (GetScriptAttribute(scriptType, "SupportURL") is string supportURL)
				{
					Log.Message(Log.Level.Error, "Please check the following site for support on the issue: ", supportURL);
				}

				return null;
			}

			_runningScripts.Add(script);

			// Restore previously executing script
			_executingScript = previousScript;

			return script;
		}

		/// <summary>
		/// Loads and starts all scripts.
		/// </summary>
		public void Start()
		{
			if (_scriptTypes.Count != 0 || _runningScripts.Count != 0)
			{
				Log.Message(Log.Level.Error, "cannot start scriptdomain if scripts are already running");
				return; // Cannot start script domain if scripts are already running
			}

			Log.Message(Log.Level.Debug, "Loading scripts from ", ScriptPath, " ...");

			if (!Directory.Exists(ScriptPath))
			{
				Log.Message(Log.Level.Warning, "Failed to reload scripts because the ", ScriptPath, " directory is missing.");
				return;
			}

			// Only this folder (runtime/ below it holds the framework), only DLLs built against the API, never Core or the API itself
			try
			{
				foreach (var filename in Directory.GetFiles(ScriptPath, "*.dll", SearchOption.TopDirectoryOnly))
				{
					string name = Path.GetFileName(filename);
					if (name.Equals("RDR2Coop.Core.dll", StringComparison.OrdinalIgnoreCase) || name.Equals("RDR2Coop.Api.dll", StringComparison.OrdinalIgnoreCase)
						|| name.Equals("ScriptHookRDRNetAPI.dll", StringComparison.OrdinalIgnoreCase) || name.Equals("System.Windows.Forms.dll", StringComparison.OrdinalIgnoreCase))   // api/Compat forwarders
					{
						continue;
					}

					LoadScriptsFromAssembly(filename);
				}
				LoadScriptHookDotNetScripts();
			}
			catch (Exception ex)
			{
				Log.Message(Log.Level.Error, "Failed to reload scripts: ", ex.ToString());
			}

			// Instantiate scripts after they were all loaded, so that dependencies are launched with the right ordering
			foreach (var type in _scriptTypes.Values.Select(x => x.Item2))
			{
				// Start the script unless script does not want a default instance
				if (!(GetScriptAttribute(type, "NoDefaultInstance") is bool NoDefaultInstance) || !NoDefaultInstance)
				{
					InstantiateScript(type)?.Start();
				}
			}
		}
		/// <summary>
		/// Loads and starts all scripts in the specified file.
		/// </summary>
		/// <param name="filename"></param>
		public void StartScripts(string filename)
		{
			filename = Path.GetFullPath(filename);

			if (!Path.GetExtension(filename).Equals(".dll", StringComparison.OrdinalIgnoreCase) || !LoadScriptsFromAssembly(filename))
			{
				return;
			}

			// Instantiate only those scripts that are from the this assembly
			foreach (var type in _scriptTypes.Values.Where(x => x.Item1 == filename).Select(x => x.Item2))
			{
				// Make sure there are no others instances of this script
				_runningScripts.RemoveAll(x => x.FileName == filename && x.ScriptInstance.GetType() == type);

				// Start the script unless script does not want a default instance
				if (!(GetScriptAttribute(type, "NoDefaultInstance") is bool NoDefaultInstance) || !NoDefaultInstance)
				{
					InstantiateScript(type)?.Start();
				}
			}
		}
		/// <summary>
		/// Aborts all running scripts.
		/// </summary>
		public void Abort()
		{
			foreach (Script script in _runningScripts)
			{
				script.Abort();
			}

			_scriptTypes.Clear();
			_runningScripts.Clear();
		}
		/// <summary>
		/// Aborts all running scripts from the specified file.
		/// </summary>
		/// <param name="filename"></param>
		public void AbortScripts(string filename)
		{
			filename = Path.GetFullPath(filename);

			foreach (Script script in _runningScripts.Where(x => filename.Equals(x.FileName, StringComparison.OrdinalIgnoreCase)))
			{
				script.Abort();
			}
		}

		/// <summary>
		/// Execute a script task in this script domain.
		/// </summary>
		/// <param name="task">The task to execute.</param>
		public void ExecuteTask(IScriptTask task)
		{
			if (Thread.CurrentThread.ManagedThreadId == _executingThreadId)
			{
				// Request came from the main thread, so can just execute it right away
				task.Run();
			}
			else
			{
				// Request came from the script thread, so need to pass it to the domain thread and execute there.
				// Only the script the main thread is parked on may enqueue: an aborted thread still running user
				// code (timeout, or a reload whose abort the script swallowed) unwinds here instead.
				Script script = Script.Current;
				if (script == null || script != _executingScript || !script.IsRunning)
				{
					throw new ScriptAbortedException(); // an aborted script must not run tasks or read their results
				}

				_taskQueue.Enqueue(task);

				SignalAndWait(script._waitEvent, script._continueEvent);

				if (!script.IsRunning)
				{
					throw new ScriptAbortedException();
				}
			}
		}

		/// <summary>
		/// Gets the key down status of the specified key.
		/// </summary>
		/// <param name="key">The key to check.</param>
		/// <returns><c>true</c> if the key is currently pressed or <c>false</c> otherwise</returns>
		public bool IsKeyPressed(Keys key)
		{
			return _keyboardState[(int)key];
		}
		/// <summary>
		/// Pauses or resumes handling of keyboard events in this script domain.
		/// </summary>
		/// <param name="pause"><c>true</c> to pause or <c>false</c> to resume</param>
		public void PauseKeyEvents(bool pause)
		{
			_recordKeyboardEvents = !pause;
		}

		/// <summary>
		/// Main execution logic of the script domain.
		/// </summary>
		public void DoTick()
		{
			// Execute running scripts
			for (int i = 0; i < _runningScripts.Count; i++)
			{
				Script script = _runningScripts[i];

				// Ignore terminated scripts
				if (!script.IsRunning || script.IsPaused)
				{
					continue;
				}

				_executingScript = script;

				bool finishedInTime = true;

				try
				{
					// Resume script thread and execute any incoming tasks from it
					while ((finishedInTime = SignalAndWait(script._continueEvent, script._waitEvent, ScriptTimeoutThreshold)) && _taskQueue.Count > 0)
					{
						_taskQueue.Dequeue().Run();
					}
				}
				catch (Exception ex)
				{
					HandleUnhandledException(script, new UnhandledExceptionEventArgs(ex, true));

					// Stop script in case of an unhandled exception during task execution
					script.Abort();
				}

				_executingScript = null;

				// Tolerate long execution time if a debugger is attached since some script may be debugged using breakpoints
				if (!finishedInTime && !(OperatingSystem.IsWindows() && IsDebuggerPresent())) // the Windows guard lets CoreTests run on Linux
				{
					Log.Message(Log.Level.Error, "Script '", script.Name, "' is not responding! Aborting ...");

					// Wait operation above timed out, which means that the script did not send any task for some time, so abort it
					script.Abort();
					continue;
				}
			}

			// Clean up any pinned strings of this frame
			CleanupStrings();
		}
		/// <summary>
		/// Keyboard handling logic of the script domain.
		/// </summary>
		/// <param name="keys">The key that was originated this event and its modifiers.</param>
		/// <param name="status"><c>true</c> on a key down, <c>false</c> on a key up event.</param>
		public void DoKeyEvent(Keys keys, bool status)
		{
			var e = new KeyEventArgs(keys);

			// Only update state of the primary key (without modifiers) here
			_keyboardState[(int)e.KeyCode] = status;

			if (_recordKeyboardEvents)
			{
				var eventinfo = new Tuple<bool, KeyEventArgs>(status, e);

				foreach (Script script in _runningScripts)
				{
					script._keyboardEvents.Enqueue(eventinfo);
				}
			}
		}

		/// <summary>
		/// Free memory for all pinned strings.
		/// </summary>
		private void CleanupStrings()
		{
			foreach (IntPtr handle in _pinnedStrings)
			{
				Marshal.FreeCoTaskMem(handle);
			}

			_pinnedStrings.Clear();
		}
		/// <summary>
		/// Pins the memory of a string so that it can be used in native calls without worrying about the GC invalidating its pointer.
		/// </summary>
		/// <param name="str">The string to pin to a fixed pointer.</param>
		/// <returns>A pointer to the pinned memory containing the string.</returns>
		public IntPtr PinString(string str)
		{
			if (Script.Current != null && !Script.Current.IsRunning)
			{
				throw new ScriptAbortedException(); // _pinnedStrings is the main thread's; a dead script thread must not touch it
			}

			IntPtr handle = RDR2DN.NativeMemory.StringToCoTaskMemUTF8(str);

			if (handle == IntPtr.Zero)
			{
				return NativeMemory.NullString;
			}
			else
			{
				_pinnedStrings.Add(handle);
				return handle;
			}
		}

		/// <summary>
		/// Finds the script object representing the specified <paramref name="scriptInstance"/> object.
		/// </summary>
		/// <param name="scriptInstance">The 'RDR2.Script' instance to check.</param>
		public Script LookupScript(object scriptInstance)
		{
			if (scriptInstance == null)
			{
				return null;
			}

			// Return matching script in running script list if one is found
			var script = _runningScripts.Where(x => x.ScriptInstance == scriptInstance).FirstOrDefault();

			// Otherwise return the executing script, since during constructor execution the running script list was not yet updated
			if (script == null && _executingScript != null && _executingScript.ScriptInstance == null)
			{
				// Handle the case where a script creates a custom instance of a script class that is not managed by RDR2DN
				// These may attempt to set events, but are not allowed to do so, since RDR2DN will never call them, so just return null
				if (!_executingScript.Name.Contains(scriptInstance.GetType().FullName))
				{
					Log.Message(Log.Level.Warning, "A script tried to use a custom script instance of type ", scriptInstance.GetType().FullName, " that was not instantiated by ScriptHookRDRDotNet.");
					return null;
				}

				script = _executingScript;
			}

			return script;
		}
		public string LookupScriptFilename(Type scriptType)
		{
			return _scriptTypes.Values.FirstOrDefault(x => x.Item2 == scriptType)?.Item1 ?? string.Empty;
		}

		/// <summary>
		/// Checks if the script has a 'GTA.ScriptAttributes' attribute with the specified argument attached to it and returns it.
		/// </summary>
		/// <param name="scriptType">The script type to check for the attribute.</param>
		/// <param name="name">The named argument to search.</param>
		private static object GetScriptAttribute(Type scriptType, string name)
		{
			var attribute = scriptType.GetCustomAttributesData().Where(x => x.AttributeType.FullName == "RDR2.ScriptAttributes").FirstOrDefault();

			if (attribute != null)
			{
				foreach (var arg in attribute.NamedArguments)
				{
					if (arg.MemberName == name)
					{
						return arg.TypedValue.Value;
					}
				}
			}

			return null;
		}

		private static void SignalAndWait(SemaphoreSlim toSignal, SemaphoreSlim toWaitOn)
		{
			toSignal.Release();
			toWaitOn.Wait();
		}
		private static bool SignalAndWait(SemaphoreSlim toSignal, SemaphoreSlim toWaitOn, uint timeout)
		{
			toSignal.Release();
			return toWaitOn.Wait((int)timeout);
		}

		private static bool IsSubclassOf(Type type, string baseTypeName)
		{
			for (Type t = type.BaseType; t != null; t = t.BaseType)
			{
				if (t.FullName == baseTypeName)
				{
					return true;
				}
			}

			return false;
		}

		static bool ReferencesApi(string filename) => References(filename, "RDR2Coop.Api") || References(filename, "ScriptHookRDRNetAPI");

		/// <summary>
		/// ScriptHookRDR2DotNet scripts in &lt;game&gt;\scripts (enRage): run here when ScriptHookRDRDotNet.asi isn't installed, so
		/// they share this runtime with the mod (the co-op link to enRage is in-process data). With it installed they're
		/// its, and nothing in scripts\ is touched. Never scripts\RDR2Coop.dll: the mod's old ScriptHookRDR2DotNet copy.
		/// </summary>
		/// <summary>
		/// Why a ScriptHookRDR2DotNet script in scripts\ isn't run here, or null to run it.
		/// </summary>
		internal static string ShdnSkipReason(string name, bool shdnInstalled) =>
			name.Equals("RDR2Coop.dll", StringComparison.OrdinalIgnoreCase) ? "the mod's old ScriptHookRDR2DotNet copy, not loaded (this loader runs RDR2Coop\\RDR2Coop.dll)"
			: !shdnInstalled ? null
			: "ScriptHookRDRDotNet.asi is installed and runs it, not this loader" + (name.StartsWith("NemesisMod", StringComparison.OrdinalIgnoreCase)
				? " (the co-op link to enRage needs it to run here: remove ScriptHookRDRDotNet.asi)" : "");

		void LoadScriptHookDotNetScripts()
		{
			string dir = Path.Combine(GameDirectory, "scripts");
			if (!Directory.Exists(dir))
			{
				return;
			}
			bool shdn = File.Exists(Path.Combine(GameDirectory, "ScriptHookRDRDotNet.asi"));
			foreach (var filename in Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
			{
				string name = Path.GetFileName(filename);
				if (!References(filename, "ScriptHookRDRNetAPI"))
				{
					continue;   // a dependency (Newtonsoft.Json) or not a script
				}
				string skip = ShdnSkipReason(name, shdn);
				Log.Message(skip == null ? Log.Level.Info : Log.Level.Warning, "scripts\\", name, ": ", skip ?? "a ScriptHookRDR2DotNet script, run in this loader");
				if (skip == null)
				{
					LoadScriptsFromAssembly(filename);
				}
			}
		}

		/// <summary>
		/// Checks from the PE metadata alone (nothing is loaded) whether an assembly file references the named assembly.
		/// </summary>
		internal static bool References(string filename, string assemblyName)
		{
			try
			{
				using var pe = new PEReader(File.OpenRead(filename));
				if (!pe.HasMetadata)
				{
					return false;
				}

				var md = pe.GetMetadataReader();
				return md.AssemblyReferences.Any(h => md.GetString(md.GetAssemblyReference(h).Name) == assemblyName);
			}
			catch (BadImageFormatException)
			{
				Log.Message(Log.Level.Warning, Path.GetFileName(filename), " is not a valid assembly.");
				return false;
			}
		}

		public static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
		{
			Log.Message(Log.Level.Error, args.IsTerminating ? "Caught fatal unhandled exception:" : "Caught unhandled exception:", Environment.NewLine, args.ExceptionObject.ToString());

			if (sender is Script script)
			{
				Log.Message(Log.Level.Error, "The exception was thrown while executing the script ", script.Name, ".");

				if (GetScriptAttribute(script.ScriptInstance.GetType(), "SupportURL") is string supportURL)
				{
					Log.Message(Log.Level.Error, "Please check the following site for support on the issue: ", supportURL);
				}	

				// Show a notification with the script crash information
				var domain = ScriptDomain.CurrentDomain;
				if (domain != null && domain._executingScript != null && !args.IsTerminating)
				{
					unsafe
					{
					}
				}
			}
		}
	}
}
