namespace UnifierTSL.PluginHost
{
    public sealed record PluginHotReloadRequest(
        string PluginFilePath,
        string EntryPoint,
        string? MatchKey = null,
        int SchemaVersion = 1)
    {
        public string ResolveMatchKey()
        {
            return string.IsNullOrWhiteSpace(MatchKey)
                ? PluginHotReloadMatchKey.Create(PluginFilePath, EntryPoint)
                : MatchKey;
        }
    }
}
