using UnifierTSL.Plugins;

namespace UnifierTSL.PluginHost
{
    public readonly record struct PluginHotReloadResult(
        bool Success,
        HotReloadReasonCode ReasonCode,
        string Message,
        string MatchKey,
        string PluginFilePath,
        string EntryPoint,
        Version? OldVersion = null,
        Version? NewVersion = null)
    {
        public static PluginHotReloadResult Accepted(
            string matchKey,
            string pluginFilePath,
            string entryPoint,
            Version oldVersion,
            Version newVersion,
            string message)
        {
            return new(
                Success: true,
                ReasonCode: HotReloadReasonCode.None,
                Message: message,
                MatchKey: matchKey,
                PluginFilePath: pluginFilePath,
                EntryPoint: entryPoint,
                OldVersion: oldVersion,
                NewVersion: newVersion);
        }

        public static PluginHotReloadResult Rejected(
            HotReloadReasonCode reasonCode,
            string message,
            string matchKey,
            string pluginFilePath,
            string entryPoint,
            Version? oldVersion = null,
            Version? newVersion = null)
        {
            return new(
                Success: false,
                ReasonCode: reasonCode,
                Message: message,
                MatchKey: matchKey,
                PluginFilePath: pluginFilePath,
                EntryPoint: entryPoint,
                OldVersion: oldVersion,
                NewVersion: newVersion);
        }
    }
}
