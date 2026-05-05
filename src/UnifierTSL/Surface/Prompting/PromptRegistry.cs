using System.Collections.Immutable;
using UnifierTSL.Surface;
using UnifierTSL.Surface.Prompting.Runtime;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Status;
using UnifierTSL.Commanding;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Contracts.Display;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Servers;

namespace UnifierTSL.Surface.Prompting
{
    public static class PromptRegistry
    {
        private static readonly Lock SyncLock = new();
        private static readonly List<RegisteredPromptFactory> commandPromptFactories = [];
        private static long nextCommandPromptFactoryId = 1;
        private static readonly Dictionary<long, Func<ServerContext?, IReadOnlyList<string>>> commandPrefixProviders = [];
        private static long nextCommandPrefixProviderId = 1;
        private static readonly Dictionary<SemanticKey, RegisteredParameterExplainer> parameterExplainers = [];
        private static long nextParameterExplainerRegistrationId = 1;
        private static readonly Dictionary<SemanticKey, RegisteredParameterExplainer> parameterExplainerOverrides = [];
        private static long nextParameterExplainerOverrideRegistrationId = 1;
        private static readonly Dictionary<SemanticKey, RegisteredParameterCandidateProvider> parameterCandidateProviders = [];
        private static long nextParameterCandidateProviderRegistrationId = 1;
        private static readonly Dictionary<SemanticKey, RegisteredParameterCandidateProvider> parameterCandidateProviderOverrides = [];
        private static long nextParameterCandidateProviderOverrideRegistrationId = 1;

        public static void SetDefaultCommandPromptSpecFactory(Func<ServerContext?, PromptSurfaceSpec> promptFactory) {
            ArgumentNullException.ThrowIfNull(promptFactory);
            lock (SyncLock) {
                RegisteredPromptFactory registered = new(0, promptFactory);
                var baselineIndex = commandPromptFactories.FindIndex(static factory => factory.Id == 0);
                if (baselineIndex >= 0) {
                    commandPromptFactories[baselineIndex] = registered;
                }
                else {
                    commandPromptFactories.Insert(0, registered);
                }
            }
        }

        public static void ClearDefaultCommandPromptSpecFactory() {
            lock (SyncLock) {
                commandPromptFactories.RemoveAll(static factory => factory.Id == 0);
            }
        }

        public static IDisposable RegisterDefaultCommandPromptSpecFactory(Func<ServerContext?, PromptSurfaceSpec> promptFactory) {
            ArgumentNullException.ThrowIfNull(promptFactory);

            long id;
            lock (SyncLock) {
                id = nextCommandPromptFactoryId++;
                commandPromptFactories.Add(new RegisteredPromptFactory(id, promptFactory));
            }

            return new DefaultCommandPromptFactoryRegistration(id);
        }

        public static IDisposable RegisterCommandPrefixProvider(Func<IReadOnlyList<string>> provider) {
            return RegisterCommandPrefixProvider(_ => provider());
        }

        public static IDisposable RegisterCommandPrefixProvider(Func<ServerContext?, IReadOnlyList<string>> provider) {
            ArgumentNullException.ThrowIfNull(provider);

            long id;
            lock (SyncLock) {
                id = nextCommandPrefixProviderId++;
                commandPrefixProviders[id] = provider;
            }

            return new CommandPrefixProviderRegistration(id);
        }

        public static IReadOnlyList<string> GetRegisteredCommandPrefixes() {
            return GetRegisteredCommandPrefixes(server: null);
        }

        public static IReadOnlyList<string> GetRegisteredCommandPrefixes(ServerContext? server) {
            List<Func<ServerContext?, IReadOnlyList<string>>> providers;
            lock (SyncLock) {
                providers = [.. commandPrefixProviders.Values];
            }

            List<string> resolved = [];
            foreach (var provider in providers) {
                try {
                    var prefixes = provider(server);
                    if (prefixes is null) {
                        continue;
                    }

                    foreach (var prefix in prefixes) {
                        if (string.IsNullOrWhiteSpace(prefix)) {
                            continue;
                        }

                        resolved.Add(prefix.Trim());
                    }
                }
                catch (Exception ex) {
                    LogPromptWarning(GetString("A surface prompt command-prefix provider failed."), ex);
                }
            }

            return resolved;
        }

        public static IDisposable RegisterParameterExplainer(SemanticKey semanticKey, IParamValueExplainer explainer) {
            ArgumentNullException.ThrowIfNull(explainer);
            long id;
            lock (SyncLock) {
                id = nextParameterExplainerRegistrationId++;
                parameterExplainers[semanticKey] = new RegisteredParameterExplainer(id, explainer);
            }

            return new ParameterExplainerRegistration(semanticKey, id);
        }

