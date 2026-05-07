using System.Collections;
using System.Text.Json.Nodes;
using UnifierTSL.Commanding;

namespace TShockAPI.Commanding
{
    public sealed record TSPageAttachment(
        int PageNumber,
        IList DataToPaginate,
        int DataToPaginateCount,
        PaginationTools.Settings? Settings,
        CommandOutcomeAttachmentPhase Phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) : ICommandOutcomeAttachment
    {
        public JsonObject ToJsonMetadata() {
            return new JsonObject {
                ["type"] = nameof(TSPageAttachment),
                ["pageNumber"] = PageNumber,
                ["dataCount"] = DataToPaginateCount,
                ["hasSettings"] = Settings is not null,
                ["phase"] = Phase.ToString(),
            };
        }
    }

    public sealed record TSMultipleMatchAttachment(
        IReadOnlyList<object> Matches,
        CommandOutcomeAttachmentPhase Phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) : ICommandOutcomeAttachment
    {
        public JsonObject ToJsonMetadata() {
            JsonArray matches = [];
            foreach (var match in Matches) {
                matches.Add(match?.ToString() ?? string.Empty);
            }

            return new JsonObject {
                ["type"] = nameof(TSMultipleMatchAttachment),
                ["matchCount"] = Matches.Count,
                ["matches"] = matches,
                ["phase"] = Phase.ToString(),
            };
        }
    }

    public sealed record TSFileTextAttachment(
        string File,
        CommandOutcomeAttachmentPhase Phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) : ICommandOutcomeAttachment
    {
        public JsonObject ToJsonMetadata() {
            return new JsonObject {
                ["type"] = nameof(TSFileTextAttachment),
                ["file"] = File,
                ["phase"] = Phase.ToString(),
            };
        }
    }

    public sealed record TSPlayerReceiptAttachment(
        TSPlayer Player,
        CommandReceipt Receipt,
        CommandOutcomeAttachmentPhase Phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) : ICommandOutcomeAttachment
    {
        public JsonObject ToJsonMetadata() {
            return new JsonObject {
                ["type"] = nameof(TSPlayerReceiptAttachment),
                ["playerName"] = Player.Name,
                ["playerIndex"] = Player.Index,
                ["serverName"] = Player.GetCurrentServer()?.Name,
                ["receiptKind"] = Receipt.Kind.ToString(),
                ["message"] = Receipt.Message,
                ["phase"] = Phase.ToString(),
            };
        }
    }

    public static class TSCommandOutcome
    {
        extension(CommandOutcome)
        {
            public static CommandOutcome InfoLines(IEnumerable<string> lines) {
                return InfoLinesBuilder(lines).Build();
            }

            public static CommandOutcome.Builder InfoLinesBuilder(IEnumerable<string> lines) {
                return AddInfoLines(CommandOutcome.CreateBuilder(), lines);
            }

            public static CommandOutcome FileText(
                string file,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return FileTextBuilder(file, phase).Build();
            }

            public static CommandOutcome.Builder FileTextBuilder(
                string file,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return AddFileText(CommandOutcome.CreateBuilder(), file, phase);
            }

            public static CommandOutcome Page(
                int pageNumber,
                IEnumerable dataToPaginate,
                int dataToPaginateCount,
                PaginationTools.Settings? settings = null,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return PageBuilder(pageNumber, dataToPaginate, dataToPaginateCount, settings, phase).Build();
            }

            public static CommandOutcome.Builder PageBuilder(
                int pageNumber,
                IEnumerable dataToPaginate,
                int dataToPaginateCount,
                PaginationTools.Settings? settings = null,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return AddPage(CommandOutcome.CreateBuilder(), pageNumber, dataToPaginate, dataToPaginateCount, settings, phase);
            }

            public static CommandOutcome MultipleMatches(
                IEnumerable<object> matches,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return MultipleMatchesBuilder(matches, phase).Build();
            }

            public static CommandOutcome.Builder MultipleMatchesBuilder(
                IEnumerable<object> matches,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return AddMultipleMatches(CommandOutcome.CreateBuilder(), matches, phase);
            }

            public static CommandOutcome PlayerReceipt(
                TSPlayer player,
                CommandReceipt receipt,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return PlayerReceiptBuilder(player, receipt, phase).Build();
            }

            public static CommandOutcome.Builder PlayerReceiptBuilder(
                TSPlayer player,
                CommandReceipt receipt,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return AddPlayerReceipt(CommandOutcome.CreateBuilder(), player, receipt, phase);
            }

            public static CommandOutcome PlayerInfo(
                TSPlayer player,
                string message,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return PlayerInfoBuilder(player, message, phase).Build();
            }

            public static CommandOutcome.Builder PlayerInfoBuilder(
                TSPlayer player,
                string message,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return AddPlayerInfo(CommandOutcome.CreateBuilder(), player, message, phase);
            }

