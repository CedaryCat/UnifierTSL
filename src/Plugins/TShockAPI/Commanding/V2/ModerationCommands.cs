using UnifierTSL;
using UnifierTSL.Commanding;
using UnifierTSL.Servers;

namespace TShockAPI.Commanding.V2
{
    [CommandController("broadcast", Summary = nameof(ControllerSummary))]
    [Aliases("bc", "say")]
    internal static class BroadcastCommand
    {
        private static string ControllerSummary => GetString("Broadcasts a message to everyone on the server.");
        private static string ExecuteSummary => GetString("Broadcasts a message to one server or all running servers.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.broadcast))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [RemainingText] string message = "") {
            if (string.IsNullOrWhiteSpace(message)) {
                return CommandOutcome.Empty;
            }

            if (context.Server is not null) {
                Broadcast(context.Server, message);
                return CommandOutcome.Empty;
            }

            foreach (var server in UnifiedServerCoordinator.Servers.Where(static server => server.IsRunning)) {
                Broadcast(server, message);
            }

            return CommandOutcome.Empty;
        }

        private static void Broadcast(ServerContext server, string message) {
            var rgb = TShock.Config.GetServerSettings(server.Name).BroadcastRGB;
            Utils.Broadcast(
                server,
                GetString("(Server Broadcast) ") + message,
                Convert.ToByte(rgb[0]),
                Convert.ToByte(rgb[1]),
                Convert.ToByte(rgb[2]));
        }
    }

    [CommandController("heal", Summary = nameof(ControllerSummary))]
    internal static class HealCommand
    {
        private static string ControllerSummary => GetString("Heals a player in HP and MP.");
        private static string ExecuteSummary => GetString("Heals a player by their max HP.");
        private static string ExecuteAmountSummary => GetString("Heals a player for a specific amount.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [RequireUserArgumentCount(1)]
        [TShockCommand(nameof(Permissions.heal))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            TSPlayer target) {
            return HealTarget(context, target, target.TPlayer.statLifeMax2);
        }

        [CommandAction(Summary = nameof(ExecuteAmountSummary))]
        [RequireUserArgumentCount(2)]
        [TShockCommand(nameof(Permissions.heal))]
        public static CommandOutcome ExecuteAmount(
            [FromAmbientContext] TSExecutionContext context,
            TSPlayer target,
            [Int32Value(InvalidTokenBehavior = InvalidTokenBehavior.UseDefault)]
            int amount = 0) {
            return HealTarget(context, target, amount);
        }

        private static CommandOutcome HealTarget(
            TSExecutionContext context,
            TSPlayer target,
            int resolvedAmount) {
            if (target.Dead) {
                return CommandOutcome.Error(GetString("You can't heal a dead player!"));
            }

            target.Heal(resolvedAmount);

            var executorPlayer = context.Player;
            if (context.Silent) {
                return target == executorPlayer
                    ? CommandOutcome.Success(GetString("You healed yourself for {0} HP.", resolvedAmount))
                    : CommandOutcome.Success(GetString("You healed {0} for {1} HP.", target.Name, resolvedAmount));
            }

            var serverPlayer = target.GetCurrentServer().GetExtension<TSServerPlayer>();
            if (target == executorPlayer) {
                serverPlayer.BCInfoMessage(target.TPlayer.Male
                    ? GetString("{0} healed himself for {1} HP.", context.Executor.Name, resolvedAmount)
                    : GetString("{0} healed herself for {1} HP.", context.Executor.Name, resolvedAmount));
            }
            else {
                serverPlayer.BCInfoMessage(GetString("{0} healed {1} for {2} HP.", context.Executor.Name, target.Name, resolvedAmount));
            }

            return CommandOutcome.Empty;
        }
    }

