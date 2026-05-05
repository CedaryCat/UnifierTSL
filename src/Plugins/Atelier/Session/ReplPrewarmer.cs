using Atelier.Session.Context;
using Atelier.Session.Execution;
using Atelier.Session.Roslyn;

namespace Atelier.Session
{
    internal sealed class ReplPrewarmer(
        ScriptOptionsFactory scriptOptionsFactory,
        HostContextFactory hostContextFactory,
        ManagedPluginAssemblyCatalog managedPluginCatalog,
        Func<AtelierConfig> configProvider)
    {
        private readonly ScriptOptionsFactory scriptOptionsFactory = scriptOptionsFactory ?? throw new ArgumentNullException(nameof(scriptOptionsFactory));
        private readonly HostContextFactory hostContextFactory = hostContextFactory ?? throw new ArgumentNullException(nameof(hostContextFactory));
        private readonly ManagedPluginAssemblyCatalog managedPluginCatalog = managedPluginCatalog ?? throw new ArgumentNullException(nameof(managedPluginCatalog));
        private readonly Func<AtelierConfig> configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));

        public async Task WarmAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            var options = new OpenOptions(LauncherInvocationHost.Instance, LauncherProfile.Instance);
            using var hostContext = hostContextFactory.Create(options, cancellationToken);
            var configuration = scriptOptionsFactory.Create(options, hostContext, managedPluginCatalog, configProvider());

            using var workspace = new ReplWorkspace(configuration.Frontend);

            // These drafts deliberately hit parse, semantic analysis, completion, and signature-help paths
            // so the first real REPL keystroke does not pay Roslyn's full cold-start cost.
            foreach (var (draftText, caretIndex) in new[] {
                (string.Empty, 0),
                ("Conso", 5),
                ("Console.", "Console.".Length),
                ("Console.WriteLine(", "Console.WriteLine(".Length),
            }) {
                cancellationToken.ThrowIfCancellationRequested();
                await workspace.AnalyzeAsync([], CreateSyntheticDocument(draftText, caretIndex), cancellationToken).ConfigureAwait(false);
            }
        }

        private static SyntheticDocument CreateSyntheticDocument(string draftText, int caretIndex) {
            var normalizedDraft = draftText ?? string.Empty;
            var boundedCaret = Math.Clamp(caretIndex, 0, normalizedDraft.Length);
            return new SyntheticDocument(
                normalizedDraft,
                normalizedDraft,
                0,
                normalizedDraft.Length,
                boundedCaret,
                []);
        }
    }
}
