using Microsoft.Xna.Framework;
using Terraria;
using UnifierTSL.Commanding;

namespace TShockAPI.Commanding.V2
{
    [CommandController("me", Summary = nameof(ControllerSummary))]
    internal static class MeCommand
    {
        private static string ControllerSummary => GetString("Sends an action message to everyone.");
        private static string ExecuteSummary => GetString("Sends a third-person action message to the current server.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [RequireUnmuted]
        [TShockCommand(nameof(Permissions.cantalkinthird), ServerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText] string message) {
            context.Server!.GetExtension<TSServerPlayer>().BCMessage(GetString("*{0} {1}", context.Executor.Name, message), 205, 133, 63);
            return CommandOutcome.Empty;
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}me <text>", Commands.Specifier));
        }
    }

    [CommandController("party", Summary = nameof(ControllerSummary))]
    [Aliases("p")]
    internal static class PartyChatCommand
    {
        private static string ControllerSummary => GetString("Sends a message to everyone on your team.");
        private static string ExecuteSummary => GetString("Broadcasts a message to the executor's current team.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [RequireUnmuted]
        [TShockCommand(nameof(Permissions.canpartychat), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText] string message) {
            var tsPlayer = context.Player!;

            var playerTeam = tsPlayer.Team;
            if (playerTeam == 0) {
                return CommandOutcome.Error(GetString("You are not in a party!"));
            }

            var formatted = GetString("<{0}> {1}", context.Executor.Name, message);
            foreach (var player in TShock.Players) {
                if (player is null || !player.Active || player.GetCurrentServer() != context.Server || player.Team != playerTeam) {
                    continue;
                }

                player.SendMessage(formatted, Main.teamColor[playerTeam].R, Main.teamColor[playerTeam].G, Main.teamColor[playerTeam].B);
            }

            return CommandOutcome.Empty;
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}p <team chat text>", Commands.Specifier));
        }
    }

    [CommandController("reply", Summary = nameof(ControllerSummary))]
    [Aliases("r")]
    internal static class ReplyCommand
    {
        private static string ControllerSummary => GetString("Replies to a PM sent to you.");
        private static string ExecuteSummary => GetString("Replies to the last whisper received by the execution actor.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [RequireUnmuted]
        [DisallowRest]
        [TShockCommand(nameof(Permissions.whisper), ServerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText] string message = "") {
            var actor = context.Executor.Player;

            if (actor.LastWhisper is not null && actor.LastWhisper.Active) {
                if (!actor.LastWhisper.AcceptingWhispers) {
                    return CommandOutcome.Error(GetString("{0} is not accepting whispers.", actor.LastWhisper.Name));
                }

                actor.LastWhisper.SendMessage(GetString("<From {0}> {1}", context.Executor.Name, message), Color.MediumPurple);
                context.Executor.SendMessage(GetString("<To {0}> {1}", actor.LastWhisper.Name, message), Color.MediumPurple);
                return CommandOutcome.Empty;
            }

            if (actor.LastWhisper is not null) {
                return CommandOutcome.Error(GetString("{0} is offline and cannot receive your reply.", actor.LastWhisper.Name));
            }

            return CommandOutcome.ErrorBuilder(GetString("You haven't previously received any whispers."))
                .AddInfo(GetString($"You can use {Commands.Specifier.Color(Utils.GreenHighlight)}{"w".Color(Utils.GreenHighlight)} to whisper to other players."))
                .Build();
        }
    }

    [CommandController("whisper", Summary = nameof(ControllerSummary))]
    [Aliases("w", "tell", "pm", "dm")]
    internal static class WhisperCommand
    {
        private static string TargetInvalidPlayerMessage(params object?[] args) => GetString("Could not find any player named \"{0}\"", args);

        private static string ControllerSummary => GetString("Sends a PM to a player.");
        private static string ExecuteSummary => GetString("Sends a private message to another player.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [RequireUnmuted]
        [DisallowRest]
        [TShockCommand(nameof(Permissions.whisper), ServerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage))] TSPlayer target,
            [RemainingText] string message) {
            var actor = context.Executor.Player;
            if (ReferenceEquals(target, actor)) {
                return CommandOutcome.Error(GetString("You cannot whisper to yourself."));
            }

            if (!target.AcceptingWhispers) {
                return CommandOutcome.Error(GetString("{0} is not accepting whispers.", target.Name));
            }

            target.SendMessage(GetString("<From {0}> {1}", context.Executor.Name, message), Color.MediumPurple);
            context.Executor.SendMessage(GetString("<To {0}> {1}", target.Name, message), Color.MediumPurple);
            target.LastWhisper = actor;
            actor.LastWhisper = target;
            return CommandOutcome.Empty;
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch([FromAmbientContext] TSExecutionContext context) {
            return BuildSyntax(context.Executor.Name);
        }

        private static CommandOutcome BuildSyntax(string executorName) {
            return CommandOutcome.InfoLines([
                GetString("Whisper Syntax"),
                GetString($"{"whisper".Color(Utils.BoldHighlight)} <{"player".Color(Utils.RedHighlight)}> <{"message".Color(Utils.PinkHighlight)}>"),
                GetString($"Example usage: {"w".Color(Utils.BoldHighlight)} {executorName.Color(Utils.RedHighlight)} {"We're no strangers to love, you know the rules, and so do I.".Color(Utils.PinkHighlight)}"),
            ]);
        }
    }

    [CommandController("wallow", Summary = nameof(ControllerSummary))]
    [Aliases("wa")]
    internal static class WallowCommand
    {
        private static string ControllerSummary => GetString("Toggles whether you accept whispers.");
        private static string ExecuteSummary => GetString("Toggles whether other players may whisper you.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.whisper), PlayerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            var tsPlayer = context.Player!;

            tsPlayer.AcceptingWhispers = !tsPlayer.AcceptingWhispers;
            return CommandOutcome.InfoBuilder(tsPlayer.AcceptingWhispers
                    ? GetString("You may now receive whispers from other players.")
                    : GetString("You will no longer receive whispers from other players."))
                .AddInfo(GetString($"You can use {Commands.Specifier.Color(Utils.GreenHighlight)}{"wa".Color(Utils.GreenHighlight)} to toggle this setting."))
                .Build();
        }
    }
}