    [CommandController("kill", Summary = nameof(ControllerSummary))]
    [Aliases("slay")]
    internal static class KillCommand
    {
        private static string ControllerSummary => GetString("Kills another player.");
        private static string ExecuteSummary => GetString("Kills a player.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.kill))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [TSPlayerRef(ConsumptionMode = TSPlayerConsumptionMode.GreedyPhrase)] TSPlayer target) {
            var executorPlayer = context.Player;
            if (target.Dead) {
                return target == executorPlayer
                    ? CommandOutcome.Error(GetString("You are already dead!"))
                    : CommandOutcome.Error(GetString("{0} is already dead!", target.Name));
            }

            target.KillPlayer();

            var builder = target == executorPlayer
                ? CommandOutcome.SuccessBuilder(GetString("You just killed yourself!"))
                : CommandOutcome.SuccessBuilder(GetString("You just killed {0}!", target.Name));
            if (!context.Silent && target != executorPlayer) {
                builder.AddPlayerError(
                    target,
                    GetString("{0} just killed you!", context.Executor.Name));
            }

            return builder.Build();
        }
    }

    [CommandController("buff", Summary = nameof(ControllerSummary))]
    [TSCommandRoot(HelpText = nameof(RootHelpText))]
    internal static class BuffCommand
    {
        private static string ControllerSummary => GetString("Gives yourself a buff or debuff for an amount of time.");
        private static string RootHelpText => GetString("Gives yourself a buff or debuff for an amount of time. Putting -1 for time will set it to 415 days.");
        private static string ExecuteSummary => GetString("Applies a buff or debuff to yourself.");

