namespace UnifierTSL.PluginHost
{
    public interface IPluginEntryPoint
    {
        object EntryPoint { get; }
        string EntryPointString { get; }
    }
}
