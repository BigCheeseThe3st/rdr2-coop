// Reads RDR2Coop.ini. ScriptHookRDR2DotNet's ScriptSettings found the file but
// returned only defaults under Proton, so this small reader replaces it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace RDR2Coop.Net
{
    public sealed class Ini
    {
        readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static Ini Load(string path) => Parse(File.Exists(path) ? File.ReadAllText(path) : "");

        public static Ini Parse(string text)
        {
            var ini = new Ini();
            string section = "";
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                ini._values[section + "." + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return ini;
        }

        public string Get(string section, string key, string fallback) =>
            _values.TryGetValue(section + "." + key, out var v) && v.Length > 0 ? v : fallback;

        public int Get(string section, string key, int fallback) =>
            int.TryParse(Get(section, key, ""), out int v) ? v : fallback;

        // Sets [Coop] key=value lines in the file, keeping its comments and other
        // settings (the same rules as the installers).
        public static void SetCoop(string path, IDictionary<string, string> values)
        {
            string text = File.Exists(path) ? File.ReadAllText(path) : "[Coop]\r\n";
            foreach (var kv in values)
            {
                var line = new Regex("^" + Regex.Escape(kv.Key) + "=[^\\r\\n]*", RegexOptions.Multiline);
                var coop = new Regex(@"^\[Coop\][ \t]*\r?$", RegexOptions.Multiline);
                if (line.IsMatch(text)) text = line.Replace(text, m => kv.Key + "=" + kv.Value, 1);
                else if (coop.IsMatch(text)) text = coop.Replace(text, m => m.Value.TrimEnd('\r') + "\r\n" + kv.Key + "=" + kv.Value, 1);
                else text = "[Coop]\r\n" + kv.Key + "=" + kv.Value + "\r\n" + text;
            }
            File.WriteAllText(path, text);
        }
    }
}
