using Atelier.Session;
using Atelier.Session.Context;
using Atelier.Session.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Scripting;
using System.Collections.Immutable;

namespace Atelier.Session.Roslyn
{
    internal sealed class ScriptOptionsFactory
    {
        private static readonly ImmutableArray<string> BaselineImports = [
            "System",
            "System.Collections.Generic",
            "System.IO",
            "System.Linq",
            "System.Text",
            "System.Threading",
            "System.Threading.Tasks",
            "UnifierTSL",
            "UnifierTSL.Servers",
            "UnifierTSL.Performance",
            AtelierIds.SessionContextNamespace,
        ];

        public SessionConfiguration Create(
            OpenOptions options,
            ScriptHostContext hostContext,
            ManagedPluginAssemblyCatalog managedPluginCatalog,
            AtelierConfig? config = null) {

            var formattingConfig = config ?? new AtelierConfig();
            var imports = BuildImports(options);
            var managedPluginReferences = managedPluginCatalog.CaptureSnapshot();
            var referenceSet = MetadataReferenceCollector.Collect(
                assemblyReferences: [typeof(ScriptGlobals).Assembly],
                assemblyPathReferences: managedPluginReferences.Select(static reference => reference.AssemblyPath),
                inMemoryReferences: [hostContext.GlobalsMetadataReference]);
            var scriptOptions = ScriptOptions.Default
                .WithReferences(referenceSet.References)
                .WithImports(imports)
                .WithSourceResolver(null!)
                .WithMetadataResolver(null!);
            var parseOptions = CSharpParseOptions.Default
                .WithKind(SourceCodeKind.Script)
                .WithLanguageVersion(LanguageVersion.Preview);
            var compilationOptions = new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    usings: imports,
                    optimizationLevel: OptimizationLevel.Debug,
                    nullableContextOptions: NullableContextOptions.Enable)
                .WithScriptClassName("AtelierSubmission")
                .WithMetadataImportOptions(MetadataImportOptions.All)
                .WithSourceReferenceResolver(null)
                .WithMetadataReferenceResolver(null);
            return new SessionConfiguration(
                new FrontendConfiguration(
                    scriptOptions,
                    parseOptions,
                    compilationOptions,
                    referenceSet.References,
                    imports,
                    referenceSet.ReferencePaths,
                    formattingConfig.UseKAndRBraceStyle,
                    formattingConfig.UseSmartSubmitDetection,
                    hostContext.GlobalsType,
                    hostContext.HostOutExpression,
                    managedPluginReferences),
                new ExecutionConfiguration(hostContext.GlobalsType, hostContext.GlobalsType.Assembly));
        }

        private static ImmutableArray<string> BuildImports(OpenOptions options) {
            var imports = BaselineImports.ToBuilder();
            switch (options.TargetProfile) {
                case LauncherProfile:
                    break;

                case ServerProfile:
                    imports.Add("System.Diagnostics");
                    break;

                default:
                    throw new InvalidOperationException(GetString($"Unsupported atelier target profile '{options.TargetProfile.GetType().FullName}'."));
            }

            return imports.ToImmutable();
        }
    }
}
