using UnifierTSL.ConsoleClient.Shell;

namespace UnifierTSL.CLI.Sessions
{
    internal interface IReadLineSemanticProvider
    {
        ReadLineSemanticSnapshot BuildInitial();

        ReadLineSemanticSnapshot BuildReactive(ReadLineReactiveState state);
    }
}
