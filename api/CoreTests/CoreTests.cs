// Tests for RDR2Coop.Core that need no game: the script scheduler, cooperative abort, key dispatch, the
// metadata reference check and string splitting. Same Run/Check style as tests/NetTests.cs; no test framework.
// Run: dotnet build api/CoreTests -c Release && dotnet api/CoreTests/bin/Release/net9.0/CoreTests.dll

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RDR2DN;

// ScriptDomain recognises scripts by base type name "RDR2.Script"; this stands in for the Api's class.
namespace RDR2 { public class Script { } }

namespace CoreTests
{
	class ScriptA : RDR2.Script { }
	class ScriptB : RDR2.Script { }

	static class Program
	{
		static int _failed, _passed;
		static string _tmp;

		static int Main()
		{
			_tmp = Path.Combine(Path.GetTempPath(), "rdr2coop-coretests-" + Environment.ProcessId);
			Directory.CreateDirectory(Path.Combine(_tmp, "scripts"));
			Directory.CreateDirectory(Path.Combine(_tmp, "RDR2Coop"));
			Log.Directory = Path.Combine(_tmp, "scripts");
			Script.TextPoolInitEnabled = false;

			Run("handshake: one DoTick = one step of the script", Handshake);
			Run("interval is honoured", Interval);
			Run("abort from the main thread unwinds a parked script", AbortFromMain);
			Run("abort from inside Tick stops user code at the call", AbortInsideTick);
			Run("a script that never yields times out; the next script still ticks", Timeout);
			Run("key events reach KeyDown with modifiers; pause suppresses; IsKeyPressed tracks", Keys);
			Run("References reads the PE metadata only", References);
			Run("ScriptHookRDR2DotNet scripts in scripts\\ run here only without ScriptHookRDRDotNet.asi, never the old mod copy", ShdnSkip);
			Run("the enRage co-op patch is applied in memory to the official enRage only", EnrageInMemory);
			Run("the real enRage co-op patch rebuilds the patched enRage (ENRAGE_OFFICIAL + ENRAGE_PATCH_DIR, else skipped)", EnrageReal);
			Run("PushLongString splits at 99 UTF-8 bytes", PushLongString);
			Run("Unload aborts the scripts and the domain can be recreated", Unload);
			Run("a thread that swallowed its abort cannot join the new domain's handshake", StaleThread);
			Run("a swallowed abort still ends the script thread", SwallowedAbort);

			Console.WriteLine($"{_passed} passed, {_failed} failed");
			try { Directory.Delete(_tmp, true); } catch { }
			return _failed == 0 ? 0 : 1;
		}

		static void Run(string name, Action test)
		{
			try { test(); _passed++; Console.WriteLine("  ok   " + name); }
			catch (Exception e) { _failed++; Console.WriteLine("  FAIL " + name + ": " + e.Message); }
		}

		static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }

		static void ShdnSkip()
		{
			Check(ScriptDomain.ShdnSkipReason("NemesisMod.dll", false) == null, "enRage runs here without ScriptHookRDRDotNet.asi");
			Check(ScriptDomain.ShdnSkipReason("NemesisMod.dll", true)?.Contains("enRage") == true, "with it installed, enRage stays its, with the hint");
			Check(ScriptDomain.ShdnSkipReason("Other.dll", true) != null, "any script stays ScriptHookRDRDotNet's when it's installed");
			Check(ScriptDomain.ShdnSkipReason("rdr2coop.DLL", false) != null, "never the mod's old copy, any case");
		}

		// A small patch made with the same bsdiff4 + raw-deflate steps as tools/make-release.sh.
		const string PatchB64 = "UkRDT1BBVDEJAQAAAAAAABgAAAAAAAAABgAAAAAAAAA3AAAAAAAAAGNngABWKM0BpTWhtB4qv4GQuBqUngalAWNgGGgAAEvO180vKMpMzyhRKMhJrEwt0lFIzEtRKEgsKslLLSpWSM4vzSvRU0jNC0pMTwXyaKC6JDEzBwA=";

