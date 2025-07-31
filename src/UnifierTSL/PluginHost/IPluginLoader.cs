using UnifierTSL.PluginService;

namespace UnifierTSL.PluginHost
{
    public interface IPluginLoader
    {
        IPluginContainer? LoadPlugin(IPluginInfo pluginInfo, bool addToHost = true);
    }
}
