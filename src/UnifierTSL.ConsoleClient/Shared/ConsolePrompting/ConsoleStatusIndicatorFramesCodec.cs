namespace UnifierTSL.ConsoleClient.Shared.ConsolePrompting
{
    public static class ConsoleStatusIndicatorFramesCodec
    {
        public const char FrameSeparator = '\u001f';

        public static string Serialize(IEnumerable<string>? frames) {
            if (frames is null) {
                return string.Empty;
            }

            return string.Join(FrameSeparator.ToString(), frames);
        }

        public static string[] Deserialize(string? serialized) {
            if (string.IsNullOrEmpty(serialized)) {
                return [];
            }

            return serialized.Split(FrameSeparator, StringSplitOptions.None);
        }
    }
}