        public static IDisposable RegisterParameterExplainerOverride(SemanticKey semanticKey, IParamValueExplainer explainer) {
            ArgumentNullException.ThrowIfNull(explainer);
            long id;
            lock (SyncLock) {
                id = nextParameterExplainerOverrideRegistrationId++;
                parameterExplainerOverrides[semanticKey] = new RegisteredParameterExplainer(id, explainer);
            }

            return new ParameterExplainerOverrideRegistration(semanticKey, id);
        }

        public static IDisposable RegisterParameterCandidateProvider(SemanticKey semanticKey, IParamValueCandidateProvider provider) {
            ArgumentNullException.ThrowIfNull(provider);
            long id;
            lock (SyncLock) {
                id = nextParameterCandidateProviderRegistrationId++;
                parameterCandidateProviders[semanticKey] = new RegisteredParameterCandidateProvider(id, provider);
            }

            return new ParameterCandidateProviderRegistration(semanticKey, id);
        }

        public static IDisposable RegisterParameterCandidateProviderOverride(SemanticKey semanticKey, IParamValueCandidateProvider provider) {
            ArgumentNullException.ThrowIfNull(provider);
            long id;
            lock (SyncLock) {
                id = nextParameterCandidateProviderOverrideRegistrationId++;
                parameterCandidateProviderOverrides[semanticKey] = new RegisteredParameterCandidateProvider(id, provider);
            }

            return new ParameterCandidateProviderOverrideRegistration(semanticKey, id);
        }

        public static PromptSurfaceSpec CreateDefaultCommandPromptSpec(ServerContext? server) {
            Func<ServerContext?, PromptSurfaceSpec>? local;
            lock (SyncLock) {
                local = commandPromptFactories.Count > 0
                    ? commandPromptFactories[^1].Factory
                    : null;
            }

            if (local is not null) {
                try {
                    var promptSpec = local(server);
                    if (promptSpec is not null) {
                        return DecorateCommandPromptSpec(promptSpec, server);
                    }
                }
                catch (Exception ex) {
                    LogPromptWarning(GetString("The default surface prompt factory failed."), ex);
                }
            }

            var activityActive = server?.Console.HasActiveSurfaceActivity ?? SurfaceRuntimeOptions.HasRootActivity;
            return DecorateCommandPromptSpec(
                PromptSurfaceSpec.CreateCommandLine(activityActive: activityActive),
                server);
        }

        internal static PromptSurfaceCompiler CreateCompiler(
            PromptSurfaceSpec contextSpec,
            PromptSurfaceScenario initialScenario,
            PromptSurfaceScenario reactiveScenario) {
            return new PromptSurfaceCompiler(contextSpec, initialScenario, reactiveScenario);
        }

        private static PromptSurfaceSpec DecorateCommandPromptSpec(PromptSurfaceSpec contextSpec, ServerContext? server) {
            if (contextSpec.Purpose != PromptInputPurpose.CommandLine) {
                return contextSpec;
            }

            var sourceResolver = contextSpec.RuntimeResolver;
            var decoratedServer = contextSpec.Server ?? server;
            // Merge order is intentionally three-tiered:
            // 1. registered defaults
            // 2. prompt-spec local providers
            // 3. registered overrides
            // Shared semantic keys such as PlayerRef rely on the final layer so surfaces like
            // TShock can replace generic prompt behavior without minting a forked semantic key.
            var decoratedExplainers =
                MergeParameterExplainers(
                    MergeParameterExplainers(GetRegisteredParameterExplainersSnapshot(), contextSpec.ParameterExplainers),
                    GetRegisteredParameterExplainerOverridesSnapshot());
            var decoratedCandidateProviders =
                MergeParameterCandidateProviders(
                    MergeParameterCandidateProviders(GetRegisteredParameterCandidateProvidersSnapshot(), contextSpec.ParameterCandidateProviders),
                    GetRegisteredParameterCandidateProviderOverridesSnapshot());

            return CreateDerivedPromptSpec(
                contextSpec,
                decoratedServer,
                decoratedExplainers,
                decoratedCandidateProviders,
                PromptSurfaceRuntimeResolver.Create(
                    resolveContext => {
                        ProjectionDocumentContent resolvedContent = contextSpec.Content;
                        if (sourceResolver is not null) {
                            try {
                                resolvedContent = sourceResolver.Resolve(resolveContext);
                            }
                            catch (Exception ex) {
                                LogPromptWarning(GetString("The surface prompt runtime resolver failed while producing projection content."), ex);
                            }
                        }

                        var inputSummary = PromptSummaryComposer.Compose(server, resolveContext);
                        return PromptProjectionDocumentFactory.WithState(
                            resolvedContent,
                            nodes: [
                                new TextProjectionNodeState {
                                    NodeId = PromptProjectionDocumentFactory.NodeIds.StatusSummary,
                                    State = new TextNodeState {
                                        Content = PromptProjectionDocumentFactory.CreateSingleLineBlock(
                                            inputSummary,
                                            SurfaceStyleCatalog.StatusSummary),
                                    },
                                },
                            ]);
                    },
                    resolveContext => {
                        long sourceRevision = 0;
                        if (sourceResolver is not null) {
                            try {
                                sourceRevision = sourceResolver.GetRevision(resolveContext);
                            }
                            catch (Exception ex) {
                                LogPromptWarning(GetString("The surface prompt runtime resolver failed while computing its revision."), ex);
                            }
                        }

                        return sourceRevision;
                    }));
        }

