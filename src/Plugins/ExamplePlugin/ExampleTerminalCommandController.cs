using UnifierTSL.Commanding;

namespace ExamplePlugin
{
    [ControllerGroup(typeof(ExampleSimulatedTaskCommand))]
    internal sealed partial class ExampleTerminalCommandController { }
}
