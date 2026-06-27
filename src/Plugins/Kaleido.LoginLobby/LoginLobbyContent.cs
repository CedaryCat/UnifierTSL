using Kaleido.Hosting.SharedProjection;
using Kaleido.LoginLobby.Identity;
using Kaleido.Model.Transfer;
using Kaleido.Systems;
using Kaleido.Systems.Installation;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Drawing;
using Terraria.Localization;
using TrProtocol;
using TrProtocol.Models;
using TrProtocol.NetPackets;
using TrProtocol.NetPackets.Mobile;
using TrProtocol.NetPackets.Modules;
using UnifierTSL.Network;
using UnifierTSL.Servers;

namespace Kaleido.LoginLobby
{
    internal sealed class LoginLobbyContent : IRealmContentInstaller
    {
        private enum LobbyStage
        {
            Intro,
            AwaitingPassword,
            ConfirmingPassword,
            ReadyToExit,
            Transferring,
            Denied,
            Completed
        }

        private const int ReminderFrames = 60 * 15;
        private readonly LoginLobbyService service;
        private readonly int ownerPlayerId;
        private readonly string playerName;
        private readonly string clientUuid;
        private readonly LoginLobbyDecision decision;
        private readonly ServerContext destination;
        private readonly DateTime expiresAtUtc;
        private SharedProjectionContext context = null!;
        private RealmSystemRealms realms = null!;
        private CancellationTokenSource lifetime = null!;
        private LobbyStage stage;
        private string? pendingPassword;
        private int introStep;
        private int attempts;
        private int reminderFrames;
        private int effectFrames;
        private bool touchClient;
        private Task? transfer;
        private TransferCompletion transferCompletion;

        public LoginLobbyContent(
            LoginLobbyService service,
            int ownerPlayerId,
            string playerName,
            string clientUuid,
            LoginLobbyDecision decision,
            ServerContext destination) {

            this.service = service;
            this.ownerPlayerId = ownerPlayerId;
            this.playerName = playerName;
            this.clientUuid = clientUuid;
            this.decision = decision;
            this.destination = destination;
            expiresAtUtc = DateTime.UtcNow.AddSeconds(service.Config.SessionTimeoutSeconds);
            stage = decision.Kind == LoginLobbyDecisionKind.Deny ? LobbyStage.Denied : LobbyStage.Intro;
        }

        public Task InstallAsync(RealmInstallScope install, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            context = install.Server as SharedProjectionContext
                ?? throw new InvalidOperationException("Login lobby content requires a shared projection host.");
            var projection = install.Projection
                ?? throw new InvalidOperationException("Shared projection hooks are unavailable.");
            realms = install.Realms;
            lifetime = new();

            projection.OnPlayerSync(input => SendRackState(input.Remote));
            projection.OnPlayerEntered(OnPlayerEntered);
            projection.OnModule<NetTextModule>(NetModuleType.NetTextModule, OnChat);
            projection.OnPacket<TileChange>(MessageID.TileChange, OnTileEdit);
            projection.OnPacket<RequestTileEntityInteraction>(MessageID.RequestTileEntityInteraction, OnRequestTileEntityInteraction);
            projection.OnPacket<WeaponsRackTryPlacing>(MessageID.WeaponsRackTryPlacing, OnWeaponsRackTryPlacing);
            projection.OnPacket<PlayerPlatformInfo>(MessageID.PlayerPlatformInfo, OnPlayerPlatformInfo);
            projection.OnPlayerFrame(OnPlayerFrame);
            return Task.CompletedTask;
        }

        public async Task UninstallAsync(RealmInstallScope install) {
            lifetime.Cancel();
            if (transfer is { } pending) {
                await pending.ConfigureAwait(false);
                transfer = null;
            }
            lifetime.Dispose();
        }

        private void OnPlayerEntered(ProjectionPlayerEntered input) {
            SendInfo($"Welcome to {service.Config.DisplayName}.");
            if (stage == LobbyStage.Denied) {
                SendWarning(string.IsNullOrWhiteSpace(decision.Message) ? "Login is not available for this account." : decision.Message);
                Kick("Login denied.");
                return;
            }

            SendDecisionIntro();
            SendCrystalHint();
        }

