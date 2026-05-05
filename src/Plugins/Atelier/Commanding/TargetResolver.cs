using Atelier.Session.Context;
using UnifierTSL;
using UnifierTSL.Commanding;
using UnifierTSL.Servers;

namespace Atelier.Commanding
{
    internal static class TargetResolver
    {
        public static bool TryResolve(
            CommandInvocationContext context,
            IReadOnlyList<string> args,
            out OpenOptions? options,
            out CommandOutcome? failure) {

            options = null;
            failure = null;

            var targetLauncher = false;
            string? targetServerName = null;

            for (var i = 0; i < args.Count; i++) {
                var token = args[i];
                switch (token) {
                    case "--launcher":
                        if (targetLauncher) {
                            failure = CommandOutcome.Error(GetString("Duplicate --launcher flag."));
                            return false;
                        }

                        targetLauncher = true;
                        break;

                    case "--server":
                        if (targetServerName is not null) {
                            failure = CommandOutcome.Error(GetString("Duplicate --server flag."));
                            return false;
                        }

                        if (!TryReadValue(args, ref i, out targetServerName)) {
                            failure = CommandOutcome.Error(GetString("Missing server name after --server."));
                            return false;
                        }
                        break;

                    default:
                        failure = CommandOutcome.Usage(GetString("Usage: atelier [--launcher | --server <name>]"));
                        return false;
                }
            }

            var targetOverrideCount = 0;
            if (targetLauncher) {
                targetOverrideCount++;
            }

            if (targetServerName is not null) {
                targetOverrideCount++;
            }

            if (targetOverrideCount > 1) {
                failure = CommandOutcome.Error(GetString("Only one target override can be specified."));
                return false;
            }

            if (!TryResolveInvocationHost(context, out var invocationHost, out failure)) {
                return false;
            }

            var resolvedInvocationHost = invocationHost ?? throw new InvalidOperationException(GetString("Resolved Atelier invocation host is missing."));
            if (!TryResolveTargetProfile(resolvedInvocationHost, targetLauncher, targetServerName, out var targetProfile, out failure)) {
                return false;
            }

            options = new OpenOptions(resolvedInvocationHost, targetProfile!);
            return true;
        }

        private static bool TryResolveInvocationHost(
            CommandInvocationContext context,
            out InvocationHost? invocationHost,
            out CommandOutcome? failure) {
            failure = null;
            invocationHost = null;
            if (context.Target.Equals(CommandInvocationTarget.LauncherConsole)) {
                invocationHost = LauncherInvocationHost.Instance;
            } else if (context.Target.Equals(CommandInvocationTarget.ServerConsole) && context.Server is not null) {
                invocationHost = new ServerInvocationHost(context.Server);
            }
            if (invocationHost is not null) {
                return true;
            }

            failure = CommandOutcome.Error(GetString("Atelier can only be opened from the launcher console or a specific server console."));
            return false;
        }

        private static bool TryResolveTargetProfile(
            InvocationHost invocationHost,
            bool targetLauncher,
            string? targetServerName,
            out TargetProfile? targetProfile,
            out CommandOutcome? failure) {
            failure = null;

            if (targetLauncher) {
                targetProfile = LauncherProfile.Instance;
                return true;
            }

            if (targetServerName is not null) {
                var targetServer = FindServer(targetServerName);
                if (targetServer is null) {
                    targetProfile = null;
                    failure = CommandOutcome.Error(GetString($"Server '{targetServerName}' not found."));
                    return false;
                }

                targetProfile = new ServerProfile(targetServer);
                return true;
            }

            targetProfile = invocationHost switch {
                LauncherInvocationHost => LauncherProfile.Instance,
                ServerInvocationHost serverHost => new ServerProfile(serverHost.HostServer),
                _ => throw new InvalidOperationException(GetString($"Unsupported atelier invocation host '{invocationHost.GetType().FullName}'.")),
            };
            return true;
        }

        private static bool TryReadValue(IReadOnlyList<string> args, ref int index, out string? value) {
            value = null;
            var nextIndex = index + 1;
            if (nextIndex >= args.Count || string.IsNullOrWhiteSpace(args[nextIndex])) {
                return false;
            }

            value = args[nextIndex].Trim();
            index = nextIndex;
            return true;
        }

        private static ServerContext? FindServer(string rawName) {
            var name = rawName.Trim();
            foreach (var server in UnifiedServerCoordinator.Servers) {
                if (!server.IsRunning) {
                    continue;
                }

                if (string.Equals(server.Name, name, StringComparison.OrdinalIgnoreCase)) {
                    return server;
                }
            }

            return null;
        }
    }
}