        private const int TimeLimit = (int.MaxValue / 60) - 1;

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.buff), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [BuffRef] int buffId,
            [BuffDuration(TimeLimit)]
            int durationSeconds = 60) {
            context.Player!.SetBuff(buffId, durationSeconds * 60);
            return CommandOutcome.Success(GetString(
                "You buffed yourself with {0} ({1}) for {2} seconds.",
                Utils.GetBuffName(buffId),
                Utils.GetBuffDescription(buffId),
                durationSeconds));
        }
    }

    [CommandController("gbuff", Summary = nameof(ControllerSummary))]
    [Aliases("buffplayer")]
    [TSCommandRoot(HelpText = nameof(RootHelpText))]
    internal static class GroupBuffCommand
    {
        private static string ControllerSummary => GetString("Gives another player a buff or debuff for an amount of time.");
        private static string RootHelpText => GetString("Gives another player a buff or debuff for an amount of time. Putting -1 for time will set it to 415 days.");
        private static string ExecuteSummary => GetString("Applies a buff or debuff to a target player.");

        private const int TimeLimit = (int.MaxValue / 60) - 1;

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.buffplayer))]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            TSPlayer target,
            [BuffRef] int buffId,
            [BuffDuration(TimeLimit)]
            int durationSeconds = 60) {
            return ApplyTargetBuff(context, target, buffId, durationSeconds);
        }

        private static CommandOutcome ApplyTargetBuff(
            TSExecutionContext context,
            TSPlayer target,
            int buffId,
            int durationSeconds) {
            target.SetBuff(buffId, durationSeconds * 60);

            var executorPlayer = context.Player;
            var builder = target == executorPlayer
                ? CommandOutcome.SuccessBuilder(GetString(
                    "You buffed yourself with {0} ({1}) for {2} seconds.",
                    Utils.GetBuffName(buffId),
                    Utils.GetBuffDescription(buffId),
                    durationSeconds))
                : CommandOutcome.SuccessBuilder(GetString(
                    "You have buffed {0} with {1} ({2}) for {3} seconds!",
                    target.Name,
                    Utils.GetBuffName(buffId),
                    Utils.GetBuffDescription(buffId),
                    durationSeconds));

            if (!context.Silent && target != executorPlayer) {
                builder.AddPlayerSuccess(
                    target,
                    GetString(
                        "{0} has buffed you with {1} ({2}) for {3} seconds!",
                        context.Executor.Name,
                        Utils.GetBuffName(buffId),
                        Utils.GetBuffDescription(buffId),
                        durationSeconds));
            }

            return builder.Build();
        }
    }

    [CommandController("time", Summary = nameof(ControllerSummary))]
    internal static class TimeCommand
    {
        private static string ControllerSummary => GetString("Sets the world time.");
        private static string QuerySummary => GetString("Shows the current world time.");
        private static string PresetSummary => GetString("Sets the world time to a named preset.");
        private static string CustomSummary => GetString("Sets the world time to a custom hh:mm value.");

        [CommandAction(Summary = nameof(QuerySummary))]
        [TShockCommand(nameof(Permissions.time), ServerScope = true)]
        public static CommandOutcome Query([FromAmbientContext] ServerContext server) {
            var time = server.Main.time / 3600.0;
            time += 4.5;
            if (!server.Main.dayTime) {
                time += 15.0;
            }

            time %= 24.0;
            return CommandOutcome.Info(GetString(
                "The current time is {0}:{1:D2}.",
                (int)Math.Floor(time),
                (int)Math.Floor((time % 1.0) * 60.0)));
        }

        [CommandAction(Summary = nameof(PresetSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.time), ServerScope = true)]
        public static CommandOutcome Preset(
            [FromAmbientContext] TSExecutionContext context,
            [FromAmbientContext] ServerContext server,
            [CommandLiteral("day", "night", "noon", "midnight")] string preset) {
            var serverPlayer = server.GetExtension<TSServerPlayer>();
            switch (preset.ToLowerInvariant()) {
                case "day":
                    serverPlayer.SetTime(true, 0.0);
                    serverPlayer.BCInfoMessage(GetString("{0} set the time to 04:30.", context.Executor.Name));
                    break;
                case "night":
                    serverPlayer.SetTime(false, 0.0);
                    serverPlayer.BCInfoMessage(GetString("{0} set the time to 19:30.", context.Executor.Name));
                    break;
                case "noon":
                    serverPlayer.SetTime(true, 27000.0);
                    serverPlayer.BCInfoMessage(GetString("{0} set the time to 12:00.", context.Executor.Name));
                    break;
                case "midnight":
                    serverPlayer.SetTime(false, 16200.0);
                    serverPlayer.BCInfoMessage(GetString("{0} set the time to 00:00.", context.Executor.Name));
                    break;
                default:
                    return CommandOutcome.Error(GetString("Invalid time string. Proper format: hh:mm, in 24-hour time."));
            }

            return CommandOutcome.Empty;
        }

        [CommandAction(Summary = nameof(CustomSummary))]
        [RequireWorldTimeFormatPreBind]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.time), ServerScope = true)]
        public static CommandOutcome Custom(
            [FromAmbientContext] TSExecutionContext context,
            [FromAmbientContext] ServerContext server,
            [WorldTime(Name = "hh:mm")] TimeOnly timeText) {
            var hours = timeText.Hour;
            var minutes = timeText.Minute;
            var time = hours + (minutes / 60.0m);
            time -= 4.50m;
            if (time < 0.00m) {
                time += 24.00m;
            }

            var serverPlayer = server.GetExtension<TSServerPlayer>();
            if (time >= 15.00m) {
                serverPlayer.SetTime(false, (double)((time - 15.00m) * 3600.0m));
            }
            else {
                serverPlayer.SetTime(true, (double)(time * 3600.0m));
            }

            serverPlayer.BCInfoMessage(GetString("{0} set the time to {1}:{2:D2}.", context.Executor.Name, hours, minutes));
            return CommandOutcome.Empty;
        }
    }

    [CommandController("wind", Summary = nameof(ControllerSummary))]
    internal static class WindCommand
    {
        private static string ControllerSummary => GetString("Changes the wind speed.");
        private static string ExecuteSummary => GetString("Sets the world wind speed in mph.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.wind), ServerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [FromAmbientContext] ServerContext server,
            [WindSpeed]
            float mph) {
            var speed = mph / 50f;
            server.Main.windSpeedCurrent = speed;
            server.Main.windSpeedTarget = speed;
            server.NetMessage.SendData((int)PacketTypes.WorldInfo, -1, -1);
            server.GetExtension<TSServerPlayer>().BCInfoMessage(GetString("{0} changed the wind speed to {1}mph.", context.Executor.Name, mph));
            return CommandOutcome.Empty;
        }
    }
}