        private ProjectionInputResult OnChat(ProjectionModule<NetTextModule> input) {
            if (input.Module.TextC2S?.Text is not { } text) {
                return ProjectionInputResult.Handled;
            }
            if (stage is LobbyStage.Completed or LobbyStage.Denied) {
                return ProjectionInputResult.Handled;
            }

            if (stage is not (LobbyStage.AwaitingPassword or LobbyStage.ConfirmingPassword)) {
                SendInfo($"The gate is not asking for a secret yet. {CrystalActionText()} to continue.");
                return ProjectionInputResult.Handled;
            }

            var password = text.Trim();
            if (password.Length < service.Config.PasswordMinLength) {
                SendWarning($"Password must be at least {service.Config.PasswordMinLength} characters.");
                return ProjectionInputResult.Handled;
            }

            if (decision.Kind == LoginLobbyDecisionKind.RequireRegistration) {
                HandleRegistration(password);
            }
            else if (decision.Kind == LoginLobbyDecisionKind.RequirePasswordRebind) {
                HandlePasswordRebind(password);
            }

            return ProjectionInputResult.Handled;
        }

        private ProjectionInputResult OnTileEdit(ProjectionPacket<TileChange> input) {
            if (stage == LobbyStage.Completed) {
                return ProjectionInputResult.Handled;
            }

            if (IsRackClick(input.Packet) && FlatLoginLobbyWorldDataProvider.IsRackArea(input.Packet.Position)) {
                SendRackState(input.Remote);
                InteractRack(input.Remote);
                return ProjectionInputResult.Handled;
            }

            SendInfo($"This private lobby is locked down. {CrystalActionText()}.");
            return ProjectionInputResult.Handled;
        }

        private static bool IsRackClick(TileChange packet) => packet.ChangeType == TileEditAction.KillTile && packet.TileType == 1;

        private ProjectionInputResult OnRequestTileEntityInteraction(ProjectionPacket<RequestTileEntityInteraction> input) {
            if (input.Packet.TileEntityID != FlatLoginLobbyWorldDataProvider.RackEntityId) {
                return ProjectionInputResult.Unhandled;
            }

            SendRackState(input.Remote);
            InteractRack(input.Remote);
            return ProjectionInputResult.Handled;
        }

        private ProjectionInputResult OnWeaponsRackTryPlacing(ProjectionPacket<WeaponsRackTryPlacing> input) {
            if (!FlatLoginLobbyWorldDataProvider.IsRackArea(input.Packet.Position)) {
                return ProjectionInputResult.Unhandled;
            }

            SendRackState(input.Remote);
            InteractRack(input.Remote);
            return ProjectionInputResult.Handled;
        }

        private ProjectionInputResult OnPlayerPlatformInfo(ProjectionPacket<PlayerPlatformInfo> input) {
            touchClient = true;
            return ProjectionInputResult.Handled;
        }

        private void OnPlayerFrame(ProjectionPlayerFrame input) {
            if (input.PlayerId != ownerPlayerId) {
                return;
            }

            if (transfer is { IsCompleted: true }) {
                transfer = null;
                CompleteTransfer(transferCompletion);
                return;
            }

            if (stage is LobbyStage.Completed or LobbyStage.Denied or LobbyStage.Transferring) {
                return;
            }

            if (DateTime.UtcNow >= expiresAtUtc) {
                stage = LobbyStage.Completed;
                Kick("Login timed out.");
                return;
            }

            if (++effectFrames >= 25) {
                effectFrames = 0;
                SendCrystalEffect();
            }

            if (++reminderFrames >= ReminderFrames) {
                reminderFrames = 0;
                SendReminder();
            }
        }

        private void SendDecisionIntro() {
            switch (decision.Kind) {
                case LoginLobbyDecisionKind.RequireRegistration:
                    SendInfo($"No account is bound to '{playerName}'. The lobby can register and bind this client before you enter a world.");
                    break;
                case LoginLobbyDecisionKind.RequirePasswordRebind:
                    SendInfo($"The name '{playerName}' is registered, but this client is not bound to that account.");
                    break;
                default:
                    SendInfo("This session requires additional account handling before it can enter a world.");
                    break;
            }
        }