            public static CommandOutcome PlayerSuccess(
                TSPlayer player,
                string message,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return PlayerSuccessBuilder(player, message, phase).Build();
            }

            public static CommandOutcome.Builder PlayerSuccessBuilder(
                TSPlayer player,
                string message,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return AddPlayerSuccess(CommandOutcome.CreateBuilder(), player, message, phase);
            }

            public static CommandOutcome PlayerWarning(
                TSPlayer player,
                string message,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return PlayerWarningBuilder(player, message, phase).Build();
            }

            public static CommandOutcome.Builder PlayerWarningBuilder(
                TSPlayer player,
                string message,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return AddPlayerWarning(CommandOutcome.CreateBuilder(), player, message, phase);
            }

            public static CommandOutcome PlayerError(
                TSPlayer player,
                string message,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return PlayerErrorBuilder(player, message, phase).Build();
            }

            public static CommandOutcome.Builder PlayerErrorBuilder(
                TSPlayer player,
                string message,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return AddPlayerError(CommandOutcome.CreateBuilder(), player, message, phase);
            }

            public static CommandOutcome? TryParsePageNumber(IReadOnlyList<string> commandParameters, int expectedParameterIndex, out int pageNumber) {
                pageNumber = 1;
                if (commandParameters.Count <= expectedParameterIndex) {
                    return null;
                }

                var pageNumberRaw = commandParameters[expectedParameterIndex];
                if (!int.TryParse(pageNumberRaw, out pageNumber) || pageNumber < 1) {
                    pageNumber = 1;
                    return CommandOutcome.Error(GetString("\"{0}\" is not a valid page number.", pageNumberRaw));
                }

                return null;
            }
        }
        extension(CommandOutcome.Builder builder)
        {

            public CommandOutcome.Builder AddInfoLines(IEnumerable<string> lines) {
                ArgumentNullException.ThrowIfNull(builder);
                ArgumentNullException.ThrowIfNull(lines);

                foreach (var line in lines.Where(static line => !string.IsNullOrWhiteSpace(line))) {
                    builder.AddInfo(line);
                }

                return builder;
            }

            public CommandOutcome.Builder AddFileText(
                string file,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                ArgumentNullException.ThrowIfNull(builder);
                ArgumentException.ThrowIfNullOrWhiteSpace(file);

                builder.AddAttachment(new TSFileTextAttachment(file, phase));
                return builder;
            }

            public CommandOutcome.Builder AddPage(
                int pageNumber,
                IEnumerable dataToPaginate,
                int dataToPaginateCount,
                PaginationTools.Settings? settings = null,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                ArgumentNullException.ThrowIfNull(builder);
                ArgumentNullException.ThrowIfNull(dataToPaginate);

                builder.AddAttachment(new TSPageAttachment(
                    pageNumber,
                    MaterializeList(dataToPaginate),
                    dataToPaginateCount,
                    settings,
                    phase));
                return builder;
            }

            public CommandOutcome.Builder AddMultipleMatches(
                IEnumerable<object> matches,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                ArgumentNullException.ThrowIfNull(builder);
                ArgumentNullException.ThrowIfNull(matches);

                builder.AddAttachment(new TSMultipleMatchAttachment([.. matches], phase));
                return builder;
            }

            public CommandOutcome.Builder AddPlayerReceipt(
                TSPlayer player,
                CommandReceipt receipt,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                ArgumentNullException.ThrowIfNull(builder);
                ArgumentNullException.ThrowIfNull(player);

                builder.AddAttachment(new TSPlayerReceiptAttachment(
                    player,
                    receipt,
                    phase));
                return builder;
            }

            public CommandOutcome.Builder AddPlayerInfo(
                TSPlayer player,
                string message,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return builder.AddPlayerReceipt(player, CommandReceipt.Info(message), phase);
            }

            public CommandOutcome.Builder AddPlayerSuccess(
                TSPlayer player,
                string message,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return builder.AddPlayerReceipt(player, CommandReceipt.Success(message), phase);
            }

            public CommandOutcome.Builder AddPlayerWarning(
                TSPlayer player,
                string message,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return builder.AddPlayerReceipt(player, CommandReceipt.Warning(message), phase);
            }

            public CommandOutcome.Builder AddPlayerError(
                TSPlayer player,
                string message,
                CommandOutcomeAttachmentPhase phase = CommandOutcomeAttachmentPhase.AfterPrimaryReceipts) {
                return builder.AddPlayerReceipt(player, CommandReceipt.Error(message), phase);
            }
        }
        private static IList MaterializeList(IEnumerable dataToPaginate) {
            if (dataToPaginate is IList list) {
                return list;
            }

            ArrayList materialized = [];
            foreach (var item in dataToPaginate) {
                materialized.Add(item);
            }

            return materialized;
        }
    }
}
