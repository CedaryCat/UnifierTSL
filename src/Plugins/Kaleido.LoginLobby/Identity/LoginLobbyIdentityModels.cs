namespace Kaleido.LoginLobby.Identity
{
    public enum LoginLobbyDecisionKind
    {
        PassThrough,
        RequireRegistration,
        RequirePasswordRebind,
        Deny
    }

    public readonly record struct LoginLobbyIdentityRequest(
        int PlayerId,
        string PlayerName,
        string ClientUuid);

    public readonly record struct LoginLobbySecretRequest(
        int PlayerId,
        string PlayerName,
        string ClientUuid,
        string Password);

    public readonly record struct LoginLobbyDecision(
        LoginLobbyDecisionKind Kind,
        string Message = "");

    public readonly record struct LoginLobbySecretResult(
        bool Accepted,
        string Message = "");
}
