namespace UnifierTSL.PluginHost
{
    public static class PluginHotReloadMatchKey
    {
        private const string Separator = "::";

        public static string Create(string pluginFilePath, string entryPoint)
        {
            if (string.IsNullOrWhiteSpace(pluginFilePath))
                throw new ArgumentException("Plugin file path cannot be null or empty.", nameof(pluginFilePath));
            if (string.IsNullOrWhiteSpace(entryPoint))
                throw new ArgumentException("Entry point cannot be null or empty.", nameof(entryPoint));

            string normalizedPath = Path.GetFullPath(pluginFilePath).Trim();
            string normalizedEntryPoint = entryPoint.Trim();
            return normalizedPath + Separator + normalizedEntryPoint;
        }
    }
}
