using UnifierTSL.ConsoleClient.Shell;

namespace UnifierTSL.CLI.Sessions
{
    internal interface IReadLineContextMaterializer
    {
        ReadLineReactiveState CreateInitialReactiveState(ReadLineContextSpec contextSpec);

        ReadLineResolvedContext BuildContext(
            ReadLineContextSpec contextSpec,
            ReadLineReactiveState state,
            ReadLineMaterializationScenario scenario);
    }
}
