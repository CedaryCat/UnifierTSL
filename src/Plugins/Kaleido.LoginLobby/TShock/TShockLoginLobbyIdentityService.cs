using Kaleido.LoginLobby.Identity;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace Kaleido.LoginLobby
{
    internal sealed class TShockLoginLobbyIdentityService : ILoginLobbyIdentityService
    {
        public LoginLobbyDecision Evaluate(LoginLobbyIdentityRequest request) {
            var tsPlayer = TryGetPlayer(request.PlayerId);
            if (tsPlayer is null) {
                return new(LoginLobbyDecisionKind.Deny, "Player session is not available.");
            }

            if (tsPlayer.IsLoggedIn) {
                return new(LoginLobbyDecisionKind.PassThrough);
            }

            var account = TShockAPI.TShock.UserAccounts.GetUserAccountByName(request.PlayerName);
            if (account is null) {
                return Plugin.Service.Config.AllowRegistration
                    ? new(LoginLobbyDecisionKind.RequireRegistration)
                    : new(LoginLobbyDecisionKind.Deny, "Registration is disabled.");
            }

            return !TShockAPI.TShock.Config.GlobalSettings.DisableUUIDLogin
                && account.UUID == request.ClientUuid
                && !string.IsNullOrWhiteSpace(request.ClientUuid)
                    ? new(LoginLobbyDecisionKind.PassThrough)
                    : new(LoginLobbyDecisionKind.RequirePasswordRebind);
        }

        public LoginLobbySecretResult Register(LoginLobbySecretRequest request) {
            if (!Plugin.Service.Config.AllowRegistration) {
                return new(false, "Registration is disabled.");
            }

            var tsPlayer = TryGetPlayer(request.PlayerId);
            if (tsPlayer is null) {
                return new(false, "Player session is not available.");
            }

            if (TShockAPI.TShock.UserAccounts.GetUserAccountByName(request.PlayerName) is not null
                || request.PlayerName == TSServerPlayer.AccountName) {
                return new(false, "That account name is already taken.");
            }

            try {
                var account = new UserAccount {
                    Name = request.PlayerName,
                    Group = TShockAPI.TShock.Config.GlobalSettings.DefaultRegistrationGroupName,
                    UUID = request.ClientUuid
                };
                account.CreateBCryptHash(request.Password);
                TShockAPI.TShock.UserAccounts.AddUserAccount(account);

                var storedAccount = TShockAPI.TShock.UserAccounts.GetUserAccountByName(request.PlayerName) ?? account;
                return CompleteLogin(tsPlayer, storedAccount, "Account registered and authenticated.");
            }
            catch (ArgumentOutOfRangeException) {
                return new(false, $"Password must be at least {TShockAPI.TShock.Config.GlobalSettings.MinimumPasswordLength} characters.");
            }
            catch (UserAccountManagerException ex) {
                return new(false, ex.Message);
            }
            catch (Exception ex) {
                TShockAPI.TShock.Log.Error(ex.ToString());
                return new(false, "Registration failed.");
            }
        }

        public LoginLobbySecretResult VerifyAndBind(LoginLobbySecretRequest request) {
            var tsPlayer = TryGetPlayer(request.PlayerId);
            if (tsPlayer is null) {
                return new(false, "Player session is not available.");
            }

            var account = TShockAPI.TShock.UserAccounts.GetUserAccountByName(request.PlayerName);
            if (account is null) {
                return new(false, "A user account by that name does not exist.");
            }

            if (PlayerHooks.OnPlayerPreLogin(tsPlayer, request.PlayerName, request.Password)) {
                return new(false, "Login was cancelled.");
            }

            if (!account.VerifyPassword(request.Password)) {
                tsPlayer.LoginAttempts++;
                return new(false, "Invalid password.");
            }

            try {
                TShockAPI.TShock.UserAccounts.SetUserAccountUUID(account, request.ClientUuid);
                account.UUID = request.ClientUuid;
                return CompleteLogin(tsPlayer, account, "Account verified and bound to this client.");
            }
            catch (UserAccountManagerException ex) {
                return new(false, ex.Message);
            }
            catch (Exception ex) {
                TShockAPI.TShock.Log.Error(ex.ToString());
                return new(false, "Login failed.");
            }
        }

        private static LoginLobbySecretResult CompleteLogin(TSPlayer tsPlayer, UserAccount account, string message) {
            var group = TShockAPI.TShock.Groups.GetGroupByName(account.Group);
            if (group is null || !TShockAPI.TShock.Groups.AssertGroupValid(tsPlayer, group, false)) {
                return new(false, "Account group could not be loaded.");
            }

            var server = tsPlayer.GetCurrentServer();
            try {
                tsPlayer.PlayerData = TShockAPI.TShock.CharacterDB.GetPlayerData(tsPlayer, account.ID);
                if (server.Main.ServerSideCharacter && TShockAPI.TShock.CharacterDB.IsSeededAppearanceMissing(tsPlayer.PlayerData)) {
                    TShockAPI.TShock.CharacterDB.SyncSeededAppearance(account, tsPlayer);
                    tsPlayer.PlayerData = TShockAPI.TShock.CharacterDB.GetPlayerData(tsPlayer, account.ID);
                }

                tsPlayer.Group = group;
                tsPlayer.tempGroup = null;
                tsPlayer.Account = account;
                tsPlayer.IsLoggedIn = true;
                tsPlayer.IsDisabledForSSC = false;

                if (server.Main.ServerSideCharacter) {
                    if (tsPlayer.HasPermission(Permissions.bypassssc)) {
                        tsPlayer.PlayerData.CopyCharacter(tsPlayer);
                        TShockAPI.TShock.CharacterDB.InsertPlayerData(tsPlayer);
                    }

                    tsPlayer.PlayerData.RestoreCharacter(tsPlayer);
                }

                tsPlayer.LoginFailsBySsi = false;
                if (tsPlayer.HasPermission(Permissions.ignorestackhackdetection)) {
                    tsPlayer.IsDisabledForStackDetection = false;
                }

                if (tsPlayer.HasPermission(Permissions.usebanneditem)) {
                    tsPlayer.IsDisabledForBannedWearable = false;
                }

                PlayerHooks.OnPlayerPostLogin(tsPlayer);
                return new(true, message);
            }
            catch (Exception ex) {
                TShockAPI.TShock.Log.Error(ex.ToString());
                return new(false, "Authentication state could not be applied.");
            }
        }

        private static TSPlayer? TryGetPlayer(int playerId) {
            return (uint)playerId < (uint)TShockAPI.TShock.Players.Length
                ? TShockAPI.TShock.Players[playerId]
                : null;
        }
    }
}
