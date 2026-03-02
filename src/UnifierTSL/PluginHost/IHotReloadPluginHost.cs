using UnifierTSL.Plugins;

namespace UnifierTSL.PluginHost
{
    /// <summary>
    /// Optional host extension for targeted plugin hot-reload handoff.
    /// </summary>
    public interface IHotReloadPluginHost
    {
        Task<PluginHotReloadResult> TryHotReloadAsync(PluginHotReloadRequest request, CancellationToken cancellationToken = default);
    }
}
