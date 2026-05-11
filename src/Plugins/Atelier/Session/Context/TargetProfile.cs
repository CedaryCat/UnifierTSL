using UnifierTSL.Servers;

namespace Atelier.Session.Context
{
    internal abstract record TargetProfile
    {
        public abstract string Label { get; }
    }

    internal sealed record LauncherProfile : TargetProfile
    {
        public static LauncherProfile Instance { get; } = new();

        public override string Label => "launcher";

        private LauncherProfile() { }
    }

    internal sealed record ServerProfile(ServerContext server) : TargetProfile
    {
        public ServerContext Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

        public override string Label => $"server:{Server.Name}";
    }
}
