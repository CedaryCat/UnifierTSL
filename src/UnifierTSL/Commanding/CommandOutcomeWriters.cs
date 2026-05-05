using System.Collections.Immutable;

namespace UnifierTSL.Commanding
{
    public interface ICommandOutcomeWriter<in TSink>
    {
        void Write(TSink sink, CommandOutcome outcome);
    }

    public abstract class StandardCommandOutcomeWriter<TSink> : ICommandOutcomeWriter<TSink>
    {
        public virtual void Write(TSink sink, CommandOutcome outcome) {
            ArgumentNullException.ThrowIfNull(sink);
            ArgumentNullException.ThrowIfNull(outcome);

            WriteAttachments(sink, outcome, CommandOutcomeAttachmentPhase.BeforePrimaryReceipts);
            WriteReceipts(sink, outcome);
            WriteAttachments(sink, outcome, CommandOutcomeAttachmentPhase.AfterPrimaryReceipts);
            WriteLogs(sink, outcome);
        }

        protected virtual void WriteReceipts(TSink sink, CommandOutcome outcome) {
            foreach (var receipt in outcome.Receipts) {
                WriteReceipt(sink, receipt);
            }
        }

        protected virtual void WriteLogs(TSink sink, CommandOutcome outcome) {
            foreach (var log in outcome.Logs) {
                WriteLog(sink, log);
            }
        }

        protected virtual void WriteAttachments(
            TSink sink,
            CommandOutcome outcome,
            CommandOutcomeAttachmentPhase phase) {
            foreach (var attachment in outcome.Attachments) {
                if (attachment.Phase != phase) {
                    continue;
                }

                if (!TryWriteAttachment(sink, attachment)) {
                    throw new InvalidOperationException(GetParticularString(
                        "{0} is command attachment metadata json, {1} is sink type name",
                        $"No outcome writer attachment handler was found for sink '{typeof(TSink).FullName}' and attachment {attachment.ToJsonMetadata().ToJsonString()}."));
                }
            }
        }

        protected abstract void WriteReceipt(TSink sink, CommandReceipt receipt);

        protected abstract void WriteLog(TSink sink, CommandLogEntry log);

        protected virtual bool TryWriteAttachment(TSink sink, ICommandOutcomeAttachment attachment) {
            return false;
        }
    }

    public sealed record CommandOutcomeWriterRegistration(Type SinkType, object Writer);

    internal sealed class CommandOutcomeWriterRegistryBuilder
    {
        private readonly Dictionary<Type, object> writers = [];

        public void AddWriter<TSink, TWriter>() where TWriter : ICommandOutcomeWriter<TSink>, new() {
            AddWriter(new TWriter());
        }

        public void AddWriter<TSink>(ICommandOutcomeWriter<TSink> writer) {
            ArgumentNullException.ThrowIfNull(writer);
            var sinkType = typeof(TSink);
            if (!writers.TryAdd(sinkType, writer)) {
                throw new InvalidOperationException(GetParticularString(
                    "{0} is sink type name",
                    $"An outcome writer for sink '{sinkType.FullName}' is already registered."));
            }
        }

        public ImmutableArray<CommandOutcomeWriterRegistration> Build() {
            return [.. writers
                .OrderBy(static pair => pair.Key.FullName, StringComparer.Ordinal)
                .Select(static pair => new CommandOutcomeWriterRegistration(pair.Key, pair.Value))];
        }
    }
}
