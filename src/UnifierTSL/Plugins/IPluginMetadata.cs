namespace UnifierTSL.Plugins
{
    public interface IPluginMetadata
    {
        string Name { get; }
        Version Version { get; }
        string Author { get; }
        string Description { get; }
    }
}
