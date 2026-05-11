using System.Collections.Immutable;
using System.Reflection;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Commanding.Execution;
using UnifierTSL.Logging;

namespace UnifierTSL.Commanding.Endpoints
{
    public enum CommandAccessResult : byte
    {
        Allowed,
        Unavailable,
        Denied,
    }

    public sealed record CommandEndpointExecutionAdapter
    {
        public required Func<CommandEndpointActionBinding, CommandAccessResult> EvaluateAccess { get; init; }

        public Func<CommandEndpointActionBinding, CommandAccessResult, CommandOutcome?>? ResolveAccessFailure { get; init; }
    }

    public sealed record CommandPreviewCandidate
    {
        public required CommandEndpointActionBinding Binding { get; init; }

        public required CommandInvocationContext Context { get; init; }

        public required int ConsumedUserTokens { get; init; }

        public required int MatchedPathLength { get; init; }
    }

    public sealed record CommandPreviewResult
    {
        public required CommandEndpointRootBinding Root { get; init; }

        public required CommandExecutionRequest Request { get; init; }

        public ImmutableArray<CommandPreviewCandidate> Candidates { get; init; } = [];

        public ImmutableArray<CommandMatchFailure> Failures { get; init; } = [];
    }

    public static class CommandEndpointDispatcher
    {
        private readonly record struct ActionInputProjection(
            ImmutableArray<string> RawUserArguments,
            ImmutableArray<CommandInputToken> RawUserArgumentTokens,
            ImmutableArray<string> UserArguments,
            ImmutableArray<CommandInputToken> UserArgumentTokens,
            ImmutableArray<string> RecognizedFlags,
            object? FlagsValue);

        private sealed record MatchCandidate(
            CommandEndpointActionBinding Binding,
            CommandInvocationContext Context,
            object?[] BoundValues,
            int ConsumedUserTokens,
            int MatchedPathLength);

        private sealed record MatchEvaluation(MatchCandidate? Candidate, CommandMatchFailure? BindingFailure);

        private readonly record struct GuardEvaluation(bool SkipAction, CommandMatchFailure? Failure);

        private readonly record struct MismatchHandlerAdapter(CommandMismatchHandlingMode HandlingMode, CommandActionRuntime Runtime);

        public static Task<CommandOutcome> DispatchAsync(
            CommandEndpointRootBinding root,
            CommandExecutionRequest request,
            CommandEndpointExecutionAdapter adapter,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(adapter);

            MismatchHandlerAdapter? mismatchHandler = root.Root.MismatchHandler is null
                ? null
                : new MismatchHandlerAdapter(
                    root.Root.MismatchHandler.HandlingMode,
                    root.Root.MismatchHandler.Runtime);

            return DispatchCoreAsync(
                root.Root,
                root.Actions,
                mismatchHandler,
                BuildUsage(root, request.InvokedRoot),
                request,
                adapter,
                cancellationToken);
        }

        public static CommandPreviewResult PreviewMatches(
            CommandEndpointRootBinding root,
            CommandExecutionRequest request,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(request);

            (var candidates, var failures) = FindCandidates(
                root.Actions,
                root.Root,
                request,
                cancellationToken);
            return new CommandPreviewResult {
                Root = root,
                Request = request,
                Candidates = [.. candidates.Select(static candidate => new CommandPreviewCandidate {
                    Binding = candidate.Binding,
                    Context = candidate.Context,
                    ConsumedUserTokens = candidate.ConsumedUserTokens,
                    MatchedPathLength = candidate.MatchedPathLength,
                })],
                Failures = failures,
            };
        }