		static string Sha(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant();

		static string Repeat(string s, int n) => string.Concat(System.Linq.Enumerable.Repeat(s, n));

		static void EnrageInMemory()
		{
			byte[] official = Encoding.ASCII.GetBytes(Repeat("enRage official build: nemeses hold grudges against the player. ", 4));
			byte[] coop = Encoding.ASCII.GetBytes(Repeat("enRage co-op build: nemeses hold grudges against the right player, and partners count. ", 3) + "tail");
			byte[] patch = Convert.FromBase64String(PatchB64);
			Check(Sha(EnragePatch.Apply(official, patch)) == Sha(coop), "Apply rebuilds the patched file");

			string dir = Path.Combine(_tmp, "enrage"), scripts = Path.Combine(dir, "scripts"), pd = Path.Combine(dir, "patch");
			Directory.CreateDirectory(scripts);
			string dll = Path.Combine(scripts, "NemesisMod.dll"), ini = Path.Combine(scripts, "NemesisMod.ini");
			var said = new List<string>();
			Check(EnragePatch.ForLoad(dll, official, pd, said.Add) == official && said[0].Contains("no co-op patch"), "no patch in the mod: unchanged, and said");
			Directory.CreateDirectory(pd);
			File.WriteAllBytes(Path.Combine(pd, "NemesisMod.coop.patch"), patch);
			File.WriteAllText(Path.Combine(pd, "patch.txt"), $"patch_version=0.4.2\nenrage_version=0.9.5.2\nbase_sha256={Sha(official)}\npatched_sha256={Sha(coop)}\n");
			File.WriteAllText(ini, "[Menu]\r\nPauseWorld=True\r\nOther=1\r\n");
			Check(Sha(EnragePatch.ForLoad(dll, official, pd, said.Add)) == Sha(coop), "the official enRage loads patched");
			Check(File.ReadAllText(ini).Contains("PauseWorld=False") && File.ReadAllText(ini).Contains("Other=1"), "PauseWorld switched off, the rest kept");
			Check(!File.Exists(dll), "the player's file isn't written");
			Check(EnragePatch.ForLoad(dll, coop, pd, said.Add) == coop && said[^1].Contains("already"), "a copy the installer patched loads as it is");
			byte[] other = Encoding.ASCII.GetBytes("enRage 0.9.6");
			Check(EnragePatch.ForLoad(dll, other, pd, said.Add) == other && said[^1].Contains("0.9.5.2"), "another version: unchanged, and the log names the right one");
			Check(EnragePatch.ForLoad(Path.Combine(scripts, "Other.dll"), official, pd, said.Add) == official, "only NemesisMod.dll");
			File.Delete(ini);
			Check(EnragePatch.NoMenuPause(ini) && File.ReadAllText(ini).Contains("[Menu]"), "no ini yet: one with PauseWorld=False");
			Check(!EnragePatch.NoMenuPause(ini), "already off: left alone");
		}

		static void EnrageReal()
		{
			string official = Environment.GetEnvironmentVariable("ENRAGE_OFFICIAL"), pd = Environment.GetEnvironmentVariable("ENRAGE_PATCH_DIR");
			if (string.IsNullOrEmpty(official) || string.IsNullOrEmpty(pd)) return;
			var said = new List<string>();
			string scripts = Path.Combine(_tmp, "real", "scripts");
			Directory.CreateDirectory(scripts);
			byte[] dll = File.ReadAllBytes(official), loaded = EnragePatch.ForLoad(Path.Combine(scripts, "NemesisMod.dll"), dll, pd, said.Add);
			Check(loaded != dll && said[0].Contains("applied in memory"), "patched: " + string.Join(" | ", said));
		}

		static ScriptDomain NewDomain()
		{
			var d = new ScriptDomain(_tmp, false);
			d.ScriptTimeoutThreshold = 5000;
			return d;
		}

		static Script StartScript(ScriptDomain d, Type type, EventHandler tick)
		{
			var s = d.InstantiateScript(type);
			Check(s != null, "InstantiateScript returned null");
			s.Tick += tick;
			s.Start();
			Check(SpinUntil(() => s.IsRunning, 1000), "script thread did not start");
			return s;
		}

		static bool SpinUntil(Func<bool> cond, int ms)
		{
			var end = DateTime.UtcNow.AddMilliseconds(ms);
			while (!cond()) { if (DateTime.UtcNow > end) return false; Thread.Sleep(1); }
			return true;
		}

		static void Handshake()
		{
			var d = NewDomain();
			int ticks = 0, steps = 0;
			Script s = null;
			s = StartScript(d, typeof(ScriptA), (o, e) => { ticks++; s.Wait(0); steps++; s.Wait(0); steps++; s.Wait(0); steps++; });

			for (int i = 0; i < 4; i++) d.DoTick();
			Check(ticks == 1 && steps == 3, $"after 4 DoTicks: ticks {ticks}, steps {steps}");
			d.DoTick();
			Check(ticks == 2 && steps == 3, $"after 5 DoTicks: ticks {ticks}, steps {steps}");
			ScriptDomain.Unload(d);
		}

		static void Interval()
		{
			var d = NewDomain();
			int ticks = 0;
			var s = StartScript(d, typeof(ScriptA), (o, e) => ticks++);
			s.Interval = 150;
			d.DoTick();
			Check(ticks == 1, "first tick");
			for (int i = 0; i < 5; i++) d.DoTick();
			Check(ticks == 1, "ticked again before the interval passed");
			Thread.Sleep(200);
			d.DoTick();
			Check(ticks == 2, "did not tick after the interval passed");
			ScriptDomain.Unload(d);
		}

		static void AbortFromMain()
		{
			var d = NewDomain();
			int aborted = 0; bool exited = false;
			Script s = null;
			s = StartScript(d, typeof(ScriptA), (o, e) => { try { s.Wait(0); } finally { exited = true; } });
			s.Aborted += (o, e) => aborted++;
			d.DoTick(); // the script is now parked in Wait
			s.Abort();
			Check(!s.IsRunning, "IsRunning still true");
			Check(aborted == 1, "Aborted fired " + aborted + " times");
			Check(SpinUntil(() => exited, 100), "script thread did not unwind within 100 ms");
			ScriptDomain.Unload(d);
		}

		sealed class CountTask : IScriptTask { public int Runs; public void Run() { Runs++; } }

		static void AbortInsideTick()
		{
			var d = NewDomain();
			bool after = false, tickEnded = false;
			var task = new CountTask();
			Script s = null;
			s = StartScript(d, typeof(ScriptA), (o, e) => { try { s.Abort(); after = true; } finally { tickEnded = true; } });
			s.Aborted += (o, e) => d.ExecuteTask(task); // the mod's OnAborted calls natives from the script thread
			d.DoTick();
			Check(SpinUntil(() => tickEnded, 100), "Tick did not finish");
			Check(!after, "user code after Abort() ran");
			Check(!s.IsRunning, "IsRunning still true");
			Check(task.Runs == 1, "the Aborted handler's task ran " + task.Runs + " times");
			ScriptDomain.Unload(d);
		}

		static void Timeout()
		{
			var d = NewDomain();
			d.ScriptTimeoutThreshold = 50;
			bool stop = false, exited = false; int bTicks = 0;
			Script a = null;
			a = StartScript(d, typeof(ScriptA), (o, e) => { try { while (!stop) Thread.Sleep(1); a.Wait(0); } finally { exited = true; } });
			var b = StartScript(d, typeof(ScriptB), (o, e) => bTicks++);
			var t0 = DateTime.UtcNow;
			d.DoTick();
			Check(!a.IsRunning, "runaway script not aborted");
			Check((DateTime.UtcNow - t0).TotalMilliseconds < 2000, "DoTick took too long");
			Check(bTicks == 1, "second script did not tick in the same DoTick");
			stop = true;
			Check(SpinUntil(() => exited, 500), "runaway thread did not exit once it yielded");
			ScriptDomain.Unload(d);
		}

		static void Keys()
		{
			var d = NewDomain();
			var seen = new List<KeyEventArgs>();
			var s = StartScript(d, typeof(ScriptA), (o, e) => { });
			s.KeyDown += (o, e) => seen.Add(e);
			d.DoTick();

			d.DoKeyEvent(System.Windows.Forms.Keys.F10 | System.Windows.Forms.Keys.Control, true);
			d.DoTick();
			Check(seen.Count == 1 && seen[0].KeyCode == System.Windows.Forms.Keys.F10 && seen[0].Control && !seen[0].Alt && !seen[0].Shift, "Ctrl+F10 not seen as such");
			Check(d.IsKeyPressed(System.Windows.Forms.Keys.F10), "F10 not pressed");
			d.DoKeyEvent(System.Windows.Forms.Keys.F10, false);
			Check(!d.IsKeyPressed(System.Windows.Forms.Keys.F10), "F10 still pressed after key-up");

			d.PauseKeyEvents(true);
			d.DoKeyEvent(System.Windows.Forms.Keys.F9, true);
			d.DoTick();
			Check(seen.Count == 1, "paused key event was dispatched");
			Check(d.IsKeyPressed(System.Windows.Forms.Keys.F9), "key state not tracked while paused");
			d.PauseKeyEvents(false);
			d.DoKeyEvent(System.Windows.Forms.Keys.F9, true);
			d.DoTick();
			Check(seen.Count == 2, "key event after resume not dispatched");
			ScriptDomain.Unload(d);
		}

		static void References()
		{
			string tests = typeof(Program).Assembly.Location;
			string core = typeof(ScriptDomain).Assembly.Location;
			Check(ScriptDomain.References(tests, "RDR2Coop.Core"), "CoreTests.dll references RDR2Coop.Core");
			Check(!ScriptDomain.References(core, "RDR2Coop.Api"), "RDR2Coop.Core.dll does not reference the Api");
			Check(!ScriptDomain.References(tests, "RDR2Coop.Api"), "CoreTests.dll does not reference the Api");
			string txt = Path.Combine(_tmp, "RDR2Coop", "notadll.dll");
			File.WriteAllText(txt, "hello");
			Check(!ScriptDomain.References(txt, "RDR2Coop.Api"), "a text file is not an assembly");
		}

		static void PushLongString()
		{
			var parts = new List<string>();
			NativeFunc.PushLongString(null, parts.Add);
			NativeFunc.PushLongString("short", parts.Add);
			Check(parts.Count == 2 && parts[0] == null && parts[1] == "short", "short strings pass through unchanged");

			parts.Clear();
			string ascii = new string('a', 250);
			NativeFunc.PushLongString(ascii, parts.Add);
			Check(parts.Count == 3 && parts[0].Length == 99 && parts[1].Length == 99 && parts[2].Length == 52, "ASCII split 99/99/52");

			parts.Clear();
			string mixed = string.Concat(System.Linq.Enumerable.Repeat("aé€😀", 40)); // 1+2+3+4 bytes per repeat
			NativeFunc.PushLongString(mixed, parts.Add);
			Check(string.Concat(parts) == mixed, "chunks concatenate to the original");
			// SHDN counts each UTF-16 unit as <= 3 bytes (its surrogate branch is unreachable), so chunks can split an emoji
			// pair; kept as upstream has it. The byte bound still holds because the count overestimates.
			foreach (var p in parts)
			{
				Check(Encoding.UTF8.GetByteCount(p) <= 99, "chunk over 99 bytes");
			}
		}

		static void SwallowedAbort()
		{
			var d = NewDomain();
			Thread t = null;
			Script s = null;
			s = StartScript(d, typeof(ScriptA), (o, e) => { t = Thread.CurrentThread; try { s.Wait(0); } catch (Exception) { } });
			d.DoTick(); // parked in Wait
			s.Abort();
			Check(SpinUntil(() => !t.IsAlive, 500), "script thread still alive after its tick swallowed the abort");
			ScriptDomain.Unload(d);
		}

		static void StaleThread()
		{
			// The mod's tick is try { ... } catch (Exception): a reload's ScriptAbortedException lands in that catch
			// and the old thread goes on to call a native while the main thread is inside the NEW domain's DoTick.
			var d = NewDomain();
			bool go = false, staleDone = false, staleThrew = false;
			Script s = null;
			s = StartScript(d, typeof(ScriptA), (o, e) =>
			{
				try { s.Wait(0); }
				catch (Exception)
				{
					SpinUntil(() => go, 1000);
					try { ScriptDomain.CurrentDomain.ExecuteTask(new CountTask()); }
					catch (ScriptAbortedException) { staleThrew = true; }
					finally { staleDone = true; }
				}
			});
			d.DoTick(); // parked in Wait
			ScriptDomain.Unload(d);
			var d2 = NewDomain();
			int ticks = 0;
			StartScript(d2, typeof(ScriptB), (o, e) => { ticks++; go = true; SpinUntil(() => staleDone, 500); });
			d2.DoTick();
			Check(ticks == 1, "new script did not tick");
			Check(staleDone && staleThrew, "stale thread did not unwind at its native call (done " + staleDone + ", threw " + staleThrew + ")");
			d2.DoTick();
			Check(ticks == 2, "new script's handshake is broken after the stale call");
			ScriptDomain.Unload(d2);
		}

		static void Unload()
		{
			var d = NewDomain();
			var s = StartScript(d, typeof(ScriptA), (o, e) => { });
			d.DoTick();
			ScriptDomain.Unload(d);
			Check(!s.IsRunning, "script still running after Unload");
			Check(d.RunningScripts.Length == 0, "RunningScripts not cleared");
			var d2 = NewDomain();
			Check(ScriptDomain.CurrentDomain == d2, "CurrentDomain not the new domain");
			d2.Start(); // the RDR2Coop folder holds only notadll.dll from the References test: logged, nothing loads
			Check(d2.RunningScripts.Length == 0, "Start loaded something from an empty folder");
			ScriptDomain.Unload(d2);
		}
	}
}
