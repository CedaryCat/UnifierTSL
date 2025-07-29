using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

public class PreloadAssemblyLoadContext : AssemblyLoadContext, IDisposable
{
    private AssemblyDependencyResolver _resolver;
    private bool _disposed;

    public PreloadAssemblyLoadContext(string mainAssemblyPath) : base(isCollectible: true) 
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        string assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }

    public Assembly LoadMainAssembly(string assemblyPath)
    {
        return LoadFromAssemblyPath(assemblyPath);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        Unload();
    }
}
