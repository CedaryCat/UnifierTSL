using UnifierTSL.ConsoleClient.Shell;

namespace UnifierTSL.CLI.Sessions
{
    internal static class ReadLineRenderPaging
    {
        public const int DefaultPageSize = 30;
        public const int DefaultPrefetchThreshold = 5;
        private static readonly ReadLineRenderMapOptions PagingOptions = new(
            EnablePaging: true,
            PageSize: DefaultPageSize,
            PrefetchThreshold: DefaultPrefetchThreshold);

        public static ReadLineRenderSnapshot BuildSnapshot(ReadLineSemanticSnapshot semantic, ReadLineReactiveState state)
        {
            ArgumentNullException.ThrowIfNull(semantic);
            ArgumentNullException.ThrowIfNull(state);
            return SemanticToRenderMapper.Map(semantic, state, PagingOptions);
        }
    }
}
