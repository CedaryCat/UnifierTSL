using UnifierTSL.Servers;

namespace Atelier.Session.Context
{
    internal sealed record OpenOptions(InvocationHost invocationHost, TargetProfile targetProfile)
    {
        public InvocationHost InvocationHost { get; } = invocationHost ?? throw new ArgumentNullException(nameof(invocationHost));

        public TargetProfile TargetProfile { get; } = targetProfile ?? throw new ArgumentNullException(nameof(targetProfile));
    }

    internal abstract record InvocationHost
    {
        public abstract string Label { get; }
    }

    internal sealed record LauncherInvocationHost : InvocationHost
    {
        public static LauncherInvocationHost Instance { get; } = new();

        public override string Label => "launcher";

        private LauncherInvocationHost() { }
    }

    internal sealed record ServerInvocationHost(ServerContext hostServer) : InvocationHost
    {
        public ServerContext HostServer { get; } = hostServer ?? throw new ArgumentNullException(nameof(hostServer));

        public override string Label => $"server:{HostServer.Name}";
    }
}