        private void InteractRack(LocalClientSender remote) {
            reminderFrames = 0;
            if (stage == LobbyStage.ReadyToExit) {
                QueueTransfer();
                return;
            }

            if (stage == LobbyStage.Transferring) {
                SendInfo($"Entering '{destination.Name}'.");
                return;
            }

            if (stage is LobbyStage.AwaitingPassword or LobbyStage.ConfirmingPassword) {
                SendInfo("Use chat for the password step. Lobby chat is intercepted and will not be broadcast.");
                return;
            }

            if (decision.Kind == LoginLobbyDecisionKind.RequireRegistration) {
                AdvanceRegistrationIntro();
            }
            else if (decision.Kind == LoginLobbyDecisionKind.RequirePasswordRebind) {
                AdvanceRebindIntro();
            }
        }

        private void AdvanceRegistrationIntro() {
            introStep++;
            switch (introStep) {
                case 1:
                    SendInfo("This creates a TShock account using your current character name.");
                    break;
                case 2:
                    SendInfo("After confirmation, the account will be logged in and bound to this client UUID.");
                    break;
                default:
                    stage = LobbyStage.AwaitingPassword;
                    SendInfo("Type a new password in chat.");
                    MovePlayerToSpawn();
                    break;
            }
        }

        private void AdvanceRebindIntro() {
            introStep++;
            switch (introStep) {
                case 1:
                    SendInfo("A password check can prove ownership and refresh the UUID binding.");
                    break;
                default:
                    stage = LobbyStage.AwaitingPassword;
                    SendInfo("Type the account password in chat.");
                    MovePlayerToSpawn();
                    break;
            }
        }

        private void HandleRegistration(string password) {
            if (stage == LobbyStage.AwaitingPassword) {
                pendingPassword = password;
                stage = LobbyStage.ConfirmingPassword;
                SendInfo("Type the same password again to confirm registration.");
                return;
            }

            if (pendingPassword != password) {
                pendingPassword = null;
                stage = LobbyStage.AwaitingPassword;
                RegisterFailure("Passwords did not match. Start again with your new password.");
                return;
            }

            var result = service.Register(new LoginLobbySecretRequest(ownerPlayerId, playerName, clientUuid, password));
            if (result.Accepted) {
                MarkReady(string.IsNullOrWhiteSpace(result.Message) ? "Account registered." : result.Message);
            }
            else {
                pendingPassword = null;
                stage = LobbyStage.AwaitingPassword;
                RegisterFailure(result.Message);
            }
        }

        private void HandlePasswordRebind(string password) {
            var result = service.VerifyAndBind(new LoginLobbySecretRequest(ownerPlayerId, playerName, clientUuid, password));
            if (result.Accepted) {
                MarkReady(string.IsNullOrWhiteSpace(result.Message) ? "Account verified." : result.Message);
            }
            else {
                RegisterFailure(string.IsNullOrWhiteSpace(result.Message) ? "Invalid password." : result.Message);
            }
        }

        private void RegisterFailure(string message) {
            attempts++;
            SendWarning(message);
            if (attempts >= service.Config.MaxAttempts) {
                stage = LobbyStage.Completed;
                Kick("Too many failed attempts.");
            }
        }

        private void MarkReady(string message) {
            stage = LobbyStage.ReadyToExit;
            pendingPassword = null;
            attempts = 0;
            SendSuccess(message);
            SendInfo($"{CrystalActionText()} to enter '{destination.Name}'.");
            MovePlayerToSpawn();
        }

        private void QueueTransfer() {
            stage = LobbyStage.Transferring;
            SendSuccess($"Entering '{destination.Name}'.");
            try {
                transfer = ObserveTransferAsync(realms.TransferAsync(
                    RealmTransferRequest.ToServer(ownerPlayerId, destination),
                    lifetime.Token));
            }
            catch (Exception ex) {
                service.LogTransferFailure(ownerPlayerId, destination, ex);
                RestoreReadyToExit("The destination realm is not available right now.");
            }
        }

