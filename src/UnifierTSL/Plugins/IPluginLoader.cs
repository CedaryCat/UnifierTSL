using UnifierTSL.PluginService;

namespace UnifierTSL.Plugins
{
    public interface IPluginLoader
    {
        IPluginContainer? LoadPlugin(IPluginInfo pluginInfo, bool addToHost = true);
    }
}
