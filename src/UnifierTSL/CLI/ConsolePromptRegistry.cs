using UnifierTSL.CLI.Prompting;
using UnifierTSL.CLI.Status;
using UnifierTSL.ConsoleClient.Shared.ConsolePrompting;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI
{
    public static class ConsolePromptRegistry
    {
        private static readonly Lock SyncLock = new();
        private static Func<ServerContext?, ConsolePromptSpec>? commandPromptFactory;

        public static void SetDefaultCommandPromptSpecFactory(Func<ServerContext?, ConsolePromptSpec> promptFactory) {
            ArgumentNullException.ThrowIfNull(promptFactory);
            lock (SyncLock) {
                commandPromptFactory = promptFactory;
            }
        }

        public static void ClearDefaultCommandPromptSpecFactory() {
            lock (SyncLock) {
                commandPromptFactory = null;
            }
        }

        public static ConsolePromptSpec CreateDefaultCommandPromptSpec(ServerContext? server) {
            Func<ServerContext?, ConsolePromptSpec>? local;
            lock (SyncLock) {
                local = commandPromptFactory;
            }

            if (local is not null) {
                try {
                    ConsolePromptSpec? promptSpec = local(server);
                    if (promptSpec is not null) {
                        return DecorateCommandPromptSpec(promptSpec, server);
                    }
                }
                catch {
                }
            }

            return DecorateCommandPromptSpec(ConsolePromptSpec.CreateCommandLine(), server);
        }

        internal static ConsolePromptCompiler CreateCompiler(
            ConsolePromptSpec contextSpec,
            ConsolePromptScenario initialScenario,
            ConsolePromptScenario reactiveScenario) {
            ArgumentNullException.ThrowIfNull(contextSpec);
            return new ConsolePromptCompiler(contextSpec, initialScenario, reactiveScenario);
        }

        private static ConsolePromptSpec DecorateCommandPromptSpec(ConsolePromptSpec contextSpec, ServerContext? server) {
            if (contextSpec.Purpose != ConsoleInputPurpose.CommandLine) {
                return contextSpec;
            }

            Func<ConsolePromptResolveContext, ConsolePromptUpdate>? sourceResolver = contextSpec.DynamicResolver;

            return contextSpec with {
                DynamicResolver = resolveContext => {
                    ConsolePromptUpdate? sourcePatch = null;
                    if (sourceResolver is not null) {
                        try {
                            sourcePatch = sourceResolver(resolveContext);
                        }
                        catch {
                        }
                    }

                    string inputSummary = ConsolePromptSummaryComposer.Compose(server, resolveContext);
                    ConsolePromptTheme runtimeTheme = UnifierApi.GetConsolePromptTheme();
                    if (sourcePatch is null) {
                        return new ConsolePromptUpdate {
                            InputSummaryOverride = inputSummary,
                            ThemeOverride = runtimeTheme,
                        };
                    }

                    return sourcePatch with {
                        InputSummaryOverride = inputSummary,
                        ThemeOverride = sourcePatch.ThemeOverride ?? runtimeTheme,
                    };
                },
            };
        }
    }
}
