using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI.DB;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Bindings;

namespace TShockAPI.Commanding.V2
{

    [CommandController("tp", Summary = nameof(ControllerSummary))]
    internal static class TeleportCommand
    {
        private static string TargetInvalidPlayerMessage => GetString("Invalid destination player.");
        private static string ExecutePairDenialMessage => GetString("You do not have permission to teleport other players.");
        private static string SourceInvalidPlayerMessage => GetString("Invalid destination player.");

        private static string ControllerSummary => GetString("Teleports players.");
        private static string ExecuteSingleSummary => GetString("Teleports you to another player.");
        private static string ExecutePairSummary => GetString("Teleports a player to another player.");

        [CommandAction(Summary = nameof(ExecuteSingleSummary))]
        [TShockCommand(nameof(Permissions.tp), PlayerScope = true)]
        public static CommandOutcome ExecuteSingle(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage), Modifiers = CommandParamModifiers.ServerScope)] TSPlayer target) {
            var tsPlayer = context.Player!;
            if (!target.TPAllow && !context.Executor.HasPermission(Permissions.tpoverride)) {
                return CommandOutcome.Error(GetString("{0} has disabled incoming teleports.", target.Name));
            }

            if (!tsPlayer.Teleport(target.TPlayer.Bottom, true)) {
                return CommandOutcome.Empty;
            }

            var builder = CommandOutcome.SuccessBuilder(GetString("Teleported to {0}.", target.Name));
            if (!context.Executor.HasPermission(Permissions.tpsilent)) {
                builder.AddPlayerInfo(
                    target,
                    GetString("{0} teleported to you.", context.Executor.Name));
            }

            return builder.Build();
        }

        [CommandAction(Summary = nameof(ExecutePairSummary))]
        [RequirePermissionPreBind(nameof(Permissions.tpothers), nameof(ExecutePairDenialMessage))]
        [TShockCommand(nameof(Permissions.tp), PlayerScope = true)]
        public static CommandOutcome ExecutePair(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(SourceInvalidPlayerMessage), Modifiers = CommandParamModifiers.All | CommandParamModifiers.ServerScope)] TSPlayer source,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage), Modifiers = CommandParamModifiers.ServerScope, Name = "player2")] TSPlayer target) {
            var executorPlayer = context.Player!;
            var silent = context.Executor.HasPermission(Permissions.tpsilent);
            var canOverride = context.Executor.HasPermission(Permissions.tpoverride);

            if (source is TSPlayerAll allPlayers) {
                if (!context.Executor.HasPermission(Permissions.tpallothers)) {
                    return CommandOutcome.Error(GetString("You do not have permission to teleport all players."));
                }

                var outcomeBuilder = CommandOutcome.SuccessBuilder(GetString("Teleported everyone to {0}.", target.Name));
                foreach (var sourcePlayer in allPlayers.ResolveTargets().Where(player => player.Index != executorPlayer.Index)) {
                    if (!target.TPAllow && !canOverride) {
                        continue;
                    }

                    if (!sourcePlayer.Teleport(target.TPlayer.Bottom, true)) {
                        continue;
                    }

                    if (executorPlayer != sourcePlayer) {
                        outcomeBuilder.AddPlayerSuccess(
                            sourcePlayer,
                            silent
                                ? GetString("You were teleported to {0}.", target.Name)
                                : GetString("{0} teleported you to {1}.", context.Executor.Name, target.Name),
                            phase: CommandOutcomeAttachmentPhase.BeforePrimaryReceipts);
                    }

                    if (executorPlayer != target) {
                        outcomeBuilder.AddPlayerInfo(
                            target,
                            silent
                                ? GetString("{0} was teleported to you.", sourcePlayer.Name)
                                : GetString("{0} teleported {1} to you.", context.Executor.Name, sourcePlayer.Name),
                            phase: CommandOutcomeAttachmentPhase.BeforePrimaryReceipts);
                    }
                }

                return outcomeBuilder.Build();
            }

            var singleSourcePlayer = source;

            if (!singleSourcePlayer.TPAllow && !canOverride) {
                return CommandOutcome.Error(GetString("{0} has disabled incoming teleports.", singleSourcePlayer.Name));
            }

            if (!target.TPAllow && !canOverride) {
                return CommandOutcome.Error(GetString("{0} has disabled incoming teleports.", target.Name));
            }

            if (!singleSourcePlayer.Teleport(target.TPlayer.Bottom, true)) {
                return CommandOutcome.Empty;
            }

            var builder = CommandOutcome.SuccessBuilder(GetString("Teleported {0} to {1}.", singleSourcePlayer.Name, target.Name));
            if (executorPlayer != singleSourcePlayer) {
                builder.AddPlayerSuccess(
                    singleSourcePlayer,
                    silent
                        ? GetString("You were teleported to {0}.", target.Name)
                        : GetString("{0} teleported you to {1}.", context.Executor.Name, target.Name));
            }

            if (executorPlayer != target) {
                builder.AddPlayerInfo(
                    target,
                    silent
                        ? GetString("{0} was teleported to you.", singleSourcePlayer.Name)
                        : GetString("{0} teleported {1} to you.", context.Executor.Name, singleSourcePlayer.Name));
            }

            return builder.Build();
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch([FromAmbientContext] TSExecutionContext context) {
            return context.Executor.HasPermission(Permissions.tpothers)
                ? CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}tp <player> [player 2].", Commands.Specifier))
                : CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}tp <player>.", Commands.Specifier));
        }
    }

    [CommandController("tphere", Summary = nameof(ControllerSummary))]
    internal static class TeleportHereCommand
    {
        private static string PlayerTargetInvalidPlayerMessage => GetString("Invalid destination player.");

        private static string ControllerSummary => GetString("Teleports players to your position.");
        private static string ExecuteSummary => GetString("Teleports a player to your position.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.tpothers), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(PlayerTargetInvalidPlayerMessage), Modifiers = CommandParamModifiers.All | CommandParamModifiers.ServerScope, ConsumptionMode = TSPlayerConsumptionMode.GreedyPhrase)] TSPlayer playerTarget) {
            var tsPlayer = context.Player!;
            if (playerTarget is TSPlayerAll allPlayers) {
                if (!context.Executor.HasPermission(Permissions.tpallothers)) {
                    return CommandOutcome.Error(GetString("You do not have permission to teleport all other players."));
                }

                var outcomeBuilder = CommandOutcome.SuccessBuilder(GetString("Teleported everyone to yourself."));
                foreach (var player in allPlayers.ResolveTargets()) {
                    if (player.Index == tsPlayer.Index) {
                        continue;
                    }

                    if (player.Teleport(tsPlayer.TPlayer.Bottom, true)) {
                        outcomeBuilder.AddPlayerSuccess(
                            player,
                            GetString("You were teleported to {0}.", context.Executor.Name),
                            phase: CommandOutcomeAttachmentPhase.BeforePrimaryReceipts);
                    }
                }

                return outcomeBuilder.Build();
            }

            if (!playerTarget.Teleport(tsPlayer.TPlayer.Bottom, true)) {
                return CommandOutcome.Empty;
            }

            var builder = CommandOutcome.SuccessBuilder(GetString("Teleported {0} to yourself.", playerTarget.Name));
            builder.AddPlayerInfo(
                playerTarget,
                GetString("You were teleported to {0}.", context.Executor.Name),
                phase: CommandOutcomeAttachmentPhase.BeforePrimaryReceipts);
            return builder.Build();
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch([FromAmbientContext] TSExecutionContext context) {
            return context.Executor.HasPermission(Permissions.tpallothers)
                ? CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}tphere <player|*>.", Commands.Specifier))
                : CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}tphere <player>.", Commands.Specifier));
        }
    }

    [CommandController("tpnpc", Summary = nameof(ControllerSummary))]
    internal static class TeleportNpcCommand
    {
        private static string ControllerSummary => GetString("Teleports you to an active NPC.");
        private static string ExecuteSummary => GetString("Teleports you to an active NPC.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.tpnpc), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText]
            [NpcRef]
            NPC target) {
            var tsPlayer = context.Player!;
            tsPlayer.Teleport(target.Bottom, true);
            return CommandOutcome.Success(GetString("Teleported to the '{0}'.", target.FullName));
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}tpnpc <NPC>.", Commands.Specifier));
        }
    }

    [CommandController("tppos", Summary = nameof(ControllerSummary))]
    internal static class TeleportPositionCommand
    {
        private static string ControllerSummary => GetString("Teleports you to a tile position.");
        private static string ExecuteSummary => GetString("Teleports you to a tile position.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.tppos), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [TileCoordinate(TSTileCoordinateAxis.X, Name = "x")] int x,
            [TileCoordinate(TSTileCoordinateAxis.Y, Name = "y")] int y) {
            var tsPlayer = context.Player!;
            tsPlayer.Teleport(16 * x, 16 * y);
            return CommandOutcome.Success(GetString("Teleported to {0}, {1}.", x, y));
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}tppos <tile x> <tile y>.", Commands.Specifier));
        }
    }

    [CommandController("tpallow", Summary = nameof(ControllerSummary))]
    internal static class TeleportAllowCommand
    {
        private static string ControllerSummary => GetString("Toggles whether other players may teleport to you.");
        private static string ExecuteSummary => GetString("Toggles whether other players may teleport to you.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.tpallow), PlayerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            var tsPlayer = context.Player!;
            var outcome = !tsPlayer.TPAllow
                ? CommandOutcome.Success(GetString("Incoming teleports are now allowed."))
                : CommandOutcome.Success(GetString("Incoming teleports are now disabled."));
            tsPlayer.TPAllow = !tsPlayer.TPAllow;
            return outcome;
        }

    }

    [CommandController("home", Summary = nameof(ControllerSummary))]
    internal static class HomeCommand
    {
        private static string ControllerSummary => GetString("Sends you to your spawn point.");
        private static string ExecuteSummary => GetString("Sends you to your spawn point.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.home), PlayerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            var tsPlayer = context.Player!;
            if (tsPlayer.Dead) {
                return CommandOutcome.Error(GetString("You are dead. Dead players can't go home."));
            }

            tsPlayer.Spawn(PlayerSpawnContext.RecallFromItem);
            return CommandOutcome.Success(GetString("Teleported to your spawn point (home)."));
        }

    }

    [CommandController("spawn", Summary = nameof(ControllerSummary))]
    internal static class SpawnCommand
    {
        private static string ControllerSummary => GetString("Sends you to the world's spawn point.");
        private static string ExecuteSummary => GetString("Sends you to the world's spawn point.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.spawn), PlayerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            return context.Player!.TeleportToWorldSpawn()
                ? CommandOutcome.Success(GetString("Teleported to the map's spawn point."))
                : CommandOutcome.Empty;
        }
    }

    [CommandController("pos", Summary = nameof(ControllerSummary))]
    internal static class PositionCommand
    {
        private static string TargetInvalidPlayerMessage => GetString("Invalid target player.");

        private static string ControllerSummary => GetString("Returns the user's or specified user's current position.");
        private static string ExecuteSelfSummary => GetString("Returns the user's or specified user's current position.");
        private static string ExecuteOtherSummary => GetString("Returns the user's or specified user's current position.");

        [CommandAction(Summary = nameof(ExecuteSelfSummary))]
        [RequireUserArgumentCount(0)]
        [TShockCommand(nameof(Permissions.getpos), PlayerScope = true)]
        public static CommandOutcome ExecuteSelf([FromAmbientContext] TSExecutionContext context) {
            var target = context.Player!;
            return CommandOutcome.Success(GetString("Location of {0} is ({1}, {2}).", target.Name, target.TileX, target.TileY));
        }

        [CommandAction(Summary = nameof(ExecuteOtherSummary))]
        [TShockCommand(nameof(Permissions.getpos), PlayerScope = true)]
        public static CommandOutcome ExecuteOther(
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage), ConsumptionMode = TSPlayerConsumptionMode.GreedyPhrase)]
            TSPlayer target) {
            return CommandOutcome.Success(GetString("Location of {0} is ({1}, {2}).", target.Name, target.TileX, target.TileY));
        }
    }

    [CommandController("warp", Summary = nameof(ControllerSummary))]
    internal static class WarpCommand
    {
        private static string AddSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}warp add [name].", args);
        private static string DeleteSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}warp del [name].", args);
        private static string WarpInvalidWarpMessage(params object?[] args) => GetString("Could not find a warp named {0} to remove.", args);
        private static string HideSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}warp hide [name] <true/false>.", args);
        private static string WarpInvalidWarpMessage2 => GetString("Could not find specified warp.");
        private static string SendSyntaxMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}warp send [player] [warpname].", args);
        private static string TargetPlayerInvalidPlayerMessage => GetString("Invalid target player.");
        private static string WarpInvalidWarpMessage3(params object?[] args) => GetString("The destination warp, {0}, was not found.", args);

        private static string ControllerSummary => GetString("Teleports players using named warp points.");
        private static string ListSummary => GetString("Lists public warps.");
        private static string PageNumberInvalidTokenMessage(params object?[] args) => GetString("\"{0}\" is not a valid page number.", args);
        private static string AddSummary => GetString("Adds a warp at your current position.");
        private static string DeleteSummary => GetString("Deletes a warp.");
        private static string HideSummary => GetString("Changes whether a warp is private.");
        private static string StateInvalidTokenMessage(params object?[] args) => GetString("Invalid syntax. Proper syntax: {0}warp hide [name] <true/false>.", args);
        private static string SendSummary => GetString("Sends a player to a warp.");
        private static string ExecuteDefaultSummary => GetString("Warps you to the named warp.");

        [CommandAction("list", Summary = nameof(ListSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.warp), ServerScope = true)]
        public static CommandOutcome List(
            [PageRef<WarpListPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) {
            var lines = CreateWarpListLines();
            return CommandOutcome.Page(
                pageNumber,
                lines,
                lines.Count,
                CreateWarpListPageSettings());
        }

        [CommandAction("add", Summary = nameof(AddSummary))]
        [RequireUserArgumentCountSyntax(1, nameof(AddSyntaxMessage))]
        [SkipWithoutPermissionPreBind(nameof(Permissions.managewarp))]
        [TShockCommand(nameof(Permissions.warp), PlayerScope = true)]
        public static CommandOutcome Add(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "name")] string warpName) {
            if (!context.Executor.HasPermission(Permissions.managewarp)) {
                return CommandOutcome.Error(GetString("You do not have access to this command."));
            }

            var normalizedWarpName = warpName.Trim();
            if (normalizedWarpName is "list" or "hide" or "del" or "add") {
                return CommandOutcome.Error(GetString("Invalid warp name. The names 'list', 'hide', 'del' and 'add' are reserved for commands."));
            }

            var tsPlayer = context.Player!;
            var worldId = context.Server!.Main.worldID.ToString();
            return TShock.Warps.Add(worldId, tsPlayer.CenterTileX, tsPlayer.UnmountedTileY, normalizedWarpName)
                ? CommandOutcome.Success(GetString("Warp added: {0}.", normalizedWarpName))
                : CommandOutcome.Error(GetString("Warp {0} already exists.", normalizedWarpName));
        }

        [CommandAction("del", Summary = nameof(DeleteSummary))]
        [RequireUserArgumentCountSyntax(1, nameof(DeleteSyntaxMessage))]
        [SkipWithoutPermissionPreBind(nameof(Permissions.managewarp))]
        [TShockCommand(nameof(Permissions.warp), ServerScope = true)]
        public static CommandOutcome Delete(
            [FromAmbientContext] TSExecutionContext context,
            [WarpRef(nameof(WarpInvalidWarpMessage), Name = "name", LookupMode = TSLookupMatchMode.ExactOnly)] Warp warp) {
            if (!context.Executor.HasPermission(Permissions.managewarp)) {
                return CommandOutcome.Error(GetString("You do not have access to this command."));
            }

            return TShock.Warps.Remove(warp.WorldID, warp.Name)
                ? CommandOutcome.Success(GetString("Warp deleted: {0}", warp.Name))
                : CommandOutcome.Error(GetString("Could not find a warp named {0} to remove.", warp.Name));
        }

        [CommandAction("hide", Summary = nameof(HideSummary))]
        [RequireUserArgumentCountSyntax(2, nameof(HideSyntaxMessage))]
        [SkipWithoutPermissionPreBind(nameof(Permissions.managewarp))]
        [TShockCommand(nameof(Permissions.warp), ServerScope = true)]
        public static CommandOutcome Hide(
            [FromAmbientContext] TSExecutionContext context,
            [WarpRef(nameof(WarpInvalidWarpMessage2), Name = "name", LookupMode = TSLookupMatchMode.ExactOnly)] Warp warp,
            [BooleanValue(InvalidTokenMessage = nameof(StateInvalidTokenMessage))] bool state) {
            if (!context.Executor.HasPermission(Permissions.managewarp)) {
                return CommandOutcome.Error(GetString("You do not have access to this command."));
            }

            if (!TShock.Warps.Hide(warp.WorldID, warp.Name, state)) {
                return CommandOutcome.Error(GetString("Could not find specified warp."));
            }

            return state
                ? CommandOutcome.Success(GetString("Warp {0} is now private.", warp.Name))
                : CommandOutcome.Success(GetString("Warp {0} is now public.", warp.Name));
        }

        [CommandAction("send", Summary = nameof(SendSummary))]
        [RequireUserArgumentCountSyntax(2, int.MaxValue, nameof(SendSyntaxMessage))]
        [IgnoreTrailingArguments]
        [SkipWithoutPermissionPreBind(nameof(Permissions.tpothers))]
        [TShockCommand(nameof(Permissions.warp), ServerScope = true)]
        public static CommandOutcome Send(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(TargetPlayerInvalidPlayerMessage), Modifiers = CommandParamModifiers.ServerScope)] TSPlayer targetPlayer,
            [WarpRef(nameof(WarpInvalidWarpMessage3), Name = "name", LookupMode = TSLookupMatchMode.ExactOnly)] Warp warp) {
            if (!context.Executor.HasPermission(Permissions.tpothers)) {
                return CommandOutcome.Error(GetString("You do not have access to this command."));
            }

            if (!targetPlayer.Teleport(new Vector2(warp.Position.X * 16 + (targetPlayer.TPlayer.width / 2f), (warp.Position.Y + 3) * 16), true)) {
                return CommandOutcome.Empty;
            }

            var builder = CommandOutcome.SuccessBuilder(GetString("You warped {0} to {1}.", targetPlayer.Name, warp.Name));
            builder.AddPlayerSuccess(
                targetPlayer,
                GetString("{0} warped you to {1}.", context.Executor.Name, warp.Name),
                phase: CommandOutcomeAttachmentPhase.BeforePrimaryReceipts);
            return builder.Build();
        }

        [CommandAction(Summary = nameof(ExecuteDefaultSummary))]
        [TShockCommand(nameof(Permissions.warp), PlayerScope = true)]
        public static CommandOutcome ExecuteDefault(
            [FromAmbientContext] TSExecutionContext context,
            [WarpRef(nameof(WarpInvalidWarpMessage3), Name = "name", LookupMode = TSLookupMatchMode.ExactOnly)]
            [RemainingText] Warp? warp = null) {
            if (warp is null) {
                if (context.Executor.HasPermission(Permissions.managewarp)) {
                    return CommandOutcome.InfoLines([
                        GetString("Invalid syntax. Proper syntax: {0}warp [command] [arguments].", Commands.Specifier),
                        GetString("Commands: add, del, hide, list, send, [warpname]."),
                        GetString("Arguments: add [warp name], del [warp name], list [page]."),
                        GetString("Arguments: send [player] [warp name], hide [warp name] [Enable(true/false)]."),
                        GetString("Examples: {0}warp add foobar, {0}warp hide foobar true, {0}warp foobar.", Commands.Specifier),
                    ]);
                }

                return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}warp [name] or {0}warp list <page>.", Commands.Specifier));
            }

            var tsPlayer = context.Player!;
            if (!tsPlayer.Teleport(new Vector2(warp.Position.X * 16 + (tsPlayer.TPlayer.width / 2f), (warp.Position.Y + 3) * 16), true)) {
                return CommandOutcome.Empty;
            }

            return CommandOutcome.Success(GetString("Warped to {0}.", warp.Name));
        }

        private sealed class WarpListPageSource : IPageRefSource<WarpListPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var lines = CreateWarpListLines();
                return lines.Count == 0
                    ? null
                    : TSPageRefResolver.CountPages(lines.Count, CreateWarpListPageSettings());
            }
        }

        private static List<string> CreateWarpListLines() {
            return PaginationTools.BuildLinesFromTerms(
                from warp in TShock.Warps.Warps
                where !warp.IsPrivate
                select warp.Name);
        }

        private static PaginationTools.Settings CreateWarpListPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Warps ({0}/{1}):"),
                FooterFormat = GetString("Type {0}warp list {{0}} for more.", Commands.Specifier),
                NothingToDisplayString = GetString("There are currently no warps defined.")
            };
        }
    }
}
