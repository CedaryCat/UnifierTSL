namespace Kaleido.LoginLobby.Identity
{
    public interface ILoginLobbyIdentityService
    {
        LoginLobbyDecision Evaluate(LoginLobbyIdentityRequest request);
        LoginLobbySecretResult Register(LoginLobbySecretRequest request);
        LoginLobbySecretResult VerifyAndBind(LoginLobbySecretRequest request);
    }
}
