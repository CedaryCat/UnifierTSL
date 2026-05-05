using Atelier.Session.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;
using UnifierTSL.Logging;

namespace Atelier.Session.Context
{
    internal sealed class ScriptHostTypeFactory
    {
        private const string GeneratedNamespace = "Atelier.Session.Context.Generated";

        public ScriptHostContext Create(
            HostConsole console,
            LauncherGlobals launcher,
            ServerGlobals? server,
            IStandardLogger log,
            string hostLabel,
            string targetLabel,
            CancellationToken cancellation) {
            var typeName = "ScriptGlobals_" + Guid.NewGuid().ToString("N");
            var hostTypeExpression = "global::" + GeneratedNamespace + "." + typeName;
            var image = Compile(typeName);
            var loadContext = new ScriptHostLoadContext(typeName, [typeof(ScriptGlobals).Assembly, typeof(IStandardLogger).Assembly]);
            var assembly = loadContext.LoadFromStream(new MemoryStream(image));
            var globalsType = assembly.GetType(GeneratedNamespace + "." + typeName, throwOnError: true, ignoreCase: false)
                ?? throw new InvalidOperationException(GetString($"Generated Atelier globals type '{typeName}' was not found."));
            var globals = (ScriptGlobals?)Activator.CreateInstance(
                    globalsType,
                    console,
                    launcher,
                    server,
                    log,
                    hostLabel,
                    targetLabel,
                    cancellation)
                ?? throw new InvalidOperationException(GetString($"Generated Atelier globals type '{typeName}' could not be instantiated."));
            return new ScriptHostContext(
                globals,
                globalsType,
                loadContext,
                MetadataReference.CreateFromImage(image),
                hostTypeExpression + ".HostOut",
                () => globalsType.GetMethod("ReleaseHostOut", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null));
        }

        private static byte[] Compile(string typeName) {
            var source = CreateSource(typeName);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
            var referenceSet = MetadataReferenceCollector.Collect(
                assemblyReferences: [typeof(ScriptGlobals).Assembly, typeof(IStandardLogger).Assembly]);
            var compilation = CSharpCompilation.Create(
                "Atelier.Session.Context.Generated." + typeName,
                [syntaxTree],
                referenceSet.References,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    nullableContextOptions: NullableContextOptions.Enable));
            using var stream = new MemoryStream();
            var emit = compilation.Emit(stream);
            if (emit.Success) {
                return stream.ToArray();
            }

            var errors = emit.Diagnostics
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(static diagnostic => diagnostic.ToString());
            throw new InvalidOperationException(GetString($"Atelier generated globals type failed to compile: {string.Join("; ", errors)}"));
        }

        private static string CreateSource(string typeName) {
            return $$"""
namespace {{GeneratedNamespace}}
{
    public sealed class {{typeName}} : global::Atelier.Session.Context.ScriptGlobals
    {
        private static global::Atelier.Session.Context.HostConsole? hostOut;

        public {{typeName}}(
            global::Atelier.Session.Context.HostConsole console,
            global::Atelier.Session.Context.LauncherGlobals launcher,
            global::Atelier.Session.Context.ServerGlobals? server,
            global::UnifierTSL.Logging.IStandardLogger log,
            string hostLabel,
            string targetLabel,
            global::System.Threading.CancellationToken cancellation)
            : base(console, launcher, server, log, hostLabel, targetLabel, cancellation) {
            hostOut = console;
        }

        public static global::Atelier.Session.Context.HostConsole HostOut => hostOut
            ?? throw new global::System.InvalidOperationException({{SymbolDisplay.FormatLiteral(GetString("Atelier script host console is no longer active."), quote: true)}});

        public static void ReleaseHostOut() {
            hostOut = null;
        }
    }
}
""";
        }

        private sealed class ScriptHostLoadContext(
            string name,
            Assembly[] sharedAssemblies) : AssemblyLoadContext(name, isCollectible: true) {

            protected override Assembly? Load(AssemblyName assemblyName) {
                foreach (var assembly in sharedAssemblies) {
                    if (AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName)) {
                        return assembly;
                    }
                }

                return null;
            }
        }
    }
}