        private static async Task<CommandOutcome> DispatchCoreAsync(
            CommandRootDefinition root,
            ImmutableArray<CommandEndpointActionBinding> bindings,
            MismatchHandlerAdapter? mismatchHandler,
            string usage,
            CommandExecutionRequest request,
            CommandEndpointExecutionAdapter adapter,
            CancellationToken cancellationToken) {
            var rootInvocation = BuildInvocationContext(
                root,
                action: null,
                request);
            (var candidates, var failures) = FindCandidates(
                bindings,
                root,
                request,
                cancellationToken);

            if (candidates.Length == 0) {
                var bestFailure = SelectBestFailure(failures);
                if (bestFailure is not null) {
                    var failureBinding = ResolveBinding(bindings, bestFailure.Action.Method);
                    var failureAccess = adapter.EvaluateAccess(failureBinding);
                    if (failureAccess != CommandAccessResult.Allowed) {
                        return ResolveAccessFailure(failureBinding, failureAccess, adapter.ResolveAccessFailure);
                    }
                }

                if (mismatchHandler is not null
                    && ShouldInvokeMismatchHandler(mismatchHandler.Value, bestFailure)) {
                    var mismatchOutcome = await TryHandleMismatchAsync(
                        mismatchHandler.Value,
                        rootInvocation,
                        failures,
                        bestFailure,
                        cancellationToken);
                    if (mismatchOutcome is not null) {
                        return mismatchOutcome;
                    }
                }

                return bestFailure?.Outcome ?? CommandOutcome.Usage(usage);
            }

            var best = candidates[0];
            var bestAccess = adapter.EvaluateAccess(best.Binding);
            if (bestAccess != CommandAccessResult.Allowed) {
                var allowed = candidates.FirstOrDefault(candidate => adapter.EvaluateAccess(candidate.Binding) == CommandAccessResult.Allowed);
                if (allowed is not null) {
                    best = allowed;
                    bestAccess = CommandAccessResult.Allowed;
                }
            }

            if (bestAccess == CommandAccessResult.Denied) {
                return ResolveAccessFailure(best.Binding, bestAccess, adapter.ResolveAccessFailure);
            }

            if (bestAccess == CommandAccessResult.Unavailable) {
                return ResolveAccessFailure(best.Binding, bestAccess, adapter.ResolveAccessFailure);
            }

            try {
                return await best.Binding.Action.Runtime.InvokeAsync(best.BoundValues);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return CommandOutcome.Warning(GetString("Command canceled."));
            }
            catch (Exception ex) {
                return CommandOutcome.ErrorBuilder(GetString("Command failed, check logs for more details."))
                    .AddLog(LogLevel.Error, ex.ToString())
                    .Build();
            }
        }

        private static (MatchCandidate[] Candidates, ImmutableArray<CommandMatchFailure> Failures) FindCandidates(
            ImmutableArray<CommandEndpointActionBinding> bindings,
            CommandRootDefinition root,
            CommandExecutionRequest request,
            CancellationToken cancellationToken) {
            List<MatchCandidate> candidates = [];
            List<CommandMatchFailure> failures = [];

            foreach (var binding in bindings) {
                var evaluation = TryMatch(
                    root,
                    binding,
                    request,
                    cancellationToken);
                if (evaluation is null) {
                    continue;
                }

                if (evaluation.Candidate is not null) {
                    candidates.Add(evaluation.Candidate);
                    continue;
                }

                if (evaluation.BindingFailure is not null) {
                    failures.Add(evaluation.BindingFailure);
                }
            }

            MatchCandidate[] ordered = [.. candidates
                .OrderByDescending(static candidate => candidate.MatchedPathLength)
                .ThenByDescending(static candidate => candidate.ConsumedUserTokens)
                .ThenByDescending(static candidate => candidate.Context.UserArguments.Length)];
            return (ordered, [.. failures]);
        }

