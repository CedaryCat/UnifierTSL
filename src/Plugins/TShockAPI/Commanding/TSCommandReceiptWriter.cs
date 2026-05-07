using UnifierTSL.Commanding;

namespace TShockAPI.Commanding
{
    internal static class TSCommandReceiptWriter
    {
        public static void Write(CommandExecutor executor, CommandReceipt receipt) {
            switch (receipt.Kind) {
                case CommandReceiptKind.Success:
                    executor.SendSuccessMessage(receipt.Message);
                    break;
                case CommandReceiptKind.Warning:
                    executor.SendWarningMessage(receipt.Message);
                    break;
                case CommandReceiptKind.Error:
                    executor.SendErrorMessage(receipt.Message);
                    break;
                case CommandReceiptKind.Usage:
                    executor.SendInfoMessage(receipt.Message);
                    break;
                default:
                    executor.SendInfoMessage(receipt.Message);
                    break;
            }
        }

        public static void Write(TSPlayer player, CommandReceipt receipt) {
            switch (receipt.Kind) {
                case CommandReceiptKind.Success:
                    player.SendSuccessMessage(receipt.Message);
                    break;
                case CommandReceiptKind.Warning:
                    player.SendWarningMessage(receipt.Message);
                    break;
                case CommandReceiptKind.Error:
                    player.SendErrorMessage(receipt.Message);
                    break;
                case CommandReceiptKind.Usage:
                    player.SendInfoMessage(receipt.Message);
                    break;
                default:
                    player.SendInfoMessage(receipt.Message);
                    break;
            }
        }
    }
}
