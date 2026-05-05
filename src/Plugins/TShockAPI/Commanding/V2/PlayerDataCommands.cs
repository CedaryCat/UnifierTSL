using UnifierTSL.Commanding;

namespace TShockAPI.Commanding.V2
{
    [CommandController("savessc", Summary = nameof(ControllerSummary))]
    internal static class SaveSscCommand
    {
        private static string ControllerSummary => GetString("Saves all server-side characters.");
        private static string ExecuteSummary => GetString("Saves all currently tracked server-side characters.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.savessc))]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            var scopedServer = context.Executor.SourceServer;

            List<TSPlayer> playersToSave = [];
            foreach (var player in TShock.Players) {
                if (player is null) {
                    continue;
                }

                var playerServer = player.GetCurrentServer();
                if (playerServer is null) {
                    continue;
                }

                if (scopedServer is not null && playerServer != scopedServer) {
                    continue;
                }

                if (!playerServer.Main.ServerSideCharacter || !player.IsLoggedIn || player.IsDisabledPendingTrashRemoval) {
                    continue;
                }

                playersToSave.Add(player);
            }
            foreach (var player in playersToSave) {
                TShock.CharacterDB.InsertPlayerData(player, true);
            }
            return CommandOutcome.Success(GetString("All server-side character data has been saved."));
        }
    }

    [CommandController("overridessc", Summary = nameof(ControllerSummary))]
    [Aliases("ossc")]
    internal static class OverrideSscCommand
    {
        private static string MatchedPlayerInvalidPlayerMessage(params object?[] args) => GetString("No players matched \"{0}\".", args);

        private static string ControllerSummary => GetString("Overrides server-side character data from a player's current local state.");
        private static string ExecuteSummary => GetString("Overrides one player's server-side character data.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.savessc))]
        public static CommandOutcome Execute(
            [TSPlayerRef(nameof(MatchedPlayerInvalidPlayerMessage), ConsumptionMode = TSPlayerConsumptionMode.GreedyPhrase)] TSPlayer matchedPlayer) {
            var server = matchedPlayer.GetCurrentServer();
            if (server is null || !server.Main.ServerSideCharacter) {
                return CommandOutcome.Error(GetString("Server-side characters is disabled."));
            }

            if (matchedPlayer.IsLoggedIn) {
                return CommandOutcome.Error(GetString("Player \"{0}\" is already logged in.", matchedPlayer.Name));
            }

            if (!matchedPlayer.LoginFailsBySsi) {
                return CommandOutcome.Error(GetString("Player \"{0}\" has to perform a /login attempt first.", matchedPlayer.Name));
            }

            if (matchedPlayer.IsDisabledPendingTrashRemoval) {
                return CommandOutcome.Error(GetString("Player \"{0}\" has to reconnect first, because they need to delete their trash.", matchedPlayer.Name));
            }

            TShock.CharacterDB.InsertPlayerData(matchedPlayer);
            return CommandOutcome.Success(GetString("Server-side character data from \"{0}\" has been replaced by their current local data.", matchedPlayer.Name));
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Correct usage: {0}overridessc|{0}ossc <player name>", Commands.Specifier));
        }
    }

    [CommandController("uploadssc", Summary = nameof(ControllerSummary))]
    internal static class UploadSscCommand
    {
        private static string ExecuteOtherDenialMessage => GetString("You do not have permission to upload another player's character join-state server-side-character data.");
        private static string TargetPlayerInvalidPlayerMessage(params object?[] args) => GetString("No player was found matching '{0}'.", args);

        private static string ControllerSummary => GetString("Uploads player join-state local data into server-side character storage.");
        private static string ExecuteSelfSummary => GetString("Uploads your own join-state SSC data.");
        private static string ExecuteOtherSummary => GetString("Uploads another player's join-state SSC data.");

        [CommandAction(Summary = nameof(ExecuteSelfSummary))]
        [RequireUserArgumentCount(0)]
        [TShockCommand(nameof(Permissions.uploaddata))]
        public static CommandOutcome ExecuteSelf([FromAmbientContext] TSExecutionContext context) {
            return UploadJoinStateData(context.Player);
        }

        [CommandAction(Summary = nameof(ExecuteOtherSummary))]
        [RequireUserArgumentCount(1)]
        [RequirePermissionPreBind(nameof(Permissions.uploadothersdata),
            nameof(ExecuteOtherDenialMessage))]
        [TShockCommand(nameof(Permissions.uploaddata))]
        public static CommandOutcome ExecuteOther(
            [TSPlayerRef(nameof(TargetPlayerInvalidPlayerMessage))] TSPlayer targetPlayer) {
            return UploadJoinStateData(targetPlayer);
        }

        private static CommandOutcome UploadJoinStateData(TSPlayer? targetPlayer) {
            if (targetPlayer is null or TSServerPlayer or TSRestPlayer) {
                return CommandOutcome.ErrorBuilder(GetString("The targeted user cannot have their data uploaded, because they are not a player."))
                    .AddError(GetString("Usage: /uploadssc [playername]."))
                    .Build();
            }

            if (!targetPlayer.IsLoggedIn) {
                return CommandOutcome.Error(GetString("The target player has not logged in yet."));
            }

            if (!TShock.CharacterDB.InsertSpecificPlayerData(targetPlayer, targetPlayer.DataWhenJoined)) {
                return CommandOutcome.Error(GetString("Failed to upload your character data to the server. Are you logged-in to an account?"));
            }

            targetPlayer.DataWhenJoined.RestoreCharacter(targetPlayer);
            var builder = CommandOutcome.SuccessBuilder(GetString("The player's character data was successfully uploaded from their initial connection."));
            builder.AddPlayerSuccess(
                targetPlayer,
                GetString("Your local character data, from your initial connection, has been uploaded to the server."),
                phase: CommandOutcomeAttachmentPhase.BeforePrimaryReceipts);
            return builder.Build();
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Usage: /uploadssc [playername]."));
        }
    }

    [CommandController("sync", Summary = nameof(ControllerSummary))]
    internal static class SyncLocalAreaCommand
    {
        private static string ControllerSummary => GetString("Resyncs the local world area around the current player.");
        private static string ExecuteSummary => GetString("Resends nearby tiles to the executing player.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.synclocalarea), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context) {
            var player = context.Player!;
            player.SendTileSquareCentered(player.TileX, player.TileY, 32);
            return CommandOutcome.Warning(GetString("Sync'd!"));
        }
    }
}
