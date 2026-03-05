using UnifierTSL.CLI.Sessions;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI
{
    public static class ConsoleCommandHintRegistry
    {
        private static readonly Lock SyncLock = new();
        private static readonly IReadLineContextMaterializer Materializer = new DefaultReadLineContextMaterializer();
        private static Func<ServerContext?, ReadLineContextSpec>? commandLineContextFactory;

        public static void RegisterCommandLineContextSpecFactory(Func<ServerContext?, ReadLineContextSpec> contextFactory)
        {
            ArgumentNullException.ThrowIfNull(contextFactory);
            lock (SyncLock) {
                commandLineContextFactory = contextFactory;
            }
        }

        public static void UnregisterCommandLineContextSpecFactory(Func<ServerContext?, ReadLineContextSpec> contextFactory)
        {
            ArgumentNullException.ThrowIfNull(contextFactory);
            lock (SyncLock) {
                if (commandLineContextFactory == contextFactory) {
                    commandLineContextFactory = null;
                }
            }
        }

        public static ReadLineContextSpec CreateCommandLineContextSpec(ServerContext? server)
        {
            Func<ServerContext?, ReadLineContextSpec>? local;
            lock (SyncLock) {
                local = commandLineContextFactory;
            }

            if (local is not null) {
                try {
                    ReadLineContextSpec? contextSpec = local(server);
                    if (contextSpec is not null) {
                        return contextSpec;
                    }
                }
                catch {
                }
            }

            return ReadLineContextSpec.CreateCommandLine();
        }

        internal static IReadLineSemanticProvider CreateProvider(
            ReadLineContextSpec contextSpec,
            ReadLineMaterializationScenario initialScenario,
            ReadLineMaterializationScenario reactiveScenario)
        {
            ArgumentNullException.ThrowIfNull(contextSpec);
            return new SpecBackedReadLineSemanticProvider(contextSpec, Materializer, initialScenario, reactiveScenario);
        }
    }
}
