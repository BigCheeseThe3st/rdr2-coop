//
// Copyright (C) 2015 crosire & contributors
// License: https://github.com/crosire/scripthookvdotnet#license
//
// RDR2 Co-op port: the log lives in <game>\scripts\RDR2Coop.Core.log (set by Host.Init); the in-game console output is gone.

using System;
using System.IO;

namespace RDR2DN
{
	public static class Log
	{
		public enum Level
		{
			Error,
			Warning,
			Info,
			Debug,
		}

		// <game>\scripts, set by Host.Init before anything logs; the fallback is SHDN's "next to the DLL".
		internal static string Directory;

		static string FilePath => Directory != null
			? Path.Combine(Directory, "RDR2Coop.Core.log")
			: Path.ChangeExtension(typeof(ScriptDomain).Assembly.Location, ".log");

		public static void Clear()
		{
			try
			{
				File.WriteAllText(FilePath, string.Empty);
			}
			catch
			{
				// Ignore exceptions
			}
		}

		public static void Message(Level level, params string[] message)
		{
			WriteToFile(level, message);
		}

		private static void WriteToFile(Level level, params string[] message)
		{
			try
			{
				using (var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
				{
					using (var sw = new StreamWriter(fs))
					{
						sw.Write(string.Concat("[", DateTime.Now.ToString("HH:mm:ss"), "] "));

						switch (level)
						{
							case Level.Info:
							sw.Write("[INFO] ");
							break;
							case Level.Error:
							sw.Write("[ERROR] ");
							break;
							case Level.Warning:
							sw.Write("[WARNING] ");
							break;
							case Level.Debug:
							sw.Write("[DEBUG] ");
							break;
						}

						foreach (string str in message)
						{
							sw.Write(str);
						}

						sw.WriteLine();
					}
				}
			}
			catch
			{
				// Nowhere left to report a failed log write
			}
		}
	}
}