        private static void UnregisterCommandPrefixProvider(long id) {
            lock (SyncLock) {
                commandPrefixProviders.Remove(id);
            }
        }

        private static void UnregisterDefaultCommandPromptSpecFactory(long id) {
            lock (SyncLock) {
                commandPromptFactories.RemoveAll(factory => factory.Id == id);
            }
        }

        private static ImmutableDictionary<SemanticKey, IParamValueExplainer> GetRegisteredParameterExplainersSnapshot() {
            lock (SyncLock) {
                if (parameterExplainers.Count == 0) {
                    return ImmutableDictionary<SemanticKey, IParamValueExplainer>.Empty;
                }

                return parameterExplainers.ToImmutableDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.Explainer,
                    EqualityComparer<SemanticKey>.Default);
            }
        }

        private static ImmutableDictionary<SemanticKey, IParamValueExplainer> GetRegisteredParameterExplainerOverridesSnapshot() {
            lock (SyncLock) {
                if (parameterExplainerOverrides.Count == 0) {
                    return ImmutableDictionary<SemanticKey, IParamValueExplainer>.Empty;
                }

                return parameterExplainerOverrides.ToImmutableDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.Explainer,
                    EqualityComparer<SemanticKey>.Default);
            }
        }

        private static ImmutableDictionary<SemanticKey, IParamValueExplainer> MergeParameterExplainers(
            ImmutableDictionary<SemanticKey, IParamValueExplainer> defaults,
            ImmutableDictionary<SemanticKey, IParamValueExplainer> overrides) {
            if (defaults.Count == 0) {
                return overrides;
            }

            if (overrides.Count == 0) {
                return defaults;
            }

            ImmutableDictionary<SemanticKey, IParamValueExplainer>.Builder builder = defaults.ToBuilder();
            foreach ((var key, var explainer) in overrides) {
                builder[key] = explainer;
            }

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<SemanticKey, IParamValueCandidateProvider> GetRegisteredParameterCandidateProvidersSnapshot() {
            lock (SyncLock) {
                if (parameterCandidateProviders.Count == 0) {
                    return ImmutableDictionary<SemanticKey, IParamValueCandidateProvider>.Empty;
                }

                return parameterCandidateProviders.ToImmutableDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.Provider,
                    EqualityComparer<SemanticKey>.Default);
            }
        }

        private static ImmutableDictionary<SemanticKey, IParamValueCandidateProvider> GetRegisteredParameterCandidateProviderOverridesSnapshot() {
            lock (SyncLock) {
                if (parameterCandidateProviderOverrides.Count == 0) {
                    return ImmutableDictionary<SemanticKey, IParamValueCandidateProvider>.Empty;
                }

                return parameterCandidateProviderOverrides.ToImmutableDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.Provider,
                    EqualityComparer<SemanticKey>.Default);
            }
        }

        private static ImmutableDictionary<SemanticKey, IParamValueCandidateProvider> MergeParameterCandidateProviders(
            ImmutableDictionary<SemanticKey, IParamValueCandidateProvider> defaults,
            ImmutableDictionary<SemanticKey, IParamValueCandidateProvider> overrides) {
            if (defaults.Count == 0) {
                return overrides;
            }

            if (overrides.Count == 0) {
                return defaults;
            }

            ImmutableDictionary<SemanticKey, IParamValueCandidateProvider>.Builder builder = defaults.ToBuilder();
            foreach ((var key, var provider) in overrides) {
                builder[key] = provider;
            }

            return builder.ToImmutable();
        }

        private static void UnregisterParameterExplainer(SemanticKey semanticKey, long id) {
            lock (SyncLock) {
                if (parameterExplainers.TryGetValue(semanticKey, out var registration)
                    && registration.Id == id) {
                    parameterExplainers.Remove(semanticKey);
                }
            }
        }

        private static void UnregisterParameterExplainerOverride(SemanticKey semanticKey, long id) {
            lock (SyncLock) {
                if (parameterExplainerOverrides.TryGetValue(semanticKey, out var registration)
                    && registration.Id == id) {
                    parameterExplainerOverrides.Remove(semanticKey);
                }
            }
        }

        private static void UnregisterParameterCandidateProvider(SemanticKey semanticKey, long id) {
            lock (SyncLock) {
                if (parameterCandidateProviders.TryGetValue(semanticKey, out var registration)
                    && registration.Id == id) {
                    parameterCandidateProviders.Remove(semanticKey);
                }
            }
        }

        private static void UnregisterParameterCandidateProviderOverride(SemanticKey semanticKey, long id) {
            lock (SyncLock) {
                if (parameterCandidateProviderOverrides.TryGetValue(semanticKey, out var registration)
                    && registration.Id == id) {
                    parameterCandidateProviderOverrides.Remove(semanticKey);
                }
            }
        }

        private static void LogPromptWarning(string message, Exception ex) {
            UnifierApi.Logger.Warning(message, category: "SurfacePrompt", ex: ex);
        }

        private static PromptSurfaceSpec CreateDerivedPromptSpec(
            PromptSurfaceSpec source,
            ServerContext? server,
            ImmutableDictionary<SemanticKey, IParamValueExplainer> parameterExplainers,
            ImmutableDictionary<SemanticKey, IParamValueCandidateProvider> parameterCandidateProviders,
            IPromptRuntimeResolver? runtimeResolver) {
            return new PromptSurfaceSpec {
                Purpose = source.Purpose,
                Server = server,
                Content = source.Content,
                SemanticSpec = source.SemanticSpec,
                StaticCandidates = source.StaticCandidates,
                ParameterExplainers = parameterExplainers,
                ParameterCandidateProviders = parameterCandidateProviders,
                RuntimeResolver = runtimeResolver,
                BufferedAuthoring = source.BufferedAuthoring,
            };
        }

        private sealed class CommandPrefixProviderRegistration(long id) : IDisposable
        {
            private long id = id;

            public void Dispose() {
                var registrationId = Interlocked.Exchange(ref id, 0);
                if (registrationId == 0) {
                    return;
                }

                UnregisterCommandPrefixProvider(registrationId);
            }
        }

        private sealed class ParameterExplainerOverrideRegistration(SemanticKey semanticKey, long id) : IDisposable
        {
            private readonly SemanticKey semanticKey = semanticKey;
            private long id = id;

            public void Dispose() {
                var registrationId = Interlocked.Exchange(ref id, 0);
                if (registrationId == 0) {
                    return;
                }

                UnregisterParameterExplainerOverride(semanticKey, registrationId);
            }
        }

        private sealed class ParameterCandidateProviderOverrideRegistration(SemanticKey semanticKey, long id) : IDisposable
        {
            private readonly SemanticKey semanticKey = semanticKey;
            private long id = id;

            public void Dispose() {
                var registrationId = Interlocked.Exchange(ref id, 0);
                if (registrationId == 0) {
                    return;
                }

                UnregisterParameterCandidateProviderOverride(semanticKey, registrationId);
            }
        }

        private sealed record RegisteredPromptFactory(long Id, Func<ServerContext?, PromptSurfaceSpec> Factory);

        private sealed record RegisteredParameterExplainer(long Id, IParamValueExplainer Explainer);

        private sealed record RegisteredParameterCandidateProvider(long Id, IParamValueCandidateProvider Provider);

        private sealed class DefaultCommandPromptFactoryRegistration(long id) : IDisposable
        {
            private long id = id;

            public void Dispose() {
                var registrationId = Interlocked.Exchange(ref id, 0);
                if (registrationId == 0) {
                    return;
                }

                UnregisterDefaultCommandPromptSpecFactory(registrationId);
            }
        }

        private sealed class ParameterExplainerRegistration(SemanticKey semanticKey, long id) : IDisposable
        {
            private readonly SemanticKey semanticKey = semanticKey;
            private long id = id;

            public void Dispose() {
                var registrationId = Interlocked.Exchange(ref id, 0);
                if (registrationId == 0) {
                    return;
                }

                UnregisterParameterExplainer(semanticKey, registrationId);
            }
        }

        private sealed class ParameterCandidateProviderRegistration(SemanticKey semanticKey, long id) : IDisposable
        {
            private readonly SemanticKey semanticKey = semanticKey;
            private long id = id;

            public void Dispose() {
                var registrationId = Interlocked.Exchange(ref id, 0);
                if (registrationId == 0) {
                    return;
                }

                UnregisterParameterCandidateProvider(semanticKey, registrationId);
            }
        }
    }
}
