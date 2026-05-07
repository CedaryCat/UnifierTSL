using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Terraria;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Logging;
using UnifierTSL.Servers;

namespace UnifierTSL.Commanding
{
    public readonly record struct CommandInvocationTarget
    {
        private readonly string? value;

        public static CommandInvocationTarget None => default;
        public static CommandInvocationTarget LauncherConsole { get; } = new("launcher-console");
        public static CommandInvocationTarget ServerConsole { get; } = new("server-console");

        public CommandInvocationTarget(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                throw new ArgumentException(GetString("Command invocation target must not be empty."), nameof(value));
            }

            this.value = value.Trim();
        }

        public string Value => value ?? string.Empty;
        public bool IsNone => string.IsNullOrEmpty(value);

        public override string ToString() {
            return Value;
        }
    }

    public enum CommandReceiptKind : byte
    {
        Info,
        Success,
        Warning,
        Error,
        Usage,
    }

    public enum CommandOutcomeAttachmentPhase : byte
    {
        BeforePrimaryReceipts,
        AfterPrimaryReceipts,
    }

    public enum UnavailableActionVisibility : byte
    {
        HideUnavailable,
        ShowWithMarker,
        ShowAndFail,
    }

    public readonly record struct CommandAvailability
    {
        private readonly ImmutableArray<CommandInvocationTarget> allowedTargets;

        public CommandAvailability(CommandInvocationTarget allowedTarget)
            : this([allowedTarget]) {
        }

        public CommandAvailability(IEnumerable<CommandInvocationTarget> allowedTargets) {
            ArgumentNullException.ThrowIfNull(allowedTargets);

            this.allowedTargets = [.. allowedTargets
                .Where(static target => !target.IsNone)
                .Distinct()];
        }

        public static CommandAvailability None => default;

        public ImmutableArray<CommandInvocationTarget> AllowedTargets => allowedTargets.IsDefault ? [] : allowedTargets;

        public bool IsEmpty => AllowedTargets.Length == 0;

        public bool Allows(CommandInvocationTarget target) {
            if (target.IsNone) {
                return false;
            }

            return AllowedTargets.Contains(target);
        }

        public static CommandAvailability Union(IEnumerable<CommandAvailability> availabilities) {
            ArgumentNullException.ThrowIfNull(availabilities);

            return new CommandAvailability(availabilities.SelectMany(static availability => availability.AllowedTargets));
        }

        public static CommandAvailability Terminal(bool allowLauncherConsole = true, bool allowServerConsole = true) {
            List<CommandInvocationTarget> targets = [];
            if (allowLauncherConsole) {
                targets.Add(CommandInvocationTarget.LauncherConsole);
            }
            if (allowServerConsole) {
                targets.Add(CommandInvocationTarget.ServerConsole);
            }

            return new CommandAvailability(targets);
        }
    }

    public interface IAmbientValueProvider
    {
        bool TryGetAmbientValue(Type requestedType, out object? value);
    }

    public readonly record struct CommandInputToken(
        string Value,
        int StartIndex,
        int SourceLength,
        bool Quoted,
        bool LeadingCharacterEscaped)
    {
        public int EndIndex => StartIndex + SourceLength;
    }

    public abstract class CommandExecutionContext : IAmbientValueProvider
    {
        public required CommandInvocationTarget Target { get; init; }

        public ServerContext? Server { get; init; }

        public ICommandExecutionFeedback? ExecutionFeedback { get; set; }

        public bool TryGetAmbientValue(Type requestedType, out object? value) {

            if (requestedType.IsInstanceOfType(this)) {
                value = this;
                return true;
            }

            if (ExecutionFeedback is not null && requestedType.IsInstanceOfType(ExecutionFeedback)) {
                value = ExecutionFeedback;
                return true;
            }

            if (requestedType == typeof(ServerContext) && Server is not null) {
                value = Server;
                return true;
            }

            return TryResolveAmbientValue(requestedType, out value);
        }

        protected virtual bool TryResolveAmbientValue(Type requestedType, out object? value) {
            value = null;
            return false;
        }
    }

    public class TerminalExecutionContext : CommandExecutionContext { }

    public sealed record CommandExecutionRequest
    {
        public required CommandExecutionContext ExecutionContext { get; init; }

        public required string RawInput { get; init; }

        public required string InvokedRoot { get; init; }

        public required ImmutableArray<CommandInputToken> RawArgumentTokens { get; init; }

        public ImmutableArray<string> RawArguments => [.. RawArgumentTokens.Select(static token => token.Value)];

        public ImmutableArray<string> CommandTokens => [InvokedRoot, .. RawArguments];
    }

    public interface ICommandOutcomeAttachment
    {
        CommandOutcomeAttachmentPhase Phase { get; }

        JsonObject ToJsonMetadata();
    }

    public record struct CommandReceipt(CommandReceiptKind Kind, string Message)
    {
        public static CommandReceipt Info(string message) => new(CommandReceiptKind.Info, message);

        public static CommandReceipt Success(string message) => new(CommandReceiptKind.Success, message);

        public static CommandReceipt Warning(string message) => new(CommandReceiptKind.Warning, message);

        public static CommandReceipt Error(string message) => new(CommandReceiptKind.Error, message);

        public static CommandReceipt Usage(string message) => new(CommandReceiptKind.Usage, message);
    }

    public record struct CommandLogEntry(LogLevel Level, string Message);

    public sealed class CommandOutcome
    {
        public bool Succeeded { get; init; } = true;

        public ImmutableArray<CommandReceipt> Receipts { get; init; } = [];

        public ImmutableArray<CommandLogEntry> Logs { get; init; } = [];

        public ImmutableArray<ICommandOutcomeAttachment> Attachments { get; init; } = [];

        public static CommandOutcome Empty { get; } = new();

        public static CommandOutcome Success(string message) => new() {
            Receipts = [CommandReceipt.Success(message)],
        };

        public static CommandOutcome Info(string message) => new() {
            Receipts = [CommandReceipt.Info(message)],
        };

        public static CommandOutcome Warning(string message) => new() {
            Succeeded = false,
            Receipts = [CommandReceipt.Warning(message)],
        };

        public static CommandOutcome Error(string message) => new() {
            Succeeded = false,
            Receipts = [CommandReceipt.Error(message)],
        };

        public static CommandOutcome Usage(string message) => new() {
            Succeeded = false,
            Receipts = [CommandReceipt.Usage(message)],
        };

        public static Builder CreateBuilder(bool succeeded = true) {
            return new Builder {
                Succeeded = succeeded,
            };
        }

        public static Builder SuccessBuilder(string message) {
            return CreateBuilder().AddSuccess(message);
        }

        public static Builder InfoBuilder(string message) {
            return CreateBuilder().AddInfo(message);
        }

        public static Builder WarningBuilder(string message) {
            return CreateBuilder(succeeded: false).AddWarning(message);
        }

        public static Builder ErrorBuilder(string message) {
            return CreateBuilder(succeeded: false).AddError(message);
        }

        public static Builder UsageBuilder(string message) {
            return CreateBuilder(succeeded: false).AddUsage(message);
        }

        public sealed class Builder
        {
            private readonly List<CommandReceipt> receipts = [];
            private readonly List<CommandLogEntry> logs = [];
            private readonly List<ICommandOutcomeAttachment> attachments = [];

            public bool Succeeded { get; set; } = true;

            public Builder AddReceipt(CommandReceipt receipt) {
                receipts.Add(receipt);
                return this;
            }

            public Builder AddInfo(string message) {
                return AddReceipt(CommandReceipt.Info(message));
            }

            public Builder AddSuccess(string message) {
                return AddReceipt(CommandReceipt.Success(message));
            }

            public Builder AddWarning(string message) {
                Succeeded = false;
                return AddReceipt(CommandReceipt.Warning(message));
            }

            public Builder AddError(string message) {
                Succeeded = false;
                return AddReceipt(CommandReceipt.Error(message));
            }

            public Builder AddUsage(string message) {
                Succeeded = false;
                return AddReceipt(CommandReceipt.Usage(message));
            }

            public Builder AddLog(LogLevel level, string message) {
                logs.Add(new CommandLogEntry(level, message));
                return this;
            }

            public Builder AddAttachment(ICommandOutcomeAttachment attachment) {
                ArgumentNullException.ThrowIfNull(attachment);
                attachments.Add(attachment);
                return this;
            }

            public Builder Apply(CommandOutcome outcome) {

                Succeeded &= outcome.Succeeded;
                receipts.AddRange(outcome.Receipts);
                logs.AddRange(outcome.Logs);
                attachments.AddRange(outcome.Attachments);
                return this;
            }

            public CommandOutcome Build() {
                return new CommandOutcome {
                    Succeeded = Succeeded,
                    Receipts = [.. receipts],
                    Logs = [.. logs],
                    Attachments = [.. attachments],
                };
            }
        }
    }

    public sealed record CommandInvocationContext
    {
        public required CommandExecutionRequest Request { get; init; }

        public required CommandRootDefinition Root { get; init; }

        public required CommandActionDefinition? Action { get; init; }

        public CommandExecutionContext ExecutionContext => Request.ExecutionContext;

        public ServerContext? Server => ExecutionContext.Server;

        public CommandInvocationTarget Target => ExecutionContext.Target;

        public string RawInput => Request.RawInput;

        public string InvokedRoot => Request.InvokedRoot;

        public ImmutableArray<string> RawArguments => Request.RawArguments;

        public ImmutableArray<CommandInputToken> RawArgumentTokens => Request.RawArgumentTokens;

        public required ImmutableArray<string> RawUserArguments { get; init; }

        public required ImmutableArray<CommandInputToken> RawUserArgumentTokens { get; init; }

        public required ImmutableArray<string> UserArguments { get; init; }

        public required ImmutableArray<CommandInputToken> UserArgumentTokens { get; init; }

        public required ImmutableArray<string> RecognizedFlags { get; init; }

        internal object? FlagsValue { get; init; }
    }

    /// <summary>
    /// Core player-ref interpretation for commands that want an explicit single-or-all selector.
    /// Hosts may expose the same player-ref semantic through native parameter types while keeping
    /// this selector route available for host-neutral command surfaces.
    /// The single-target branch keeps an invocation-scoped <see cref="Player"/> handle rather than
    /// a host-native wrapper.
    /// </summary>
    public sealed record CommandPlayerSelector
    {
        public bool IsAll { get; init; }

        public Player? Single { get; init; }

        public ServerContext? Scope { get; init; }

        public Player? Excluded { get; init; }
    }

    public sealed record CommandMatchFailure
    {
        public required CommandActionDefinition Action { get; init; }

        public int MatchedPathLength { get; init; }

        public required int ConsumedTokens { get; init; }

        public CommandOutcome? Outcome { get; init; }

        public bool IsExplicit => Outcome is not null;
    }

    public sealed record CommandMismatchContext
    {
        public required CommandInvocationContext InvocationContext { get; init; }

        public ImmutableArray<CommandMatchFailure> Failures { get; init; } = [];

        public CommandMatchFailure? BestFailure { get; init; }
    }

    public enum CommandGuardDecision : byte
    {
        Continue,
        SkipAction,
        Fail,
    }

    public readonly record struct CommandGuardResult(
        CommandGuardDecision Decision,
        CommandOutcome? Outcome)
    {
        public static CommandGuardResult Continue() => new(CommandGuardDecision.Continue, Outcome: null);

        public static CommandGuardResult SkipAction() => new(CommandGuardDecision.SkipAction, Outcome: null);

        public static CommandGuardResult Fail(CommandOutcome outcome) {
            ArgumentNullException.ThrowIfNull(outcome);
            return new CommandGuardResult(CommandGuardDecision.Fail, outcome);
        }
    }
}