        private static MatchEvaluation? TryMatch(
            CommandRootDefinition root,
            CommandEndpointActionBinding binding,
            CommandExecutionRequest request,
            CancellationToken cancellationToken) {
            var rawArguments = request.RawArguments;
            if (!TryResolveMatchedPathLength(binding.Action, rawArguments, out var matchedPathLength)) {
                return null;
            }

            var projection = ProjectActionInput(binding.Action, request, matchedPathLength);
            var context = BuildInvocationContext(
                root,
                binding.Action,
                request,
                projection,
                matchedPathLength);
            var preBind = EvaluateGuards(
                action: binding.Action,
                guards: binding.Action.PreBindGuards,
                context,
                phaseName: "pre-bind",
                evaluateGuard: guard => guard.Evaluate(context));
            if (preBind.SkipAction) {
                return null;
            }

            if (preBind.Failure is not null) {
                return new MatchEvaluation(null, preBind.Failure);
            }

            if (!binding.Action.Runtime.TryBind(
                context,
                cancellationToken,
                out var boundValues,
                out var bindingFailure,
                out var consumedUserTokens,
                allowUnconsumedUserTokens: binding.Action.IgnoreTrailingArguments)) {
                return bindingFailure is null
                    ? null
                    : new MatchEvaluation(null, new CommandMatchFailure {
                        Action = binding.Action,
                        MatchedPathLength = matchedPathLength,
                        ConsumedTokens = matchedPathLength + bindingFailure.Value.ConsumedTokens,
                        Outcome = bindingFailure.Value.Outcome,
                    });
            }

            var postBind = EvaluateGuards(
                action: binding.Action,
                guards: binding.Action.PostBindGuards,
                context,
                phaseName: "post-bind",
                evaluateGuard: guard => guard.Evaluate(context));
            if (postBind.SkipAction) {
                return null;
            }

            if (postBind.Failure is not null) {
                return new MatchEvaluation(null, postBind.Failure);
            }

            return new MatchEvaluation(
                new MatchCandidate(binding, context, boundValues, consumedUserTokens, matchedPathLength),
                null);
        }

        private static GuardEvaluation EvaluateGuards<TGuard>(
            CommandActionDefinition action,
            ImmutableArray<TGuard> guards,
            CommandInvocationContext context,
            string phaseName,
            Func<TGuard, CommandGuardResult> evaluateGuard)
            where TGuard : Attribute {
            foreach (var guard in guards) {
                CommandGuardResult result;
                try {
                    result = evaluateGuard(guard);
                }
                catch (Exception ex) {
                    return new GuardEvaluation(
                        SkipAction: false,
                        Failure: BuildGuardFailure(action, context, CommandOutcome.ErrorBuilder(GetString("Command failed, check logs for more details."))
                            .AddLog(LogLevel.Error, ex.ToString())
                            .Build()));
                }

                switch (result.Decision) {
                    case CommandGuardDecision.Continue:
                        continue;
                    case CommandGuardDecision.SkipAction:
                        return new GuardEvaluation(SkipAction: true, Failure: null);
                    case CommandGuardDecision.Fail:
                        return new GuardEvaluation(
                            SkipAction: false,
                            Failure: BuildGuardFailure(
                                action,
                                context,
                                result.Outcome ?? CommandOutcome.Usage(GetString("Invalid command syntax."))));
                    default:
                        throw new InvalidOperationException(GetParticularString(
                            "{0} is guard type name, {1} is action method name",
                            $"Command {phaseName} guard '{guard.GetType().FullName}' returned unsupported decision '{result.Decision}' for action '{action.Method.DeclaringType?.FullName}.{action.Method.Name}'."));
                }
            }

            return new GuardEvaluation(SkipAction: false, Failure: null);
        }

        private static CommandMatchFailure BuildGuardFailure(
            CommandActionDefinition action,
            CommandInvocationContext context,
            CommandOutcome outcome) {
            return new CommandMatchFailure {
                Action = action,
                MatchedPathLength = context.RawArguments.Length - context.RawUserArguments.Length,
                ConsumedTokens = context.RawArguments.Length,
                Outcome = outcome,
            };
        }

