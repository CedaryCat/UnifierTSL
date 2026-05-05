using Microsoft.Xna.Framework;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Logging;

namespace UnifierTSL.Commanding.Prompting
{
    internal sealed class TerminalCommandOutcomeWriter : StandardCommandOutcomeWriter<MessageSender>
    {
        protected override void WriteReceipt(MessageSender sink, CommandReceipt receipt) {
            sink.Chat(receipt.Message, ResolveReceiptColor(receipt.Kind));
        }

        protected override void WriteLog(MessageSender sink, CommandLogEntry log) {
            switch (log.Level) {
                case LogLevel.Error:
                    UnifierApi.Logger.Error(log.Message, category: "Commanding");
                    break;
                case LogLevel.Warning:
                    UnifierApi.Logger.Warning(log.Message, category: "Commanding");
                    break;
                case LogLevel.Success:
                    UnifierApi.Logger.Success(log.Message, category: "Commanding");
                    break;
                default:
                    UnifierApi.Logger.Info(log.Message, category: "Commanding");
                    break;
            }
        }

        private static Color ResolveReceiptColor(CommandReceiptKind kind) {
            return kind switch {
                CommandReceiptKind.Success => Color.LightGreen,
                CommandReceiptKind.Warning => Color.Gold,
                CommandReceiptKind.Error => Color.IndianRed,
                CommandReceiptKind.Usage => Color.LightSkyBlue,
                _ => Color.White,
            };
        }
    }
}
