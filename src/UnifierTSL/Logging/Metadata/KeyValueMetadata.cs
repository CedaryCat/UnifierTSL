namespace UnifierTSL.Logging.Metadata
{
    public readonly struct KeyValueMetadata(string key, string value)
    {
        public readonly string Key = key;
        public readonly string Value = value;
    }
}