        private async Task ObserveTransferAsync(Task<RealmTransferResult> operation) {
            try {
                transferCompletion = new(await operation.ConfigureAwait(false), null, false);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) {
                transferCompletion = new(null, null, true);
            }
            catch (Exception ex) {
                transferCompletion = new(null, ex, false);
            }
        }

        private void CompleteTransfer(TransferCompletion completion) {
            if (completion.Canceled) {
                return;
            }
            if (completion.Error is { } error) {
                service.LogTransferFailure(ownerPlayerId, destination, error);
                RestoreReadyToExit("The destination realm is not available right now.");
                return;
            }

            var result = completion.Result!;
            if (result.Succeeded && ReferenceEquals(ServerRuntime.GetCurrentServer(ownerPlayerId), destination)) {
                stage = LobbyStage.Completed;
                return;
            }

            RestoreReadyToExit(result.Succeeded
                ? $"Transfer to '{destination.Name}' did not attach you to the destination."
                : result.Error ?? "Destination realm is not available.");
        }

        private void RestoreReadyToExit(string message) {
            stage = LobbyStage.ReadyToExit;
            transfer = null;
            SendWarning(message);
            SendInfo($"{CrystalActionText()} to try entering '{destination.Name}' again.");
            SendRackState(ServerRuntime.GetSender(ownerPlayerId));
        }

        private void SendCrystalHint() {
            SendInfo($"{CrystalActionText()} to continue.");
        }

        private void SendReminder() {
            if (stage is LobbyStage.AwaitingPassword or LobbyStage.ConfirmingPassword) {
                SendInfo("The lobby is waiting for a password in chat.");
            }
            else if (stage == LobbyStage.ReadyToExit) {
                SendInfo($"{CrystalActionText()} to enter '{destination.Name}'.");
            }
            else {
                SendCrystalHint();
            }
        }

        private string CrystalActionText() => $"{InteractionVerb} the crystal";

        private string InteractionVerb => touchClient ? "Tap" : "Use";

        private void MovePlayerToSpawn() {
            var player = context.Main.player[ownerPlayerId];
            var position = FlatLoginLobbyWorldDataProvider.GetPlayerStandPosition(player);
            player.position = position;
            ServerRuntime.GetSender(ownerPlayerId).SendFixedPacket(new Teleport(0, (byte)ownerPlayerId, position, 0, 0));
        }

        private void SendCrystalEffect() {
            var position = new Vector2(
                (FlatLoginLobbyWorldDataProvider.RackPosition.X + 1) * FlatLoginLobbyWorldDataProvider.TileSize + FlatLoginLobbyWorldDataProvider.TileSize / 2f,
                (FlatLoginLobbyWorldDataProvider.RackPosition.Y + 1) * FlatLoginLobbyWorldDataProvider.TileSize + FlatLoginLobbyWorldDataProvider.TileSize / 2f);
            position += new Vector2(Terraria.Main.rand.Next(-8, 9), Terraria.Main.rand.Next(-8, 9));
            ServerRuntime.GetSender(ownerPlayerId).SendFixedPacket(new NetParticlesModule(
                ParticleOrchestraType.SilverBulletSparkle,
                new ParticleOrchestraSettings {
                    MovementVector = Vector2.Zero,
                    PositionInWorld = position
                }));
        }

        private static void SendRackState(LocalClientSender remote) {
            remote.SendDynamicPacket(FlatLoginLobbyWorldDataProvider.RackPlacedSection);
        }

        private void Kick(string message) {
            ServerRuntime.GetSender(ownerPlayerId).Kick(NetworkText.FromLiteral(message));
        }

        private void SendInfo(string message) {
            SendMessage(message, Color.Yellow);
        }

        private void SendSuccess(string message) {
            SendMessage(message, Color.LimeGreen);
        }

        private void SendWarning(string message) {
            SendMessage(message, Color.OrangeRed);
        }

        private void SendMessage(string message, Color color) {
            ServerRuntime.GetSender(ownerPlayerId).SendDynamicPacket_S(new NetTextModule(null, new TextS2C {
                PlayerSlot = byte.MaxValue,
                Text = NetworkText.FromLiteral(message),
                Color = color
            }, true));
        }

        private readonly record struct TransferCompletion(
            RealmTransferResult? Result,
            Exception? Error,
            bool Canceled);
    }
}
