using System.Collections.Immutable;
using Kaleido.Hosting.SharedProjection;
using Kaleido.Model.Hosting;
using Kaleido.Model.Ids;
using Kaleido.Model.Lifecycle;
using Kaleido.Model.Planning;
using Kaleido.LoginLobby.Identity;
using Kaleido.Systems;
using Terraria;
using UnifierTSL.Logging;
using UnifierTSL.Servers;

namespace Kaleido.LoginLobby
{
    public sealed class LoginLobbyService : IRealmSystem, IDisposable
    {
        private const string SystemId = "Kaleido.LoginLobby";
        private readonly RoleLogger logger;
        private LoginLobbyConfig config = new();
        private ILoginLobbyIdentityService? identityService;
        private bool disposed;

        public LoginLobbyService(RoleLogger logger) {
            this.logger = logger;
        }

        public string Id => SystemId;
        public LoginLobbyConfig Config => config;
        public ILoginLobbyIdentityService? IdentityService => identityService;
        public Func<LoginLobbyDestinationContext, ServerContext?> DestinationSelector { get; set; }
            = static context => context.CandidateServers.Length == 0 ? null : context.CandidateServers[0];

        private bool IsActive => !disposed && config.Enabled && identityService is not null;

        public Task MountAsync(RealmSystemScope scope, CancellationToken cancellationToken) {
            scope.Join.Use(TryJoin, priority: 100);
            return Task.CompletedTask;
        }

        public void UpdateConfig(LoginLobbyConfig nextConfig) {
            config = Normalize(nextConfig);
            logger.Info(config.Enabled
                ? "Kaleido login lobby join routing enabled."
                : "Kaleido login lobby join routing disabled by config.");
        }

        public void SetIdentityService(ILoginLobbyIdentityService service) {
            identityService = service;
            logger.Info("Kaleido login lobby identity adapter registered.");
        }

        public RealmJoinDecision? TryJoin(RealmJoin join) {
            if (!IsActive || identityService is null || join.CandidateServers.Length == 0) {
                return null;
            }

            var clientUuid = join.Client.ClientUUID ?? "";
            LoginLobbyDecision decision;
            try {
                decision = identityService.Evaluate(new(join.PlayerId, join.Player.name, clientUuid));
            }
            catch (Exception ex) {
                LogError($"Login lobby identity evaluation failed for player #{join.PlayerId} ('{join.Player.name}').", ex);
                decision = new(LoginLobbyDecisionKind.Deny, "Login service is unavailable. Please try again later.");
            }

            if (decision.Kind == LoginLobbyDecisionKind.PassThrough) {
                return null;
            }

            ServerContext? target;
            try {
                target = DestinationSelector(new(join.Player, join.Client, join.CandidateServers));
            }
            catch (Exception ex) {
                LogError($"Login lobby destination selection failed for player #{join.PlayerId} ('{join.Player.name}').", ex);
                target = join.CandidateServers[0];
                if (decision.Kind != LoginLobbyDecisionKind.Deny) {
                    decision = new(LoginLobbyDecisionKind.Deny, "Login destination is unavailable. Please try again later.");
                }
            }

            if (target is null) {
                LogWarning($"Login lobby destination selection returned no target for player #{join.PlayerId} ('{join.Player.name}').");
                target = join.CandidateServers[0];
                if (decision.Kind != LoginLobbyDecisionKind.Deny) {
                    decision = new(LoginLobbyDecisionKind.Deny, "Login destination is unavailable. Please try again later.");
                }
            }

            var state = new LobbyNeedState(join.Player.name, clientUuid, decision, target);
            var plan = RealmPlan.Create(
                new RealmKey($"login-lobby:{join.PlayerId}:{Guid.NewGuid():N}"),
                config.DisplayName,
                RealmHostRequirement.SharedProjection,
                RealmLifecyclePolicy.UnloadWhenEmpty(),
                creation => new SharedProjectionContext(config.DisplayName, FlatLoginLobbyWorldDataProvider.Instance),
                content: [new LoginLobbyContent(this, join.PlayerId, state.PlayerName, state.ClientUuid, state.Decision, state.Destination)],
                tags: ["login-lobby", "transient"]);
            return RealmJoinDecision.ToRealm(plan);
        }

        internal LoginLobbySecretResult Register(LoginLobbySecretRequest request) {
            return identityService?.Register(request)
                ?? new(false, "No identity adapter is registered.");
        }

        internal LoginLobbySecretResult VerifyAndBind(LoginLobbySecretRequest request) {
            return identityService?.VerifyAndBind(request)
                ?? new(false, "No identity adapter is registered.");
        }

        internal void LogTransferFailure(int playerId, ServerContext destination, Exception ex) {
            LogError($"Login lobby transfer for player #{playerId} to '{destination.Name}' failed.", ex);
        }

        private void LogError(string message, Exception ex) {
            try {
                logger.Error(category: "LoginLobby", message: message, ex: ex);
            }
            catch {
            }
        }

        private void LogWarning(string message) {
            try {
                logger.Warning(category: "LoginLobby", message: message);
            }
            catch {
            }
        }

        private static LoginLobbyConfig Normalize(LoginLobbyConfig source) {
            return new() {
                Enabled = source.Enabled,
                DisplayName = string.IsNullOrWhiteSpace(source.DisplayName) ? "Kaleido Gate" : source.DisplayName.Trim(),
                SessionTimeoutSeconds = Math.Max(10, source.SessionTimeoutSeconds),
                PasswordMinLength = Math.Max(1, source.PasswordMinLength),
                MaxAttempts = Math.Max(1, source.MaxAttempts),
                AllowRegistration = source.AllowRegistration
            };
        }

        public void Dispose() {
            disposed = true;
        }

        private sealed record LobbyNeedState(
            string PlayerName,
            string ClientUuid,
            LoginLobbyDecision Decision,
            ServerContext Destination);
    }
}
