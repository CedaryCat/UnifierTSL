using Microsoft.CodeAnalysis;
using System.Runtime.Loader;

namespace Atelier.Session.Context
{
    internal sealed class ScriptHostContext(
        ScriptGlobals globals,
        Type globalsType,
        AssemblyLoadContext loadContext,
        MetadataReference globalsMetadataReference,
        string hostOutExpression,
        Action release) : IDisposable
    {
        private bool disposed;
        private readonly Action releaseHost = release ?? throw new ArgumentNullException(nameof(release));
        private readonly AssemblyLoadContext globalsLoadContext = loadContext ?? throw new ArgumentNullException(nameof(loadContext));

        public ScriptGlobals Globals { get; } = globals ?? throw new ArgumentNullException(nameof(globals));

        public Type GlobalsType { get; } = globalsType ?? throw new ArgumentNullException(nameof(globalsType));

        public MetadataReference GlobalsMetadataReference { get; } = globalsMetadataReference
            ?? throw new ArgumentNullException(nameof(globalsMetadataReference));

        public string HostOutExpression { get; } = string.IsNullOrWhiteSpace(hostOutExpression)
            ? throw new ArgumentException(GetString("HostOut expression is required."), nameof(hostOutExpression))
            : hostOutExpression;

        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            try {
                releaseHost();
            }
            finally {
                globalsLoadContext.Unload();
            }
        }
    }
}
