using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Logging;
using Timer = System.Timers.Timer;

namespace TShockAPI.Commanding.V2
{
    [CommandController("setup", Summary = nameof(ControllerSummary))]
    internal static class SetupCommand
    {
        private static string ControllerSummary => GetString("Used to authenticate as superadmin when first setting up TShock.");
        private static string ExecuteSummary => GetString("Finalizes the initial setup flow after you log into your new account.");
        private static string ExecuteWithCodeSummary => GetString("Authenticates you as superadmin during the initial setup flow.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(PlayerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            return ExecuteCore(context, codeRaw: null, codeProvided: false);
        }

        [CommandAction(Summary = nameof(ExecuteWithCodeSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(PlayerScope = true)]
        public static CommandOutcome ExecuteWithCode(
            [FromAmbientContext] TSExecutionContext context,
            [CommandParam(Name = "code")] string codeRaw) {
            return ExecuteCore(context, codeRaw, codeProvided: true);
        }

        private static CommandOutcome ExecuteCore(
            TSExecutionContext context,
            string? codeRaw,
            bool codeProvided) {
            var tsPlayer = context.Player!;
            if (TShock.SetupToken == 0) {
                return CommandOutcome.WarningBuilder(GetString("The initial setup system is disabled. This incident has been logged."))
                    .AddWarning(GetString("If you are locked out of all admin accounts, ask for help on https://tshock.co/"))
                    .AddLog(LogLevel.Warning, GetString(
                        "{0} attempted to use the initial setup system even though it's disabled.",
                        context.Executor.IP))
                    .Build();
            }

            if (tsPlayer.IsLoggedIn && tsPlayer.tempGroup == null) {
                FileTools.CreateFile(Path.Combine(TShock.SavePath, "setup.lock"));
                File.Delete(Path.Combine(TShock.SavePath, "setup-code.txt"));
                TShock.SetupToken = 0;
                return CommandOutcome.SuccessBuilder(GetString(
                        "Your new account has been verified, and the {0}setup system has been turned off.",
                        Commands.Specifier))
                    .AddSuccess(GetString("Share your server, talk with admins, and chill on GitHub & Discord. -- https://tshock.co/"))
                    .AddSuccess(GetString("Thank you for using TShock for Terraria!"))
                    .Build();
            }

            if (!codeProvided) {
                return CommandOutcome.Error(GetString("You must provide a setup code!"));
            }

            if (!int.TryParse(codeRaw, out var givenCode) || givenCode != TShock.SetupToken) {
                return CommandOutcome.ErrorBuilder(GetString("Incorrect setup code. This incident has been logged."))
                    .AddLog(LogLevel.Warning, GetString(
                        "{0} attempted to use an incorrect setup code.",
                        context.Executor.IP))
                    .Build();
            }

            if (tsPlayer.Group.Name != "superadmin") {
                tsPlayer.tempGroup = new SuperAdminGroup();
            }

            return CommandOutcome.InfoBuilder(GetString("Temporary system access has been given to you, so you can run one command."))
                .AddWarning(GetString("Please use the following to create a permanent account for you."))
                .AddWarning(GetString("{0}user add <username> <password> owner", Commands.Specifier))
                .AddInfo(GetString("Creates: <username> with the password <password> as part of the owner group."))
                .AddInfo(GetString("Please use {0}login <username> <password> after this process.", Commands.Specifier))
                .AddWarning(GetString(
                    "If you understand, please {0}login <username> <password> now, and then type {0}setup.",
                    Commands.Specifier))
                .Build();
        }
    }

    [CommandController("su", Summary = nameof(ControllerSummary))]
    internal static class SuCommand
    {
        private static string ControllerSummary => GetString("Temporarily elevates you to Super Admin.");
        private static string ExecuteSummary => GetString("Temporarily elevates you to Super Admin.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.su), PlayerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            var tsPlayer = context.Player!;
            if (tsPlayer.tempGroup != null) {
                tsPlayer.tempGroup = null;
                tsPlayer.tempGroupTimer?.Stop();

                return CommandOutcome.Success(GetString("Your previous permission set has been restored."));
            }

            tsPlayer.tempGroup = new SuperAdminGroup();
            tsPlayer.tempGroupTimer = new Timer(600 * 1000d);
            tsPlayer.tempGroupTimer.Elapsed += tsPlayer.TempGroupTimerElapsed;
            tsPlayer.tempGroupTimer.Start();
            return CommandOutcome.Success(GetString("Your account has been elevated to superadmin for 10 minutes."));
        }

    }

    [CommandController("sudo", Summary = nameof(ControllerSummary))]
    internal static class SudoCommand
    {
        private static string ControllerSummary => GetString("Executes a command as the super admin.");
        private static string ExecuteSummary => GetString("Executes a nested command while temporarily elevated to Super Admin.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.su), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            [CommandRef(Recursive = true, InsertPrefix = true)] string command = "") {
            if (string.IsNullOrWhiteSpace(command)) {
                return BuildUsageOutcome();
            }

            var nestedCommand = command.Trim();
            var tsPlayer = context.Player!;
            tsPlayer.tempGroup = new SuperAdminGroup();
            try {
                var request = TSCommandBridge.CreateDispatchRequest(
                    context.Executor,
                    TShockCommandEndpoints.Player,
                    nestedCommand,
                    TSCommandBridge.IsSilentInvocation(nestedCommand));
                var result = CommandDispatchCoordinator.DispatchAsync(request)
                    .GetAwaiter()
                    .GetResult();
                if (result.Matched) {
                    TSCommandBridge.AuditDispatch(context.Executor, request, result);
                    CommandSystem.GetOutcomeWriter<CommandExecutor>().Write(context.Executor, result.Outcome ?? CommandOutcome.Empty);
                }
                else if (Commands.RequiresLegacyDispatch(context.Executor, result.ExecutionRequest?.InvokedRoot)) {
                    Commands.HandleCommand(context.Executor, nestedCommand);
                }
                else {
                    CommandSystem.GetOutcomeWriter<CommandExecutor>().Write(
                        context.Executor,
                        CommandOutcome.Error(GetString("Invalid command entered. Type {0}help for a list of valid commands.", Commands.Specifier)));
                }
            }
            finally {
                tsPlayer.tempGroup = null;
            }

            return CommandOutcome.Empty;
        }

        private static CommandOutcome BuildUsageOutcome() {
            return CommandOutcome.ErrorBuilder(GetString("Usage: /sudo [command]."))
                .AddError(GetString("Example: /sudo /ban add particles 2d Hacking."))
                .Build();
        }
    }
}
