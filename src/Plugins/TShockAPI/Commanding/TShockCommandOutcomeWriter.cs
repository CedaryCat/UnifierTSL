using System.Text;
using UnifierTSL.Commanding;
using UnifierTSL.Extensions;
using UnifierTSL.Logging;

namespace TShockAPI.Commanding
{
    internal sealed class TShockCommandOutcomeWriter : StandardCommandOutcomeWriter<CommandExecutor>
    {
        protected override void WriteReceipt(CommandExecutor sink, CommandReceipt receipt) {
            TSCommandReceiptWriter.Write(sink, receipt);
        }

        protected override void WriteLog(CommandExecutor sink, CommandLogEntry log) {
            switch (log.Level) {
                case LogLevel.Error:
                    sink.LogError(log.Message);
                    break;
                case LogLevel.Warning:
                    sink.LogWarning(log.Message);
                    break;
                case LogLevel.Success:
                    sink.LogSuccess(log.Message);
                    break;
                default:
                    sink.LogInfo(log.Message);
                    break;
            }
        }

        protected override void WriteLogs(CommandExecutor sink, CommandOutcome outcome) {
            if (outcome.Logs.Length == 0) {
                return;
            }

            var level = LogLevel.Info;
            var builder = new StringBuilder();
            foreach (var log in outcome.Logs) {
                if (builder.Length != 0 && level != log.Level) {
                    Flush(sink, level, builder);
                }

                level = log.Level;
                builder.AppendLine(log.Message);
            }

            Flush(sink, level, builder);
        }

        protected override bool TryWriteAttachment(CommandExecutor sink, ICommandOutcomeAttachment attachment) {
            switch (attachment) {
                case TSPageAttachment page:
                    sink.SendPage(page.PageNumber, page.DataToPaginate, page.DataToPaginateCount, page.Settings);
                    return true;
                case TSMultipleMatchAttachment multipleMatch:
                    sink.SendMultipleMatchError(multipleMatch.Matches);
                    return true;
                case TSFileTextAttachment fileText:
                    sink.SendFileTextAsMessage(fileText.File);
                    return true;
                case TSPlayerReceiptAttachment playerReceipt:
                    TSCommandReceiptWriter.Write(playerReceipt.Player, playerReceipt.Receipt);
                    return true;
                default:
                    return false;
            }
        }

        private static void Flush(CommandExecutor sink, LogLevel level, StringBuilder builder) {
            builder.RemoveLastNewLine();
            var message = builder.ToString();
            switch (level) {
                case LogLevel.Error:
                    sink.LogError(message);
                    break;
                case LogLevel.Warning:
                    sink.LogWarning(message);
                    break;
                case LogLevel.Success:
                    sink.LogSuccess(message);
                    break;
                default:
                    sink.LogInfo(message);
                    break;
            }

            builder.Length = 0;
        }
    }
}
