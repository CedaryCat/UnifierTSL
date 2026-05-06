using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using Terraria.ID;
using TShockAPI.ConsolePrompting;
using TShockAPI.DB;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Semantics;
using UnifierTSL.Commanding;
using UnifierTSL.Logging;
using Timer = System.Timers.Timer;

namespace TShockAPI.Commanding.V2
{
    [Flags]
    internal enum BanAddFlags
    {
        None = 0,
        [CommandFlag("-a")]
        Account = 1 << 0,
        [CommandFlag("-u")]
        Uuid = 1 << 1,
        [CommandFlag("-n")]
        Name = 1 << 2,
        [CommandFlag("-ip")]
        Ip = 1 << 3,
        [CommandFlag("-e")]
        Exact = 1 << 4,
    }

    [CommandController("annoy", Summary = nameof(ControllerSummary))]
    internal static class AnnoyCommand
    {
        private static string TargetInvalidPlayerMessage(params object?[] args) => GetString("Could not find any player named \"{0}\"", args);

        private static string ControllerSummary => GetString("Annoys a player for an amount of time.");
        private static string ExecuteSummary => GetString("Annoys a player for an amount of time.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [RequireUserArgumentCount(2)]
        [TShockCommand(nameof(Permissions.annoy))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage))] TSPlayer target,
            [Int32Value(
                InvalidTokenBehavior = InvalidTokenBehavior.UseDefault,
                OutOfRangeBehavior = OutOfRangeBehavior.UseDefault)]
            int seconds = 0) {
            new Thread(target.Whoopie).Start(seconds);

            var builder = CommandOutcome.SuccessBuilder(GetString("Annoying {0} for {1} seconds.", target.Name, seconds));
            if (!context.Silent) {
                builder.AddPlayerInfo(target, GetString("You are now being annoyed."));
            }

            return builder.Build();
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch([FromAmbientContext] TSExecutionContext context) {
            return CommandOutcome.InfoLines([
                GetString("Annoy Syntax"),
                GetString($"{"annoy".Color(Utils.BoldHighlight)} <{"player".Color(Utils.RedHighlight)}> <{"seconds".Color(Utils.PinkHighlight)}>"),
                GetString($"Example usage: {"annoy".Color(Utils.BoldHighlight)} <{context.Executor.Name.Color(Utils.RedHighlight)}> <{"10".Color(Utils.PinkHighlight)}>"),
                GetString($"You can use {Commands.SilentSpecifier.Color(Utils.GreenHighlight)} instead of {Commands.Specifier.Color(Utils.RedHighlight)} to annoy a player silently."),
            ]);
        }
    }

    [CommandController("rocket", Summary = nameof(ControllerSummary))]
    internal static class RocketCommand
    {
        private static string TargetInvalidPlayerMessage(params object?[] args) => GetString("Could not find any player named \"{0}\"", args);

        private static string ControllerSummary => GetString("Rockets a player upwards. Requires SSC.");
        private static string ExecuteSummary => GetString("Rockets a player upwards. Requires SSC.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.annoy))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage))] TSPlayer target) {
            var server = target.GetCurrentServer();
            var serverPlayer = server.GetExtension<TSServerPlayer>();
            var actor = context.Executor.Player;

            if (!server.Main.ServerSideCharacter) {
                return CommandOutcome.Error(GetString("SSC must be enabled to use this command."));
            }

            if (!target.IsLoggedIn) {
                return CommandOutcome.Error(target.TPlayer.Male
                    ? GetString("Unable to launch {0} because he is not logged in.", target.Name)
                    : GetString("Unable to launch {0} because she is not logged in.", target.Name));
            }

            target.TPlayer.velocity.Y = -50;
            server.NetMessage.SendData((int)PacketTypes.PlayerUpdate, -1, -1, null, target.Index);

            if (context.Silent) {
                return CommandOutcome.Success(target == actor
                    ? GetString("You have launched yourself into space.")
                    : GetString("You have launched {0} into space.", target.Name));
            }

            serverPlayer.BCInfoMessage(target == actor
                ? actor.TPlayer.Male
                    ? GetString("{0} has launched himself into space.", context.Executor.Name)
                    : GetString("{0} has launched herself into space.", context.Executor.Name)
                : GetString("{0} has launched {1} into space.", context.Executor.Name, target.Name));
            return CommandOutcome.Empty;
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch([FromAmbientContext] TSExecutionContext context) {
            return CommandOutcome.InfoLines([
                GetString("Rocket Syntax"),
                GetString($"{"rocket".Color(Utils.BoldHighlight)} <{"player".Color(Utils.RedHighlight)}>"),
                GetString($"Example usage: {"rocket".Color(Utils.BoldHighlight)} {context.Executor.Name.Color(Utils.RedHighlight)}"),
                GetString($"You can use {Commands.SilentSpecifier.Color(Utils.GreenHighlight)} instead of {Commands.Specifier.Color(Utils.RedHighlight)} to rocket a player silently."),
            ]);
        }
    }

    [CommandController("firework", Summary = nameof(ControllerSummary))]
    internal static class FireworkCommand
    {
        private static string TargetInvalidPlayerMessage(params object?[] args) => GetString("Could not find any player named \"{0}\"", args);

        private static string ControllerSummary => GetString("Spawns fireworks at a player.");
        private static string ExecuteSummary => GetString("Spawns fireworks at a player.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.annoy))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage))] TSPlayer target,
            [CommandParam(Name = "style")]
            [CommandPrompt(SuggestionKindId = PromptSuggestionKindIds.Enum, EnumCandidates = ["r", "g", "b", "y", "red", "green", "blue", "yellow", "r2", "g2", "b2", "y2", "star", "spiral", "rings", "flower"])]
            string styleRaw = "") {
            var server = target.GetCurrentServer();
            var projectileType = ResolveFireworkProjectileType(styleRaw);
            var projectileIndex = server.Projectile.NewProjectile(
                Projectile.GetNoneSource(),
                target.TPlayer.position.X,
                target.TPlayer.position.Y - 64f,
                0f,
                -8f,
                projectileType,
                0,
                0);
            server.Main.projectile[projectileIndex].Kill(server);

            var actor = context.Executor.Player;
            var builder = CommandOutcome.SuccessBuilder(target == actor
                ? GetString("You launched fireworks on yourself.")
                : GetString("You launched fireworks on {0}.", target.Name));
            if (!context.Silent && target != actor) {
                builder.AddPlayerSuccess(target, GetString("{0} launched fireworks on you.", context.Executor.Name));
            }

            return builder.Build();
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch([FromAmbientContext] TSExecutionContext context) {
            return CommandOutcome.InfoLines([
                GetString("Firework Syntax"),
                GetString($"{"firework".Color(Utils.CyanHighlight)} <{"player".Color(Utils.PinkHighlight)}> [{"R".Color(Utils.RedHighlight)}|{"G".Color(Utils.GreenHighlight)}|{"B".Color(Utils.BoldHighlight)}|{"Y".Color(Utils.YellowHighlight)}]"),
                GetString($"Example usage: {"firework".Color(Utils.CyanHighlight)} {context.Executor.Name.Color(Utils.PinkHighlight)} {"R".Color(Utils.RedHighlight)}"),
                GetString($"You can use {Commands.SilentSpecifier.Color(Utils.GreenHighlight)} instead of {Commands.Specifier.Color(Utils.RedHighlight)} to launch a firework silently."),
            ]);
        }

        private static int ResolveFireworkProjectileType(string styleRaw) {
            return styleRaw.Trim().ToLowerInvariant() switch {
                "green" or "g" => ProjectileID.RocketFireworkGreen,
                "blue" or "b" => ProjectileID.RocketFireworkBlue,
                "yellow" or "y" => ProjectileID.RocketFireworkYellow,
                "r2" or "star" => ProjectileID.RocketFireworksBoxRed,
                "g2" or "spiral" => ProjectileID.RocketFireworksBoxGreen,
                "b2" or "rings" => ProjectileID.RocketFireworksBoxBlue,
                "y2" or "flower" => ProjectileID.RocketFireworksBoxYellow,
                _ => ProjectileID.RocketFireworkRed,
            };
        }
    }

    [CommandController("kick", Summary = nameof(ControllerSummary))]
    internal static class KickCommand
    {
        private static string TargetInvalidPlayerMessage => GetString("Player not found. Unable to kick the player.");

        private static string ControllerSummary => GetString("Removes a player from the server.");
        private static string ExecuteSummary => GetString("Removes a player from the server.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.kick))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage))] TSPlayer target,
            [RemainingText] string reason = "") {
            var resolvedReason = string.IsNullOrWhiteSpace(reason)
                ? GetString("Misbehaviour.")
                : reason;
            return target.Kick(resolvedReason, !context.Executor.IsClient, false, context.Executor.Name)
                ? CommandOutcome.Empty
                : CommandOutcome.Error(GetString("You can't kick another admin."));
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}kick <player> [reason].", Commands.Specifier));
        }
    }

    [CommandController("mute", Summary = nameof(ControllerSummary))]
    [Aliases("unmute")]
    internal static class MuteCommand
    {
        private static string TargetInvalidPlayerMessage(params object?[] args) => GetString("Could not find any players named \"{0}\"", args);

        private static string ControllerSummary => GetString("Prevents a player from talking.");
        private static string ExecuteSummary => GetString("Toggles whether a player is muted.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.mute))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage))] TSPlayer target,
            [RemainingText] string reason = "") {
            if (target.HasPermission(Permissions.mute)) {
                return CommandOutcome.Error(GetString($"You do not have permission to mute {target.Name}"));
            }

            var serverPlayer = target.GetCurrentServer().GetExtension<TSServerPlayer>();
            if (target.mute) {
                target.mute = false;
                if (context.Silent) {
                    return CommandOutcome.Success(GetString($"You have unmuted {target.Name}."));
                }

                serverPlayer.BCInfoMessage(GetString($"{context.Executor.Name} has unmuted {target.Name}."));
                return CommandOutcome.Empty;
            }

            var resolvedReason = string.IsNullOrWhiteSpace(reason)
                ? GetString("No reason specified.")
                : reason;
            target.mute = true;
            if (context.Silent) {
                return CommandOutcome.Success(GetString($"You have muted {target.Name} for {resolvedReason}"));
            }

            serverPlayer.BCInfoMessage(GetString($"{context.Executor.Name} has muted {target.Name} for {resolvedReason}."));
            return CommandOutcome.Empty;
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.InfoLines([
                GetString("Mute Syntax"),
                GetString($"{"mute".Color(Utils.BoldHighlight)} <{"player".Color(Utils.RedHighlight)}> [{"reason".Color(Utils.GreenHighlight)}]"),
                GetString($"Example usage: {"mute".Color(Utils.BoldHighlight)} \"{"player".Color(Utils.RedHighlight)}\" \"{"No swearing on my Christian server".Color(Utils.GreenHighlight)}\""),
                GetString($"To mute a player without broadcasting to chat, use the command with {Commands.SilentSpecifier.Color(Utils.GreenHighlight)} instead of {Commands.Specifier.Color(Utils.RedHighlight)}"),
            ]);
        }
    }

    [CommandController("slap", Summary = nameof(ControllerSummary))]
    internal static class SlapCommand
    {
        private static string TargetInvalidPlayerMessage => GetString("Invalid target player.");

        private static string ControllerSummary => GetString("Damages a player.");
        private static string ExecuteSummary => GetString("Damages a player for a small configurable amount.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.slap))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage))] TSPlayer target,
            string damageRaw = "") {
            var damage = 5;
            if (!string.IsNullOrWhiteSpace(damageRaw) && int.TryParse(damageRaw, out var parsedDamage)) {
                damage = parsedDamage;
            }

            if (!context.Executor.HasPermission(Permissions.kill)) {
                damage = Utils.Clamp(damage, 15, 0);
            }

            target.DamagePlayer(damage);

            var message = GetString("{0} slapped {1} for {2} damage.", context.Executor.Name, target.Name, damage);
            target.GetCurrentServer().GetExtension<TSServerPlayer>().BCInfoMessage(message);
            return CommandOutcome.CreateBuilder()
                .AddLog(LogLevel.Info, message)
                .Build();
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}slap <player> [damage].", Commands.Specifier));
        }
    }

    [CommandController("userinfo", Summary = nameof(ControllerSummary))]
    [Aliases("ui")]
    internal static class UserInfoCommand
    {
        private static string ControllerSummary => GetString("Shows information about a player.");
        private static string ExecuteSummary => GetString("Shows information about a player.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.userinfo))]
        public static CommandOutcome Execute(TSPlayer target) {
            var builder = CommandOutcome.SuccessBuilder(GetString($"IP Address: {target.IP}."));
            if (target.Account is not null && target.IsLoggedIn) {
                builder.AddSuccess(GetString($" -> Logged-in as: {target.Account.Name}; in group {target.Group.Name}."));
            }

            return builder.Build();
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}userinfo <player>.", Commands.Specifier));
        }
    }

    [CommandController("accountinfo", Summary = nameof(ControllerSummary))]
    [Aliases("ai")]
    internal static class AccountInfoCommand
    {
        private static string AccountInvalidUserAccountMessage(params object?[] args) => GetString("User {0} does not exist.", args);

        private static string ControllerSummary => GetString("Shows information about a user.");
        private static string ExecuteSummary => GetString("Shows information about a user.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.checkaccountinfo))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText]
            [UserAccountRef(nameof(AccountInvalidUserAccountMessage))]
            UserAccount account) {

            var builder = CommandOutcome.CreateBuilder();
            var timezone = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).Hours.ToString("+#;-#");

            if (DateTime.TryParse(account.LastAccessed, out _)) {
                var localLastSeen = DateTime.Parse(account.LastAccessed).ToLocalTime();
                builder.AddSuccess(GetString(
                    "{0}'s last login occurred {1} {2} UTC{3}.",
                    account.Name,
                    localLastSeen.ToShortDateString(),
                    localLastSeen.ToShortTimeString(),
                    timezone));
            }

            if (!context.Executor.HasPermission(Permissions.advaccountinfo)) {
                return builder.Build();
            }

            var ip = ResolveLastKnownIp(account.KnownIps);
            var registered = DateTime.Parse(account.Registered).ToLocalTime();

            builder.AddSuccess(GetString("{0}'s group is {1}.", account.Name, account.Group));
            builder.AddSuccess(GetString("{0}'s last known IP is {1}.", account.Name, ip));
            builder.AddSuccess(GetString(
                "{0}'s register date is {1} {2} UTC{3}.",
                account.Name,
                registered.ToShortDateString(),
                registered.ToShortTimeString(),
                timezone));
            return builder.Build();
        }

        private static string ResolveLastKnownIp(string knownIpsJson) {
            if (string.IsNullOrWhiteSpace(knownIpsJson)) {
                return GetString("N/A");
            }

            try {
                var knownIps = JsonConvert.DeserializeObject<List<string>>(knownIpsJson);
                for (var i = knownIps?.Count - 1 ?? -1; i >= 0; i--) {
                    var candidate = knownIps![i]?.Trim() ?? string.Empty;
                    if (candidate.Length > 0) {
                        return candidate;
                    }
                }
            }
            catch (JsonException) {
            }

            return GetString("N/A");
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.Error(GetString("Invalid syntax. Proper syntax: {0}accountinfo <username>.", Commands.Specifier));
        }
    }

    [CommandController("tempgroup", Summary = nameof(ControllerSummary))]
    internal static class TempGroupCommand
    {
        private static string TargetInvalidPlayerMessage(params object?[] args) => GetString("Could not find player {0}.", args);
        private static string GroupInvalidGroupMessage(params object?[] args) => GetString("Could not find group {0}", args);

        private static string ControllerSummary => GetString("Temporarily sets another player's group.");
        private static string ExecuteSummary => GetString("Temporarily sets another player's group.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [RequireUserArgumentCount(2, int.MaxValue)]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.settempgroup))]
        public static CommandOutcome Execute(
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage), MultipleMatchDisplay = TSPlayerMatchDisplay.AccountNameOrName)] TSPlayer target,
            [GroupRef(nameof(GroupInvalidGroupMessage))] Group group,
            string duration = "") {
            if (!string.IsNullOrWhiteSpace(duration)) {
                if (!Utils.TryParseTime(duration, out ulong seconds)) {
                    return CommandOutcome.InfoLines([
                        GetString("Invalid time string! Proper format: _d_h_m_s, with at least one time specifier."),
                        GetString("For example, 1d and 10h-30m+2m are both valid time strings, but 2 is not."),
                    ]);
                }

                target.tempGroupTimer = new Timer(seconds * 1000d);
                target.tempGroupTimer.Elapsed += target.TempGroupTimerElapsed;
                target.tempGroupTimer.Start();
            }

            target.tempGroup = group;

            var builder = CommandOutcome.SuccessBuilder(
                string.IsNullOrWhiteSpace(duration)
                    ? GetString("You have changed {0}'s group to {1}", target.Name, group.Name)
                    : GetString("You have changed {0}'s group to {1} for {2}", target.Name, group.Name, duration));
            builder.AddPlayerSuccess(
                target,
                string.IsNullOrWhiteSpace(duration)
                    ? GetString("Your group has temporarily been changed to {0}", group.Name)
                    : GetString("Your group has been changed to {0} for {1}", group.Name, duration));
            return builder.Build();
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() {
            return CommandOutcome.InfoLines([
                GetString("Invalid syntax."),
                GetString("Usage: {0}tempgroup <username> <new group> [time]", Commands.Specifier),
            ]);
        }
    }

    [CommandController("respawn", Summary = nameof(ControllerSummary))]
    internal static class RespawnCommand
    {
        private static string RespawnOtherDenialMessage => GetString("You do not have permission to respawn another player.");
        private static string TargetInvalidPlayerMessage(params object?[] args) => GetString("Could not find any player named \"{0}\"", args);

        private static string ControllerSummary => GetString("Respawns a player.");
        private static string RespawnSelfSummary => GetString("Respawns yourself.");
        private static string RespawnOtherSummary => GetString("Respawns another player.");

        [CommandAction(Summary = nameof(RespawnSelfSummary))]
        [TShockCommand(nameof(Permissions.respawn))]
        public static CommandOutcome RespawnSelf([FromAmbientContext] TSExecutionContext context) {
            if (context.Player is null) {
                return CommandOutcome.Error(GetString("You can't respawn the server console!"));
            }

            if (!context.Player.Dead) {
                return CommandOutcome.Error(GetString("You are not dead!"));
            }

            context.Player.Spawn(PlayerSpawnContext.ReviveFromDeath);
            return CommandOutcome.Success(GetString("You have respawned yourself."));
        }

        [CommandAction(Summary = nameof(RespawnOtherSummary))]
        [RequirePermissionPreBind(nameof(Permissions.respawnother), nameof(RespawnOtherDenialMessage))]
        [TShockCommand(nameof(Permissions.respawn))]
        public static CommandOutcome RespawnOther(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage), ConsumptionMode = TSPlayerConsumptionMode.GreedyPhrase)] TSPlayer target) {
            var executorPlayer = context.Player;
            if (!target.Dead) {
                return target == executorPlayer
                    ? CommandOutcome.Error(GetString("You are not dead!"))
                    : CommandOutcome.Error(GetString($"{target.Name} is not dead!"));
            }

            target.Spawn(PlayerSpawnContext.ReviveFromDeath);

            if (target == executorPlayer) {
                return CommandOutcome.Success(GetString("You have respawned yourself."));
            }

            var builder = CommandOutcome.SuccessBuilder(GetString($"You have respawned {target.Name}"));
            if (!context.Silent) {
                builder.AddPlayerSuccess(target, GetString($"{context.Executor.Name} has respawned you."));
            }

            return builder.Build();
        }
    }

    [CommandController("godmode", Summary = nameof(ControllerSummary))]
    [Aliases("god")]
    internal static class GodModeCommand
    {
        private static string ToggleOtherDenialMessage => GetString("You do not have permission to god mode another player.");
        private static string TargetInvalidPlayerMessage => GetString("Invalid player!");

        private static string ControllerSummary => GetString("Toggles journey godmode.");
        private static string ToggleSelfSummary => GetString("Toggles godmode for yourself.");
        private static string ToggleOtherSummary => GetString("Toggles godmode for another player.");

        [CommandAction(Summary = nameof(ToggleSelfSummary))]
        [TShockCommand(nameof(Permissions.godmode))]
        public static CommandOutcome ToggleSelf([FromAmbientContext] TSExecutionContext context) {
            if (context.Player is null) {
                return CommandOutcome.Error(GetString("You can't god mode a non player!"));
            }

            context.Player.GodMode = !context.Player.GodMode;
            return CommandOutcome.Success(context.Player.GodMode
                ? GetString("You are now in god mode.", context.Player.Name)
                : GetString("You are no longer in god mode.", context.Player.Name));
        }

        [CommandAction(Summary = nameof(ToggleOtherSummary))]
        [RequirePermissionPreBind(nameof(Permissions.godmodeother), nameof(ToggleOtherDenialMessage))]
        [TShockCommand(nameof(Permissions.godmode))]
        public static CommandOutcome ToggleOther(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(nameof(TargetInvalidPlayerMessage), ConsumptionMode = TSPlayerConsumptionMode.GreedyPhrase)] TSPlayer target) {
            var executorPlayer = context.Player;
            target.GodMode = !target.GodMode;

            var builder = CommandOutcome.CreateBuilder();
            if (target != executorPlayer) {
                builder.AddSuccess(target.GodMode
                    ? GetString("{0} is now in god mode.", target.Name)
                    : GetString("{0} is no longer in god mode.", target.Name));
            }

            if (!context.Silent || target == executorPlayer) {
                builder.AddPlayerSuccess(
                    target,
                    target.GodMode
                        ? GetString("You are now in god mode.", target.Name)
                        : GetString("You are no longer in god mode.", target.Name));
            }

            return builder.Build();
        }
    }

    [CommandController("ban", Summary = nameof(ControllerSummary))]
    internal static class BanCommand
    {
        private static string ControllerSummary => GetString("Manages player bans.");
        private static string DefaultSummary => GetString("Shows ban command help.");
        private static string HelpSummary => GetString("Shows help for a ban subcommand.");
        private static string HelpTopicSummary => GetString("Shows help for a ban subcommand.");
        private static string HelpIdentifiersSummary => GetString("Shows available ban identifiers.");
        private static string PageNumberInvalidTokenMessage => GetString("Invalid page number. Page number must be numeric.");
        private static string AddSummary => GetString("Adds a ban by player or exact identifier.");
        private static string DeleteSummary => GetString("Expires an active ban.");
        private static string ListSummary => GetString("Lists active bans.");
        private static string PageNumberInvalidTokenMessage2(params object?[] args) => GetString("\"{0}\" is not a valid page number.", args);
        private static string DetailsSummary => GetString("Shows details for a ban ticket.");

        [CommandAction(Summary = nameof(DefaultSummary))]
        [TShockCommand(nameof(Permissions.ban))]
        public static CommandOutcome Default([FromAmbientContext] TSExecutionContext context) {
            return BuildHelp(context.Executor, topic: null);
        }

        [CommandAction("help", Summary = nameof(HelpSummary))]
        [TShockCommand(nameof(Permissions.ban))]
        public static CommandOutcome Help([FromAmbientContext] TSExecutionContext context) => BuildHelp(context.Executor, topic: null);

        [CommandAction("help", Summary = nameof(HelpTopicSummary))]
        [TShockCommand(nameof(Permissions.ban))]
        public static CommandOutcome HelpTopic(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "topic")] string topic) => BuildHelp(context.Executor, topic);

        [CommandAction("help identifiers", Summary = nameof(HelpIdentifiersSummary))]
        [TShockCommand(nameof(Permissions.ban))]
        public static CommandOutcome HelpIdentifiers(
            [PageRef<BanIdentifierPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) => BuildIdentifierHelp(pageNumber);

        [CommandAction("add", Summary = nameof(AddSummary))]
        [TShockCommand(nameof(Permissions.ban))]
        public static CommandOutcome Add(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "target")] string target,
            [CommandParam(Name = "reason")] string reason = "",
            [CommandParam(Name = "duration")] string duration = "",
            [CommandFlags<BanAddFlags>] BanAddFlags flags = BanAddFlags.None) {
            var targetToken = target;
            var exactTarget = (flags & BanAddFlags.Exact) != 0;
            var banAccount = (flags & BanAddFlags.Account) != 0;
            var banUuid = (flags & BanAddFlags.Uuid) != 0;
            var banName = (flags & BanAddFlags.Name) != 0;
            var banIp = (flags & BanAddFlags.Ip) != 0;

            var resolvedReason = string.IsNullOrEmpty(reason)
                ? GetString("Banned.")
                : reason;
            var durationToken = string.IsNullOrEmpty(duration)
                ? null
                : duration;

            var expiration = DateTime.MaxValue;
            if (durationToken is not null && Utils.TryParseTime(durationToken, out ulong seconds)) {
                expiration = DateTime.UtcNow.AddSeconds(seconds);
            }

            if (!exactTarget && !banAccount && !banUuid && !banName && !banIp) {
                banAccount = true;
                banUuid = true;
                banIp = !TShock.Config.GlobalSettings.DisableDefaultIPBan;
            }

            var builder = CommandOutcome.CreateBuilder();
            if (exactTarget) {
                BanCommandHelpers.AddBan(builder, context.Executor, targetToken, resolvedReason, expiration);
                return builder.Build();
            }

            var targetMatches = TSPlayer.FindByNameOrID(targetToken);
            if (targetMatches.Count == 0) {
                return CommandOutcome.Error(GetString("Could not find the target specified. Check that you have the correct spelling."));
            }

            if (targetMatches.Count > 1) {
                return CommandOutcome.MultipleMatches(targetMatches.Select(static player => player.Name));
            }

            var targetPlayer = targetMatches[0];
            AddBanResult? lastBan = null;
            if (banAccount && targetPlayer.Account is not null) {
                lastBan = BanCommandHelpers.AddBan(
                    builder,
                    context.Executor,
                    $"{Identifier.Account}{targetPlayer.Account.Name}",
                    resolvedReason,
                    expiration);
            }

            if (banUuid && targetPlayer.UUID.Length > 0) {
                lastBan = BanCommandHelpers.AddBan(
                    builder,
                    context.Executor,
                    $"{Identifier.UUID}{targetPlayer.UUID}",
                    resolvedReason,
                    expiration);
            }

            if (banName) {
                lastBan = BanCommandHelpers.AddBan(
                    builder,
                    context.Executor,
                    $"{Identifier.Name}{targetPlayer.Name}",
                    resolvedReason,
                    expiration);
            }

            if (banIp) {
                lastBan = BanCommandHelpers.AddBan(
                    builder,
                    context.Executor,
                    $"{Identifier.IP}{targetPlayer.IP}",
                    resolvedReason,
                    expiration);
            }

            if (lastBan?.Ban is not null) {
                targetPlayer.Disconnect(GetString($"#{lastBan.Ban.TicketNumber} - You have been banned: {lastBan.Ban.Reason}."));
            }

            return builder.Build();
        }

        [CommandAction("del", Summary = nameof(DeleteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.ban))]
        public static CommandOutcome Delete(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "ticket")]
            [CommandPromptSemantic<TSCommandPromptParamKeys>(nameof(TSCommandPromptParamKeys.BanTicket))] string ticket) {
            if (!int.TryParse(ticket, out var banId)) {
                return CommandOutcome.Info(GetString($"Invalid Ticket Number. Refer to {"ban help del".Color(Utils.BoldHighlight)} for details on how to use the {"ban del".Color(Utils.BoldHighlight)} command"));
            }

            if (!TShock.Bans.RemoveBan(banId)) {
                return CommandOutcome.Error(GetString("Failed to remove ban."));
            }

            return CommandOutcome.SuccessBuilder(GetString($"Ban {banId.Color(Utils.GreenHighlight)} has now been marked as expired."))
                .AddLog(LogLevel.Info, GetString($"Ban {banId} has been revoked by {context.Executor.Account.Name}."))
                .Build();
        }

        [CommandAction("list", Summary = nameof(ListSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.ban))]
        public static CommandOutcome List(
            [PageRef<BanListPageSource>(
                InvalidTokenMessage = nameof(PageNumberInvalidTokenMessage2),
                UpperBoundBehavior = PageRefUpperBoundBehavior.ValidateKnownCount)]
            int pageNumber = 1) {
            var bans = CreateBanListLines();
            return CommandOutcome.Page(
                pageNumber,
                bans,
                bans.Count,
                CreateBanListPageSettings());
        }

        [CommandAction("details", Summary = nameof(DetailsSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.ban))]
        public static CommandOutcome Details(
            [CommandParam(Name = "ticket")]
            [CommandPromptSemantic<TSCommandPromptParamKeys>(nameof(TSCommandPromptParamKeys.BanTicket))] string ticket) {
            if (!int.TryParse(ticket, out var banId)) {
                return CommandOutcome.Info(GetString($"Invalid Ticket Number. Refer to {"ban help details".Color(Utils.BoldHighlight)} for details on how to use the {"ban details".Color(Utils.BoldHighlight)} command"));
            }

            var ban = TShock.Bans.GetBanById(banId);
            return ban is null
                ? CommandOutcome.Error(GetString("No bans found matching the provided ticket number."))
                : CommandOutcome.InfoLines(BuildBanDetails(ban));
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch(CommandInvocationContext invocation) {
            if (invocation.UserArguments.Length == 0) {
                return CommandOutcome.Empty;
            }

            return invocation.UserArguments[0].ToLowerInvariant() switch {
                "add" => CommandOutcome.Info(GetString($"Invalid Ban Add syntax. Refer to {"ban help add".Color(Utils.BoldHighlight)} for details on how to use the {"ban add".Color(Utils.BoldHighlight)} command")),
                "del" => CommandOutcome.Info(GetString($"Invalid Ban Del syntax. Refer to {"ban help del".Color(Utils.BoldHighlight)} for details on how to use the {"ban del".Color(Utils.BoldHighlight)} command")),
                "details" => CommandOutcome.Info(GetString($"Invalid Ban Details syntax. Refer to {"ban help details".Color(Utils.BoldHighlight)} for details on how to use the {"ban details".Color(Utils.BoldHighlight)} command")),
                _ => CommandOutcome.Empty,
            };
        }

        private static CommandOutcome BuildHelp(CommandExecutor executor, string? topic) {
            if (string.IsNullOrWhiteSpace(topic)) {
                return CommandOutcome.InfoLines([
                    GetString("TShock Ban Help"),
                    GetString("Available Ban commands:"),
                    GetString($"ban {"add".Color(Utils.RedHighlight)} <Target> [Flags]"),
                    GetString($"ban {"del".Color(Utils.RedHighlight)} <Ban ID>"),
                    GetString($"ban {"list".Color(Utils.RedHighlight)}"),
                    GetString($"ban {"details".Color(Utils.RedHighlight)} <Ban ID>"),
                    GetString($"Quick usage: {"ban add".Color(Utils.BoldHighlight)} {executor.Name.Color(Utils.RedHighlight)} \"Griefing\""),
                    GetString($"For more info, use {"ban help".Color(Utils.BoldHighlight)} {"command".Color(Utils.RedHighlight)} or {"ban help".Color(Utils.BoldHighlight)} {"examples".Color(Utils.RedHighlight)}"),
                ]);
            }

            switch (topic.ToLowerInvariant()) {
                case "add":
                    return CommandOutcome.InfoLines([
                        GetString("Ban Add Syntax"),
                        GetString($"{"ban add".Color(Utils.BoldHighlight)} <{"Target".Color(Utils.RedHighlight)}> [{"Reason".Color(Utils.BoldHighlight)}] [{"Duration".Color(Utils.PinkHighlight)}] [{"Flags".Color(Utils.GreenHighlight)}]"),
                        GetString($"- {"Duration".Color(Utils.PinkHighlight)}: uses the format {"0d0m0s".Color(Utils.PinkHighlight)} to determine the length of the ban."),
                        GetString($"   Eg a value of {"10d30m0s".Color(Utils.PinkHighlight)} would represent 10 days, 30 minutes, 0 seconds."),
                        GetString($"   If no duration is provided, the ban will be permanent."),
                        GetString($"- {"Flags".Color(Utils.GreenHighlight)}: -a (account name), -u (UUID), -n (character name), -ip (IP address), -e (exact, {"Target".Color(Utils.RedHighlight)} will be treated as identifier)"),
                        GetString($"   Flags may appear anywhere after the target and do not consume the {"Reason".Color(Utils.BoldHighlight)} or {"Duration".Color(Utils.PinkHighlight)} slots."),
                        GetString($"   Unless {"-e".Color(Utils.GreenHighlight)} is passed to the command, {"Target".Color(Utils.RedHighlight)} is assumed to be a player or player index"),
                        GetString($"   If no {"Flags".Color(Utils.GreenHighlight)} are specified, the command uses {"-a -u -ip".Color(Utils.GreenHighlight)} by default."),
                        GetString($"Example usage: {"ban add".Color(Utils.BoldHighlight)} {executor.Name.Color(Utils.RedHighlight)} {"-a -u -ip".Color(Utils.GreenHighlight)} {"\"Cheating\"".Color(Utils.BoldHighlight)} {"10d30m0s".Color(Utils.PinkHighlight)}"),
                    ]);
                case "del":
                    return CommandOutcome.InfoLines([
                        GetString("Ban Del Syntax"),
                        GetString($"{"ban del".Color(Utils.BoldHighlight)} <{"Ticket Number".Color(Utils.RedHighlight)}>"),
                        GetString($"- {"Ticket Numbers".Color(Utils.RedHighlight)} are provided when you add a ban, and can also be viewed with the {"ban list".Color(Utils.BoldHighlight)} command."),
                        GetString($"Example usage: {"ban del".Color(Utils.BoldHighlight)} {"12345".Color(Utils.RedHighlight)}"),
                    ]);
                case "list":
                    return CommandOutcome.InfoLines([
                        GetString("Ban List Syntax"),
                        GetString($"{"ban list".Color(Utils.BoldHighlight)} [{"Page".Color(Utils.PinkHighlight)}]"),
                        GetString("- Lists active bans. Color trends towards green as the ban approaches expiration"),
                        GetString($"Example usage: {"ban list".Color(Utils.BoldHighlight)}"),
                    ]);
                case "details":
                    return CommandOutcome.InfoLines([
                        GetString("Ban Details Syntax"),
                        GetString($"{"ban details".Color(Utils.BoldHighlight)} <{"Ticket Number".Color(Utils.RedHighlight)}>"),
                        GetString($"- {"Ticket Numbers".Color(Utils.RedHighlight)} are provided when you add a ban, and can be found with the {"ban list".Color(Utils.BoldHighlight)} command."),
                        GetString($"Example usage: {"ban details".Color(Utils.BoldHighlight)} {"12345".Color(Utils.RedHighlight)}"),
                    ]);
                case "identifiers":
                    return BuildIdentifierHelp(pageNumber: 1);
                case "examples":
                    return CommandOutcome.InfoLines([
                        GetString("Ban Usage Examples"),
                        GetString("- Ban an offline player by account name"),
                        GetString($"   {Commands.Specifier}{"ban add".Color(Utils.BoldHighlight)} \"{"acc:".Color(Utils.RedHighlight)}{executor.Account.Color(Utils.RedHighlight)}\" {"\"Multiple accounts are not allowed\"".Color(Utils.BoldHighlight)} {"-e".Color(Utils.GreenHighlight)} (Permanently bans this account name)"),
                        GetString("- Ban an offline player by IP address"),
                        GetString($"   {Commands.Specifier}{"ai".Color(Utils.BoldHighlight)} \"{executor.Account.Color(Utils.RedHighlight)}\" (Find the IP associated with the offline target's account)"),
                        GetString($"   {Commands.Specifier}{"ban add".Color(Utils.BoldHighlight)} {"ip:".Color(Utils.RedHighlight)}{executor.IP.Color(Utils.RedHighlight)} {"\"Griefing\"".Color(Utils.BoldHighlight)} {"-e".Color(Utils.GreenHighlight)} (Permanently bans this IP address)"),
                        GetString($"- Ban an online player by index (Useful for hard to type names)"),
                        GetString($"   {Commands.Specifier}{"who".Color(Utils.BoldHighlight)} {"-i".Color(Utils.GreenHighlight)} (Find the player index for the target)"),
                        GetString($"   {Commands.Specifier}{"ban add".Color(Utils.BoldHighlight)} {"tsi:".Color(Utils.RedHighlight)}{(executor.IsClient ? executor.UserId : 0).Color(Utils.RedHighlight)} {"-a -u -ip".Color(Utils.GreenHighlight)} {"\"Trolling\"".Color(Utils.BoldHighlight)} (Permanently bans the online player by Account, UUID, and IP)"),
                    ]);
                default:
                    return CommandOutcome.Info(GetString($"Unknown ban command. Try {"ban help".Color(Utils.BoldHighlight)} {"add".Color(Utils.RedHighlight)}, {"del".Color(Utils.RedHighlight)}, {"list".Color(Utils.RedHighlight)}, {"details".Color(Utils.RedHighlight)}, {"identifiers".Color(Utils.RedHighlight)}, or {"examples".Color(Utils.RedHighlight)}."));
            }
        }

        private static CommandOutcome BuildIdentifierHelp(int pageNumber) {
            var identifiers = CreateIdentifierHelpLines();
            return CommandOutcome.Page(
                pageNumber,
                identifiers,
                identifiers.Count,
                CreateIdentifierHelpPageSettings());
        }

        private static List<string> CreateBanListLines() {
            return [.. from ban in TShock.Bans.Bans
                        where ban.Value.ExpirationDateTime > DateTime.UtcNow
                        orderby ban.Value.ExpirationDateTime ascending
                        select $"[{ban.Key.Color(Utils.GreenHighlight)}] {ban.Value.Identifier.Color(PickColorForBan(ban.Value))}"];
        }

        private static PaginationTools.Settings CreateBanListPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Bans ({0}/{1}):"),
                FooterFormat = GetString("Type {0}ban list {{0}} for more.", Commands.Specifier),
                NothingToDisplayString = GetString("There are currently no active bans."),
            };
        }

        private static List<string> CreateIdentifierHelpLines() {
            return [.. Identifier.Available.Select(static ident => $"{ident.Color(Utils.RedHighlight)} - {ident.Description}")];
        }

        private static PaginationTools.Settings CreateIdentifierHelpPageSettings() {
            return new PaginationTools.Settings {
                HeaderFormat = GetString("Available identifiers ({0}/{1}):"),
                FooterFormat = GetString("Type {0}ban help identifiers {{0}} for more.", Commands.Specifier),
                NothingToDisplayString = GetString("There are currently no available identifiers."),
                HeaderTextColor = Color.White,
                LineTextColor = Color.White,
            };
        }

        private sealed class BanListPageSource : IPageRefSource<BanListPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var bans = CreateBanListLines();
                return bans.Count == 0
                    ? null
                    : TSPageRefResolver.CountPages(bans.Count, CreateBanListPageSettings());
            }
        }

        private sealed class BanIdentifierPageSource : IPageRefSource<BanIdentifierPageSource>
        {
            public static int? GetPageCount(PageRefSourceContext context) {
                var identifiers = CreateIdentifierHelpLines();
                return identifiers.Count == 0
                    ? null
                    : TSPageRefResolver.CountPages(identifiers.Count, CreateIdentifierHelpPageSettings());
            }
        }

        private static IEnumerable<string> BuildBanDetails(Ban ban) {
            yield return GetString($"{"Ban Details".Color(Utils.BoldHighlight)} - Ticket Number: {ban.TicketNumber.Color(Utils.GreenHighlight)}");
            yield return GetString($"{"Identifier:".Color(Utils.BoldHighlight)} {ban.Identifier}");
            yield return GetString($"{"Reason:".Color(Utils.BoldHighlight)} {ban.Reason}");
            yield return GetString($"{"Banned by:".Color(Utils.BoldHighlight)} {ban.BanningUser.Color(Utils.GreenHighlight)} on {ban.BanDateTime.ToString("yyyy/MM/dd").Color(Utils.RedHighlight)} ({ban.GetPrettyTimeSinceBanString().Color(Utils.YellowHighlight)} ago)");
            if (ban.ExpirationDateTime < DateTime.UtcNow) {
                yield return GetString($"{"Ban expired:".Color(Utils.BoldHighlight)} {ban.ExpirationDateTime.ToString("yyyy/MM/dd").Color(Utils.RedHighlight)} ({ban.GetPrettyExpirationString().Color(Utils.YellowHighlight)} ago)");
                yield break;
            }

            var remaining = ban.ExpirationDateTime == DateTime.MaxValue
                ? GetString("Never.").Color(Utils.YellowHighlight)
                : GetString($"{ban.GetPrettyExpirationString().Color(Utils.YellowHighlight)} remaining.");
            yield return GetString($"{"Ban expires:".Color(Utils.BoldHighlight)} {ban.ExpirationDateTime.ToString("yyyy/MM/dd").Color(Utils.RedHighlight)} ({remaining})");
        }

        private static string PickColorForBan(Ban ban) {
            var hoursRemaining = (ban.ExpirationDateTime - DateTime.UtcNow).TotalHours;
            var hoursTotal = (ban.ExpirationDateTime - ban.BanDateTime).TotalHours;
            var percentRemaining = Utils.Clamp(hoursRemaining / hoursTotal, 100, 0);

            var red = Utils.Clamp((int)(255 * 2.0f * percentRemaining), 255, 0);
            var green = Utils.Clamp((int)(255 * (2.0f * (1 - percentRemaining))), 255, 0);
            return $"{red:X2}{green:X2}{0:X2}";
        }
    }

    internal static class BanCommandHelpers
    {
        public static AddBanResult AddBan(
            CommandOutcome.Builder builder,
            CommandExecutor executor,
            string identifier,
            string reason,
            DateTime expiration) {
            var result = TShock.Bans.InsertBan(identifier, reason, executor.Account.Name, DateTime.UtcNow, expiration);
            if (result.Ban is not null) {
                builder.AddSuccess(GetString($"Ban added. Ticket Number {result.Ban.TicketNumber.Color(Utils.GreenHighlight)} was created for identifier {identifier.Color(Utils.WhiteHighlight)}."));
            }
            else {
                builder.AddWarning(GetString($"Failed to add ban for identifier: {identifier.Color(Utils.WhiteHighlight)}."));
                builder.AddWarning(GetString($"Reason: {result.Message}."));
            }

            return result;
        }
    }
}
