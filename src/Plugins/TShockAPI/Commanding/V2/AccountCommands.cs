using Microsoft.Xna.Framework;
using TShockAPI.DB;
using TShockAPI.Hooks;
using UnifierTSL.Commanding;
using UnifierTSL.Logging;

namespace TShockAPI.Commanding.V2
{
    [CommandController("login", Summary = nameof(ControllerSummary))]
    [TSCommandRoot(DoLog = false)]
    internal static class LoginCommand
    {
        private static string ControllerSummary => GetString("Logs you into an account.");
        private static string ExecuteUuidSummary => GetString("{0}login - Authenticates you using your UUID and character name.", Commands.Specifier);
        private static string ExecutePasswordSummary => GetString("{0}login <password> - Authenticates you using your password and character name.", Commands.Specifier);
        private static string ExecuteNamedSummary => GetString("{0}login <username> <password> - Authenticates you using your username and password.", Commands.Specifier);

        [CommandAction(Summary = nameof(ExecuteUuidSummary))]
        [LegacyLoginStateGuard]
        [TShockCommand(nameof(Permissions.canlogin), PlayerScope = true)]
        public static CommandOutcome ExecuteUuid([FromAmbientContext] TSExecutionContext context) {
            if (TShock.Config.GlobalSettings.DisableUUIDLogin) {
                return BuildUsage();
            }

            var tsPlayer = context.Player!;
            if (PlayerHooks.OnPlayerPreLogin(tsPlayer, context.Executor.Name, string.Empty)) {
                return CommandOutcome.Empty;
            }

            var account = TShock.UserAccounts.GetUserAccountByName(context.Executor.Name);
            return Authenticate(context, tsPlayer, account, password: string.Empty, usingUuid: true);
        }

        [CommandAction(Summary = nameof(ExecutePasswordSummary))]
        [LegacyLoginStateGuard]
        [TShockCommand(nameof(Permissions.canlogin), PlayerScope = true)]
        public static CommandOutcome ExecutePassword(
            [FromAmbientContext] TSExecutionContext context,
            string password) {
            var tsPlayer = context.Player!;
            if (PlayerHooks.OnPlayerPreLogin(tsPlayer, context.Executor.Name, password)) {
                return CommandOutcome.Empty;
            }

            var account = TShock.UserAccounts.GetUserAccountByName(context.Executor.Name);
            return Authenticate(context, tsPlayer, account, password, usingUuid: false);
        }

