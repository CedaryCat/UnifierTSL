using UnifierTSL.Commanding;

namespace CommandTeleport
{
    [ControllerGroup(
        typeof(CommandTeleportTransferCommand),
        typeof(CommandTeleportServersCommand))]
    internal sealed partial class TeleportCommandController { }
}
