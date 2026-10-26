// Writes [assembly: TypeForwardedTo(typeof(X))] lines for tools/gen-compat-forwards.sh (see api/Compat/README.md).
using System; using System.IO; using System.Linq; using System.Reflection; using System.Runtime.Loader;
class P { static void Main(string[] a) {
    string dir = Path.GetFullPath(a[1]);
    AssemblyLoadContext.Default.Resolving += (c, n) => { var p = Path.Combine(dir, n.Name + ".dll"); return File.Exists(p) ? c.LoadFromAssemblyPath(p) : null; };
    var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(a[0]));
    string ns = a.Length > 2 ? a[2] : null;
    foreach (var t in asm.GetExportedTypes().Where(t => !t.IsNested && (ns == null || t.Namespace == ns)).OrderBy(t => t.FullName)) {
        string name = t.FullName.Replace('+', '.');
        if (t.IsGenericTypeDefinition) name = name.Substring(0, name.IndexOf('`')) + "<" + new string(',', t.GetGenericArguments().Length - 1) + ">";
        Console.WriteLine($"[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(global::{name}))]");
    } } }