        [CommandAction(Summary = nameof(ExecuteNamedSummary))]
        [LegacyLoginStateGuard]
        [TShockCommand(nameof(Permissions.canlogin), PlayerScope = true)]
        public static CommandOutcome ExecuteNamed(
            [FromAmbientContext] TSExecutionContext context,
            string username,
            string password) {
            if (!TShock.Config.GlobalSettings.AllowLoginAnyUsername) {
                return BuildUsage();
            }

            var tsPlayer = context.Player!;
            if (string.IsNullOrEmpty(username)) {
                return CommandOutcome.Error(GetString("Bad login attempt."));
            }

            if (PlayerHooks.OnPlayerPreLogin(tsPlayer, username, password)) {
                return CommandOutcome.Empty;
            }

            var account = TShock.UserAccounts.GetUserAccountByName(username);
            return Authenticate(context, tsPlayer, account, password, usingUuid: false);
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() => BuildUsage();

        [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
        private sealed class LegacyLoginStateGuardAttribute : PreBindGuardAttribute
        {
            public override CommandGuardResult Evaluate(CommandInvocationContext context) {
                if (context.ExecutionContext is not TSExecutionContext tsContext || tsContext.Player is null) {
                    return CommandGuardResult.Continue();
                }

                var failure = ValidateLoginState(tsContext, tsContext.Player);
                return failure is null
                    ? CommandGuardResult.Continue()
                    : CommandGuardResult.Fail(failure);
            }
        }

        private static CommandOutcome? ValidateLoginState(TSExecutionContext context, TSPlayer tsPlayer) {
            var server = context.Server!;
            if (tsPlayer.LoginAttempts > TShock.Config.GlobalSettings.MaximumLoginAttempts
                && TShock.Config.GlobalSettings.MaximumLoginAttempts != -1) {
                context.Executor.LogWarning(GetString(
                    "{0} ({1}) had {2} or more invalid login attempts and was kicked automatically.",
                    context.Executor.IP,
                    context.Executor.Name,
                    TShock.Config.GlobalSettings.MaximumLoginAttempts));
                tsPlayer.Kick(GetString("Too many invalid login attempts."));
                return CommandOutcome.Empty;
            }

            if (tsPlayer.IsLoggedIn) {
                return CommandOutcome.Error(GetString("You are already logged in, and cannot login again."));
            }

            if (tsPlayer.TPlayer.dead) {
                return CommandOutcome.Error(GetString("You cannot login whilst dead."));
            }

            if (tsPlayer.TPlayer.itemTime > 0 || tsPlayer.TPlayer.itemAnimation > 0) {
                return CommandOutcome.Error(GetString("You cannot login whilst using an item."));
            }

            if (tsPlayer.TPlayer.CCed && server.Main.ServerSideCharacter) {
                return CommandOutcome.Error(GetString("You cannot login whilst crowd controlled."));
            }

            return null;
        }

        private static CommandOutcome Authenticate(
            TSExecutionContext context,
            TSPlayer tsPlayer,
            UserAccount? account,
            string password,
            bool usingUuid) {
            var server = context.Server!;
            try {
                if (account is null) {
                    return CommandOutcome.Error(GetString("A user account by that name does not exist."));
                }

                var authenticated = account.VerifyPassword(password)
                    || (usingUuid
                        && account.UUID == tsPlayer.UUID
                        && !TShock.Config.GlobalSettings.DisableUUIDLogin
                        && !string.IsNullOrWhiteSpace(tsPlayer.UUID));
                if (!authenticated) {
                    tsPlayer.LoginAttempts++;
                    return (usingUuid && !TShock.Config.GlobalSettings.DisableUUIDLogin)
                        ? CommandOutcome.ErrorBuilder(GetString("UUID does not match this character."))
                            .AddLog(LogLevel.Warning, GetString("{0} failed to authenticate as user: {1}.", context.Executor.IP, account.Name))
                            .Build()
                        : CommandOutcome.ErrorBuilder(GetString("Invalid password."))
                            .AddLog(LogLevel.Warning, GetString("{0} failed to authenticate as user: {1}.", context.Executor.IP, account.Name))
                            .Build();
                }

                var group = TShock.Groups.GetGroupByName(account.Group);
                if (!TShock.Groups.AssertGroupValid(tsPlayer, group, false)) {
                    return CommandOutcome.Error(GetString("Login attempt failed - see the message above."));
                }

                var validatedGroup = group!;
                tsPlayer.PlayerData = TShock.CharacterDB.GetPlayerData(tsPlayer, account.ID);
                if (server.Main.ServerSideCharacter && TShock.CharacterDB.IsSeededAppearanceMissing(tsPlayer.PlayerData)) {
                    TShock.CharacterDB.SyncSeededAppearance(account, tsPlayer);
                    tsPlayer.PlayerData = TShock.CharacterDB.GetPlayerData(tsPlayer, account.ID);
                }

                tsPlayer.Group = validatedGroup;
                tsPlayer.tempGroup = null;
                tsPlayer.Account = account;
                tsPlayer.IsLoggedIn = true;
                tsPlayer.IsDisabledForSSC = false;

                if (server.Main.ServerSideCharacter) {
                    if (context.Executor.HasPermission(Permissions.bypassssc)) {
                        tsPlayer.PlayerData.CopyCharacter(tsPlayer);
                        TShock.CharacterDB.InsertPlayerData(tsPlayer);
                    }

                    tsPlayer.PlayerData.RestoreCharacter(tsPlayer);
                }

                tsPlayer.LoginFailsBySsi = false;
                if (context.Executor.HasPermission(Permissions.ignorestackhackdetection)) {
                    tsPlayer.IsDisabledForStackDetection = false;
                }

                if (context.Executor.HasPermission(Permissions.usebanneditem)) {
                    tsPlayer.IsDisabledForBannedWearable = false;
                }

                if (tsPlayer.LoginHarassed && TShock.Config.GlobalSettings.RememberLeavePos) {
                    var worldId = server.Main.worldID.ToString();
                    var pos = TShock.RememberedPos.GetLeavePos(worldId, context.Executor.Name, context.Executor.IP);
                    if (pos != Vector2.Zero) {
                        tsPlayer.Teleport((int)pos.X * 16, (int)pos.Y * 16);
                    }

                    tsPlayer.LoginHarassed = false;
                }

                TShock.UserAccounts.SetUserAccountUUID(account, tsPlayer.UUID);
                PlayerHooks.OnPlayerPostLogin(tsPlayer);
                return CommandOutcome.SuccessBuilder(GetString("Authenticated as {0} successfully.", account.Name))
                    .AddLog(LogLevel.Info, GetString("{0} authenticated successfully as user: {1}.", context.Executor.Name, account.Name))
                    .Build();
            }
            catch (Exception ex) {
                return CommandOutcome.ErrorBuilder(GetString("There was an error processing your login or authentication related request."))
                    .AddLog(LogLevel.Error, ex.ToString())
                    .Build();
            }
        }

        private static CommandOutcome BuildUsage() {
            var builder = CommandOutcome.CreateBuilder(succeeded: false);
            if (!TShock.Config.GlobalSettings.DisableUUIDLogin) {
                builder.AddInfo(GetString("{0}login - Authenticates you using your UUID and character name.", Commands.Specifier));
            }

            if (TShock.Config.GlobalSettings.AllowLoginAnyUsername) {
                builder.AddInfo(GetString("{0}login <username> <password> - Authenticates you using your username and password.", Commands.Specifier));
            }
            else {
                builder.AddInfo(GetString("{0}login <password> - Authenticates you using your password and character name.", Commands.Specifier));
            }

            builder.AddWarning(GetString("If you forgot your password, contact the administrator for help."));
            return builder.Build();
        }
    }

    [CommandController("logout", Summary = nameof(ControllerSummary))]
    [TSCommandRoot(DoLog = false)]
    internal static class LogoutCommand
    {
        private static string ControllerSummary => GetString("Logs you out of your current account.");
        private static string ExecuteSummary => GetString("Logs you out of the account bound to your player session.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [IgnoreTrailingArguments]
        [TShockCommand(nameof(Permissions.canlogout), PlayerScope = true)]
        public static CommandOutcome Execute([FromAmbientContext] TSExecutionContext context) {
            var tsPlayer = context.Player!;

            if (!tsPlayer.IsLoggedIn) {
                return CommandOutcome.Error(GetString("You are not logged-in. Therefore, you cannot logout."));
            }

            if (tsPlayer.TPlayer.talkNPC != -1) {
                return CommandOutcome.Error(GetString("Please close NPC windows before logging out."));
            }

            tsPlayer.Logout();
            var builder = CommandOutcome.SuccessBuilder(GetString("You have been successfully logged out of your account."));
            if (context.Server!.Main.ServerSideCharacter) {
                builder.AddWarning(GetString("Server side characters are enabled. You need to be logged-in to play."));
            }

            return builder.Build();
        }
    }

    [CommandController("password", Summary = nameof(ControllerSummary))]
    [TSCommandRoot(DoLog = false)]
    internal static class PasswordCommand
    {
        private static string ControllerSummary => GetString("Changes your account's password.");
        private static string ExecuteSummary => GetString("Changes your account's password.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.canchangepassword), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            string oldPassword,
            string newPassword) {
            var tsPlayer = context.Player!;
            var account = tsPlayer.Account;
            if (!tsPlayer.IsLoggedIn || account is null) {
                return BuildUsage();
            }

            if (!account.VerifyPassword(oldPassword)) {
                return CommandOutcome.ErrorBuilder(GetString("You failed to change your password."))
                    .AddLog(LogLevel.Info, GetString("{0} ({1}) failed to change the password for account {2}.", context.Executor.IP, context.Executor.Name, account.Name))
                    .Build();
            }

            try {
                TShock.UserAccounts.SetUserAccountPassword(account, newPassword);
                return CommandOutcome.SuccessBuilder(GetString("You have successfully changed your password."))
                    .AddLog(LogLevel.Info, GetString("{0} ({1}) changed the password for account {2}.", context.Executor.IP, context.Executor.Name, account.Name))
                    .Build();
            }
            catch (ArgumentOutOfRangeException) {
                return CommandOutcome.Error(GetString("Password must be greater than or equal to {0} characters.", TShock.Config.GlobalSettings.MinimumPasswordLength));
            }
            catch (UserAccountManagerException ex) {
                return CommandOutcome.ErrorBuilder(GetString("Sorry, an error occurred: {0}.", ex.Message))
                    .AddLog(LogLevel.Error, GetString("PasswordUser returned an error: {0}.", ex))
                    .Build();
            }
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() => BuildUsage();

        private static CommandOutcome BuildUsage() {
            return CommandOutcome.Error(GetString("Not logged in or Invalid syntax. Proper syntax: {0}password <oldpassword> <newpassword>.", Commands.Specifier));
        }
    }

    [CommandController("register", Summary = nameof(ControllerSummary))]
    [TSCommandRoot(DoLog = false)]
    internal static class RegisterCommand
    {
        private static string ControllerSummary => GetString("Registers you an account.");
        private static string ExecuteSummary => GetString("Registers a new account using your current player name.");
        private static string ExecuteNamedSummary => GetString("Registers a new account using an explicit username.");

        [CommandAction(Summary = nameof(ExecuteSummary))]
        [TShockCommand(nameof(Permissions.canregister), PlayerScope = true)]
        public static CommandOutcome Execute(
            [FromAmbientContext] TSExecutionContext context,
            string password) {
            return RegisterCore(context, context.Executor.Name, password);
        }

        [CommandAction(Summary = nameof(ExecuteNamedSummary))]
        [TShockCommand(nameof(Permissions.canregister), PlayerScope = true)]
        public static CommandOutcome ExecuteNamed(
            [FromAmbientContext] TSExecutionContext context,
            string username,
            string password) {
            if (!TShock.Config.GlobalSettings.AllowRegisterAnyUsername) {
                return BuildUsage();
            }

            return RegisterCore(context, username, password);
        }

        [MismatchHandler]
        public static CommandOutcome HandleMismatch() => BuildUsage();

        private static CommandOutcome RegisterCore(
            TSExecutionContext context,
            string accountName,
            string password) {
            var tsPlayer = context.Player!;
            UserAccount account = new();
            if (string.IsNullOrWhiteSpace(accountName)) {
                return BuildUsage();
            }

            try {
                account.Name = accountName;
                account.CreateBCryptHash(password);
            }
            catch (ArgumentOutOfRangeException) {
                return CommandOutcome.Error(GetString("Password must be greater than or equal to {0} characters.", TShock.Config.GlobalSettings.MinimumPasswordLength));
            }

            account.Group = TShock.Config.GlobalSettings.DefaultRegistrationGroupName;
            account.UUID = tsPlayer.UUID;

            if (TShock.UserAccounts.GetUserAccountByName(account.Name) is not null || account.Name == TSServerPlayer.AccountName) {
                return CommandOutcome.ErrorBuilder(GetString("Sorry, {0} was already taken by another person.", account.Name))
                    .AddError(GetString("Please try a different username."))
                    .AddLog(LogLevel.Info, GetString("{0} attempted to register for the account {1} but it was already taken.", context.Executor.Name, account.Name))
                    .Build();
            }

            try {
                TShock.UserAccounts.AddUserAccount(account);
                var builder = CommandOutcome.SuccessBuilder(GetString("Your account, \"{0}\", has been registered.", account.Name));
                builder.AddSuccess(GetString("Your password is {0}.", password));
                if (!TShock.Config.GlobalSettings.DisableUUIDLogin) {
                    builder.AddInfo(GetString("Type {0}login to log-in to your account using your UUID.", Commands.Specifier));
                }

                if (TShock.Config.GlobalSettings.AllowLoginAnyUsername) {
                    builder.AddInfo(GetString($"Type {Commands.Specifier}login \"{account.Name.Color(Utils.GreenHighlight)}\" {password.Color(Utils.BoldHighlight)} to log-in to your account."));
                }
                else {
                    builder.AddInfo(GetString($"Type {Commands.Specifier}login {password.Color(Utils.BoldHighlight)} to log-in to your account."));
                }

                builder.AddLog(LogLevel.Info, GetString("{0} registered an account: \"{1}\".", context.Executor.Name, account.Name));
                return builder.Build();
            }
            catch (UserAccountManagerException ex) {
                return CommandOutcome.ErrorBuilder(GetString("Sorry, an error occurred: {0}.", ex.Message))
                    .AddLog(LogLevel.Error, GetString("RegisterUser returned an error: {0}.", ex))
                    .Build();
            }
        }

        private static CommandOutcome BuildUsage() {
            var builder = CommandOutcome.ErrorBuilder(GetString("Invalid syntax. Proper syntax: {0}register <password>.", Commands.Specifier));
            if (TShock.Config.GlobalSettings.AllowRegisterAnyUsername) {
                builder.AddInfo(GetString("If username differs from player name, use {0}register <username> <password> instead.", Commands.Specifier));
            }

            return builder.Build();
        }
    }
}
