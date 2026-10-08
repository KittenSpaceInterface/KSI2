using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

var decompiler = new CSharpDecompiler(Path.GetFullPath(args[0]), new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });
var types = decompiler.TypeSystem.MainModule.TypeDefinitions.Where(t => t.DeclaringTypeDefinition == null && t.FullName.Contains(args[1], StringComparison.OrdinalIgnoreCase)).ToArray();
foreach (var type in types)
{
    if (args.Length < 3) Console.WriteLine(type.FullName);
    else
    {
        Directory.CreateDirectory(args[2]);
        try { File.WriteAllText(Path.Combine(args[2], type.FullName.Replace('.', '_') + ".cs"), decompiler.DecompileTypeAsString(type.FullTypeName)); }
        catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
        Console.WriteLine(type.FullName);
    }
}
