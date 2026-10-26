# Compat: running ScriptHookRDR2DotNet scripts on the .NET 9 loader

ScriptHookRDR2DotNet scripts (enRage) are built against `ScriptHookRDRNetAPI` 2.2.0 and
`System.Windows.Forms` 4.0. .NET 9 only accepts an assembly with the requested name, so these two
projects are tiny assemblies with those names whose types are forwarded to ours: the API port
(`RDR2Coop.Api`, same namespaces, version 2.2.0) and the WinForms shim in `RDR2Coop.Core` (Keys,
KeyEventArgs, KeyEventHandler, Control.ModifierKeys). They ship in `RDR2Coop\` beside the API, with
`System.Diagnostics.EventLog.dll` (Microsoft, MIT; enRage reads the Windows event log at startup
after a crash and .NET 9 doesn't include it). Core loads the scripts from `scripts\` when
ScriptHookRDRDotNet.asi isn't installed (api/Core/ScriptDomain.cs, LoadScriptHookDotNetScripts).

`Forwards.cs` in each project is generated; after changing the API's public types run
`tools/gen-compat-forwards.sh` (CoreTests check that every public API type is forwarded).
