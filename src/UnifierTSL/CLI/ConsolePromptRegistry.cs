using System.Collections.Immutable;
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
        private static readonly Dictionary<long, Func<IReadOnlyList<ConsoleCommandSpec>>> commandSpecProviders = [];
        private static long nextCommandSpecProviderId = 1;
        private static readonly Dictionary<string, RegisteredParameterExplainer> parameterExplainers = new(StringComparer.Ordinal);
        private static long nextParameterExplainerRegistrationId = 1;

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

        public static IDisposable RegisterCommandSpecProvider(Func<IReadOnlyList<ConsoleCommandSpec>> provider) {
            ArgumentNullException.ThrowIfNull(provider);

            long id;
            lock (SyncLock) {
                id = nextCommandSpecProviderId++;
                commandSpecProviders[id] = provider;
            }

            return new CommandSpecProviderRegistration(id);
        }

        public static IReadOnlyList<ConsoleCommandSpec> GetRegisteredCommandSpecs() {
            List<Func<IReadOnlyList<ConsoleCommandSpec>>> providers;
            lock (SyncLock) {
                providers = [.. commandSpecProviders.Values];
            }

            List<ConsoleCommandSpec> resolved = [];
            foreach (Func<IReadOnlyList<ConsoleCommandSpec>> provider in providers) {
                try {
                    IReadOnlyList<ConsoleCommandSpec>? specs = provider();
                    if (specs is null) {
                        continue;
                    }

                    foreach (ConsoleCommandSpec spec in specs) {
                        if (spec is null || string.IsNullOrWhiteSpace(spec.PrimaryName)) {
                            continue;
                        }

                        resolved.Add(spec);
                    }
                }
                catch {
                }
            }

            return resolved;
        }

        public static IDisposable RegisterParameterExplainer(string semanticKey, IConsoleParameterValueExplainer explainer) {
            if (string.IsNullOrWhiteSpace(semanticKey)) {
                throw new ArgumentException("Semantic key must not be empty.", nameof(semanticKey));
            }

            ArgumentNullException.ThrowIfNull(explainer);

            string normalizedKey = semanticKey.Trim();
            long id;
            lock (SyncLock) {
                id = nextParameterExplainerRegistrationId++;
                parameterExplainers[normalizedKey] = new RegisteredParameterExplainer(id, explainer);
            }

            return new ParameterExplainerRegistration(normalizedKey, id);
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

            contextSpec = contextSpec with {
                Server = contextSpec.Server ?? server,
                ParameterExplainers = MergeParameterExplainers(GetRegisteredParameterExplainersSnapshot(), contextSpec.ParameterExplainers),
            };

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

        private static void UnregisterCommandSpecProvider(long id) {
            lock (SyncLock) {
                commandSpecProviders.Remove(id);
            }
        }

        private static ImmutableDictionary<string, IConsoleParameterValueExplainer> GetRegisteredParameterExplainersSnapshot() {
            lock (SyncLock) {
                if (parameterExplainers.Count == 0) {
                    return ImmutableDictionary<string, IConsoleParameterValueExplainer>.Empty.WithComparers(StringComparer.Ordinal);
                }

                return parameterExplainers.ToImmutableDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.Explainer,
                    StringComparer.Ordinal);
            }
        }

        private static ImmutableDictionary<string, IConsoleParameterValueExplainer> MergeParameterExplainers(
            ImmutableDictionary<string, IConsoleParameterValueExplainer> defaults,
            ImmutableDictionary<string, IConsoleParameterValueExplainer> overrides) {
            if (defaults.Count == 0) {
                return overrides;
            }

            if (overrides.Count == 0) {
                return defaults;
            }

            ImmutableDictionary<string, IConsoleParameterValueExplainer>.Builder builder = defaults.ToBuilder();
            foreach ((string key, IConsoleParameterValueExplainer explainer) in overrides) {
                builder[key] = explainer;
            }

            return builder.ToImmutable();
        }

        private static void UnregisterParameterExplainer(string semanticKey, long id) {
            lock (SyncLock) {
                if (parameterExplainers.TryGetValue(semanticKey, out RegisteredParameterExplainer? registration)
                    && registration.Id == id) {
                    parameterExplainers.Remove(semanticKey);
                }
            }
        }

        private sealed class CommandSpecProviderRegistration(long id) : IDisposable
        {
            private long id = id;

            public void Dispose() {
                long registrationId = Interlocked.Exchange(ref id, 0);
                if (registrationId == 0) {
                    return;
                }

                UnregisterCommandSpecProvider(registrationId);
            }
        }

        private sealed record RegisteredParameterExplainer(long Id, IConsoleParameterValueExplainer Explainer);

        private sealed class ParameterExplainerRegistration(string semanticKey, long id) : IDisposable
        {
            private readonly string semanticKey = semanticKey;
            private long id = id;

            public void Dispose() {
                long registrationId = Interlocked.Exchange(ref id, 0);
                if (registrationId == 0) {
                    return;
                }

                UnregisterParameterExplainer(semanticKey, registrationId);
            }
        }
    }
}