        private static CommandInvocationContext BuildInvocationContext(
            CommandRootDefinition root,
            CommandActionDefinition? action,
            CommandExecutionRequest request,
            ActionInputProjection? projection = null,
            int matchedPathLength = 0) {
            var rawArguments = request.RawArgumentTokens;
            var effectiveProjection = projection ?? new ActionInputProjection(
                RawUserArguments: action is null
                    ? request.RawArguments
                    : [.. request.RawArguments.Skip(matchedPathLength)],
                RawUserArgumentTokens: action is null
                    ? rawArguments
                    : [.. rawArguments.Skip(matchedPathLength)],
                UserArguments: action is null
                    ? request.RawArguments
                    : [.. request.RawArguments.Skip(matchedPathLength)],
                UserArgumentTokens: action is null
                    ? rawArguments
                    : [.. rawArguments.Skip(matchedPathLength)],
                RecognizedFlags: [],
                FlagsValue: null);

            return new CommandInvocationContext {
                Request = request,
                Root = root,
                Action = action,
                RawUserArguments = effectiveProjection.RawUserArguments,
                RawUserArgumentTokens = effectiveProjection.RawUserArgumentTokens,
                UserArguments = effectiveProjection.UserArguments,
                UserArgumentTokens = effectiveProjection.UserArgumentTokens,
                RecognizedFlags = effectiveProjection.RecognizedFlags,
                FlagsValue = effectiveProjection.FlagsValue,
            };
        }

        private static ActionInputProjection ProjectActionInput(
            CommandActionDefinition action,
            CommandExecutionRequest request,
            int matchedPathLength) {
            ImmutableArray<CommandInputToken> rawUserTokens = [.. request.RawArgumentTokens.Skip(matchedPathLength)];
            if (action.FlagsParameter is not CommandFlagsParameterDefinition flagsParameter) {
                return new ActionInputProjection(
                    RawUserArguments: [.. rawUserTokens.Select(static token => token.Value)],
                    RawUserArgumentTokens: rawUserTokens,
                    UserArguments: [.. rawUserTokens.Select(static token => token.Value)],
                    UserArgumentTokens: rawUserTokens,
                    RecognizedFlags: [],
                    FlagsValue: null);
            }

            var projection = CommandPatternInputProjector.Project(
                rawUserTokens,
                completedRawCount: rawUserTokens.Length,
                flagsParameter.Flags,
                static flag => flag.CanonicalToken,
                static flag => flag.Tokens);

            ulong flagBits = 0;
            foreach (var flag in projection.RecognizedFlags) {
                flagBits |= flag.Value;
            }

            return new ActionInputProjection(
                RawUserArguments: [.. rawUserTokens.Select(static token => token.Value)],
                RawUserArgumentTokens: rawUserTokens,
                UserArguments: [.. projection.CompletedPositionalTokens.Select(static token => token.Value)],
                UserArgumentTokens: projection.CompletedPositionalTokens,
                RecognizedFlags: [.. projection.RecognizedFlags.Select(static flag => flag.CanonicalToken)],
                FlagsValue: Enum.ToObject(flagsParameter.EnumType, flagBits));
        }

        private static async ValueTask<CommandOutcome?> TryHandleMismatchAsync(
            MismatchHandlerAdapter mismatchHandler,
            CommandInvocationContext invocationContext,
            ImmutableArray<CommandMatchFailure> failures,
            CommandMatchFailure? bestFailure,
            CancellationToken cancellationToken) {
            CommandMismatchContext mismatchContext = new() {
                InvocationContext = invocationContext,
                Failures = failures,
                BestFailure = bestFailure,
            };

            if (!mismatchHandler.Runtime.TryBind(
                invocationContext,
                cancellationToken,
                out var boundValues,
                out var _,
                out var _,
                mismatchContext,
                allowUnconsumedUserTokens: true)) {
                return null;
            }

            try {
                return await mismatchHandler.Runtime.InvokeAsync(boundValues);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return CommandOutcome.Warning(GetString("Command canceled."));
            }
            catch (Exception ex) {
                return CommandOutcome.ErrorBuilder(GetString("Command failed, check logs for more details."))
                    .AddLog(LogLevel.Error, ex.ToString())
                    .Build();
            }
        }

        private static CommandMatchFailure? SelectBestFailure(IEnumerable<CommandMatchFailure> failures) {
            return failures
                .OrderByDescending(static failure => failure.MatchedPathLength)
                .ThenByDescending(static failure => failure.ConsumedTokens)
                .ThenByDescending(static failure => failure.IsExplicit)
                .FirstOrDefault();
        }

