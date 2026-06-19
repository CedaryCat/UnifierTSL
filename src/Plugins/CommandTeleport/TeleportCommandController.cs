using UnifierTSL.Commanding;

namespace CommandTeleport
{
    [ControllerGroup(
        typeof(CommandTeleportTransferCommand),
        typeof(CommandTeleportServersCommand))]
    public sealed partial class TeleportCommandController { }
}
