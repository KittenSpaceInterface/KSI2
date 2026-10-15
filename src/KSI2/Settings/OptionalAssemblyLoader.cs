using System.Reflection;
using System.Runtime.Loader;

namespace KSI2;

internal static class OptionalAssemblyLoader
{
    internal static Assembly Load(string path)
    {
        var assemblyName = AssemblyName.GetAssemblyName(path).Name;
        var loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
            assembly.GetName().Name == assemblyName);
        return loaded ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
    }
}