        private static bool TryResolveMatchedPathLength(
            CommandActionDefinition action,
            ImmutableArray<string> rawArguments,
            out int matchedPathLength) {
            if (MatchesPath(rawArguments, action.PathSegments)) {
                matchedPathLength = action.PathSegments.Length;
                return true;
            }

            foreach (var alias in action.PathAliases) {
                if (MatchesPath(rawArguments, alias)) {
                    matchedPathLength = alias.Length;
                    return true;
                }
            }

            matchedPathLength = 0;
            return false;
        }

        private static bool MatchesPath(ImmutableArray<string> rawArguments, ImmutableArray<string> path) {
            if (rawArguments.Length < path.Length) {
                return false;
            }

            for (var i = 0; i < path.Length; i++) {
                if (!rawArguments[i].Equals(path[i], StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
            }

            return true;
        }

        private static bool ShouldInvokeMismatchHandler(
            MismatchHandlerAdapter mismatchHandler,
            CommandMatchFailure? bestFailure) {
            return mismatchHandler.HandlingMode == CommandMismatchHandlingMode.OverrideExplicitFailure
                || bestFailure is null
                || !bestFailure.IsExplicit;
        }

        private static CommandOutcome ResolveAccessFailure(
            CommandEndpointActionBinding binding,
            CommandAccessResult access,
            Func<CommandEndpointActionBinding, CommandAccessResult, CommandOutcome?>? resolver) {
            return resolver?.Invoke(binding, access)
                ?? access switch {
                    CommandAccessResult.Denied => CommandOutcome.Error(GetString("You do not have access to this command.")),
                    CommandAccessResult.Unavailable => CommandOutcome.Error(GetString("This command is not available from the current execution context.")),
                    _ => CommandOutcome.Empty,
                };
        }

        private static string BuildUsage(CommandEndpointRootBinding root, string invokedRoot) {
            var lines = root.Actions.Select(action => {
                var subCommand = action.Action.PathSegments.Length == 0 ? string.Empty : string.Join(' ', action.Action.PathSegments) + " ";
                var parameters = string.Join(' ', action.Action.Parameters.Select(FormatParameter));
                var flags = FormatFlags(action.Action.FlagsParameter);
                var usage = $"{invokedRoot} {subCommand}{parameters}".TrimEnd();
                if (!string.IsNullOrWhiteSpace(flags)) {
                    usage = string.IsNullOrWhiteSpace(parameters)
                        ? $"{usage} {flags}".TrimEnd()
                        : $"{usage} {flags}";
                }

                if (!string.IsNullOrWhiteSpace(action.Action.Summary)) {
                    usage += $" - {action.Action.Summary}";
                }

                return usage;
            });

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatParameter(CommandParamDefinition parameter) {
            var parameterName = parameter.Name;
            if (parameter.AcceptedSpecialTokens.Length > 0) {
                parameterName = string.Join('|', [parameterName, .. parameter.AcceptedSpecialTokens]);
            }

            var token = parameter.Variadic
                ? $"<{parameterName}...>"
                : $"<{parameterName}>";
            return parameter.Optional ? $"[{token}]" : token;
        }

        private static string FormatFlags(CommandFlagsParameterDefinition? flagsParameter) {
            if (flagsParameter is null || flagsParameter.Flags.Length == 0) {
                return string.Empty;
            }

            return string.Join(' ', flagsParameter.Flags.Select(static flag => $"[{flag.CanonicalToken}]"));
        }

        private static CommandEndpointActionBinding ResolveBinding(
            ImmutableArray<CommandEndpointActionBinding> bindings,
            MethodInfo method) {
            foreach (var binding in bindings) {
                if (binding.Action.Method == method) {
                    return binding;
                }
            }

            throw new InvalidOperationException(GetParticularString(
                "{0} is command action name",
                $"Command action '{method.DeclaringType?.FullName}.{method.Name}' is missing the expected dispatch binding."));
        }
    }
}
