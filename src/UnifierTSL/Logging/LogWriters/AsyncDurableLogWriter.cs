using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace UnifierTSL.Logging.LogWriters
{
    internal sealed class AsyncDurableLogWriter : LogWriter, ILogHistorySink, IDisposable
    {
        public const int BatchSize = 128;
        public const int BacklogWarnThreshold = 20_000;
        public const int BacklogWarnIntervalMs = 5_000;

        private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(3);

        private readonly IDurableLogSink sink;
        private readonly Channel<QueuedDurableLogRecord> queue;
        private readonly CancellationTokenSource stoppingCts = new();
        private readonly Task consumerTask;
        private readonly Lock disposeGate = new();

        private long enqueuedCount;
        private long dequeuedCount;
        private long lastBacklogWarnTick;
        private int sinkFailed;
        private int sinkFailureReported;
        private int disposed;

        public AsyncDurableLogWriter(IDurableLogSink sink) {
            this.sink = sink;
            queue = Channel.CreateUnbounded<QueuedDurableLogRecord>(new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
            consumerTask = Task.Run(ConsumeLoop);
        }

        public override void Write(scoped in LogEntry log) {
            if (Volatile.Read(ref sinkFailed) != 0 || Volatile.Read(ref disposed) != 0) {
                return;
            }

            Enqueue(QueuedDurableLogRecord.FromLogEntry(in log));
        }

        public void Write(scoped in LogRecordView record) {
            if (Volatile.Read(ref sinkFailed) != 0 || Volatile.Read(ref disposed) != 0) {
                return;
            }

            Enqueue(QueuedDurableLogRecord.FromLogRecordView(in record));
        }

        private void Enqueue(QueuedDurableLogRecord record) {
            if (!queue.Writer.TryWrite(record)) {
                record.ReturnPooledResources();
                return;
            }

            long enqueued = Interlocked.Increment(ref enqueuedCount);
            long backlog = enqueued - Volatile.Read(ref dequeuedCount);
            TryWarnBacklog(backlog);
        }

        private static void ReturnBatchResources(List<QueuedDurableLogRecord> batch) {
            int count = batch.Count;
            for (int i = 0; i < count; i++) {
                batch[i].ReturnPooledResources();
            }

            batch.Clear();
        }

        private async Task ConsumeLoop() {
            List<QueuedDurableLogRecord> batch = new(BatchSize);
            using PeriodicTimer flushTimer = new(FlushInterval);

            Task<bool> readTask = queue.Reader.WaitToReadAsync(stoppingCts.Token).AsTask();
            Task<bool> tickTask = flushTimer.WaitForNextTickAsync(stoppingCts.Token).AsTask();

            try {
                while (true) {
                    Task completed = await Task.WhenAny(readTask, tickTask);
                    if (completed == readTask) {
                        bool canRead = await readTask;
                        readTask = queue.Reader.WaitToReadAsync(stoppingCts.Token).AsTask();
                        if (!canRead) {
                            break;
                        }

                        DrainPendingRecords(batch);
                    }
                    else {
                        bool hasTick = await tickTask;
                        tickTask = flushTimer.WaitForNextTickAsync(stoppingCts.Token).AsTask();
                        if (!hasTick) {
                            break;
                        }

                        FlushBatch(batch);
                    }
                }

                DrainPendingRecords(batch);
                FlushBatch(batch);
                FlushSink();
            }
            catch (OperationCanceledException) {
                DrainPendingRecords(batch);
                FlushBatch(batch);
                FlushSink();
            }
            finally {
                ReturnBatchResources(batch);
            }
        }

        private void DrainPendingRecords(List<QueuedDurableLogRecord> batch) {
            while (queue.Reader.TryRead(out QueuedDurableLogRecord record)) {
                batch.Add(record);
                Interlocked.Increment(ref dequeuedCount);
                if (batch.Count >= BatchSize) {
                    FlushBatch(batch);
                }
            }
        }

        private void FlushBatch(List<QueuedDurableLogRecord> batch) {
            if (batch.Count <= 0) {
                return;
            }

            try {
                if (Volatile.Read(ref sinkFailed) == 0) {
                    sink.WriteBatch(CollectionsMarshal.AsSpan(batch));
                    sink.Flush();
                }
            }
            catch (Exception ex) {
                MarkSinkFailure(ex);
            }
            finally {
                ReturnBatchResources(batch);
            }
        }

        private void FlushSink() {
            if (Volatile.Read(ref sinkFailed) != 0) {
                return;
            }

            try {
                sink.Flush();
            }
            catch (Exception ex) {
                MarkSinkFailure(ex);
            }
        }

        private void TryWarnBacklog(long backlog) {
            if (backlog < BacklogWarnThreshold) {
                return;
            }

            long now = Environment.TickCount64;
            long previous = Volatile.Read(ref lastBacklogWarnTick);
            if (now - previous < BacklogWarnIntervalMs) {
                return;
            }

            if (Interlocked.CompareExchange(ref lastBacklogWarnTick, now, previous) != previous) {
                return;
            }

            Console.Error.WriteLine(
                GetParticularString("{0} is durable backlog count, {1} is durable backlog warning threshold", $"[Warning][LogCore|DurableQueue] Durable backlog is {backlog} entries (threshold: {BacklogWarnThreshold})."));
        }

        private void MarkSinkFailure(Exception ex) {
            Volatile.Write(ref sinkFailed, 1);
            if (Interlocked.CompareExchange(ref sinkFailureReported, 1, 0) != 0) {
                return;
            }

            Console.Error.WriteLine(
                GetParticularString("{0} is exception text", $"[Error][LogCore|DurableQueue] Durable sink disabled after failure: {ex}"));
        }

        public bool TryFlushAndStop(TimeSpan timeout) {
            bool alreadyDisposed = false;
            lock (disposeGate) {
                if (Interlocked.Exchange(ref disposed, 1) != 0) {
                    alreadyDisposed = true;
                }
                else {
                    queue.Writer.TryComplete();
                }
            }

            if (alreadyDisposed) {
                return true;
            }

            bool completed = WaitForConsumer(timeout);
            if (!completed) {
                stoppingCts.Cancel();
                completed = WaitForConsumer(TimeSpan.FromMilliseconds(500));
            }

            try {
                sink.Dispose();
            }
            catch (Exception ex) {
                MarkSinkFailure(ex);
            }

            stoppingCts.Dispose();
            return completed;
        }

        private bool WaitForConsumer(TimeSpan timeout) {
            try {
                return consumerTask.Wait(timeout);
            }
            catch (AggregateException ex) {
                Exception root = ex.GetBaseException();
                MarkSinkFailure(root);
                return true;
            }
            catch (Exception ex) {
                MarkSinkFailure(ex);
                return true;
            }
        }

        public void Dispose() {
            _ = TryFlushAndStop(DefaultShutdownTimeout);
        }
    }
}
