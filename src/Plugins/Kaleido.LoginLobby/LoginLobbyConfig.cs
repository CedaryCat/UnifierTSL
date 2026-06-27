namespace Kaleido.LoginLobby
{
    public sealed class LoginLobbyConfig
    {
        public bool Enabled { get; set; } = true;
        public string DisplayName { get; set; } = "Kaleido Gate";
        public int SessionTimeoutSeconds { get; set; } = 120;
        public int PasswordMinLength { get; set; } = 4;
        public int MaxAttempts { get; set; } = 3;
        public bool AllowRegistration { get; set; } = true;
    }
}
