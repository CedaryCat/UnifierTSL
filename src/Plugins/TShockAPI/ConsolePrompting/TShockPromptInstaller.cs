using UnifierTSL;
using UnifierTSL.Surface.Prompting;

namespace TShockAPI.ConsolePrompting
{
    internal static class TShockPromptInstaller
    {
        public static IDisposable Install() {
            var explainerRegistration = TSConsoleParameterExplainers.RegisterDefaults();
            var prefixRegistration = PromptRegistry.RegisterCommandPrefixProvider(BuildCommandPrefixes);
            return CompositeDisposable.Create(explainerRegistration, prefixRegistration);
        }

        private static List<string> BuildCommandPrefixes() {
            return [.. Commanding.TSCommandBridge.ResolveCommandPrefixes()];
        }
    }
}
