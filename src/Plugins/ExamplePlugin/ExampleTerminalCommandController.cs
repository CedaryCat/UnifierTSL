using UnifierTSL.Commanding;

namespace ExamplePlugin
{
    [ControllerGroup(typeof(ExampleSimulatedTaskCommand))]
    public sealed partial class ExampleTerminalCommandController { }
}
