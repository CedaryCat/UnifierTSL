using System.Collections.Concurrent;
using UnifierTSL.Logging;

namespace UnifierTSL.Commanding.Jobs
{
    public sealed class InMemoryCommandJobRunner : ICommandJobRunner, IDisposable
    {
        private readonly ConcurrentDictionary<CommandJobId, JobEntry> jobs = [];
        private readonly ICommandJobExecutor executor;
        private readonly CommandJobRunnerOptions options;
        private readonly TimeProvider timeProvider;
        private readonly Func<CommandJobId> idFactory;
        private int disposed;

        public InMemoryCommandJobRunner(
            ICommandJobExecutor executor,
            CommandJobRunnerOptions? options = null,
            TimeProvider? timeProvider = null,
            Func<CommandJobId>? idFactory = null) {
            this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
            this.options = ValidateOptions(options ?? new CommandJobRunnerOptions());
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.idFactory = idFactory ?? CommandJobId.CreateNew;
        }

        public ValueTask<CommandJobStartResult> StartAsync(
            CommandJobRequest request,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            var now = timeProvider.GetUtcNow();
            Prune(now);

            JobEntry? entry = null;
            while (entry is null) {
                var snapshot = CreateInitialSnapshot(request, now);
                var candidate = new JobEntry(request, snapshot);
                if (jobs.TryAdd(candidate.Id, candidate)) {
                    entry = candidate;
                    continue;
                }

                candidate.Dispose();
            }

            entry.RunTask = Task.Run(() => RunAsync(entry), CancellationToken.None);
            return ValueTask.FromResult(new CommandJobStartResult {
                JobId = entry.Id,
                Snapshot = entry.GetSnapshot(),
            });
        }

        public ValueTask<CommandJobSnapshot?> GetAsync(
            CommandJobId jobId,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            Prune(timeProvider.GetUtcNow());
            return ValueTask.FromResult(jobs.TryGetValue(jobId, out var entry)
                ? entry.GetSnapshot()
                : null);
        }

        public ValueTask<bool> TryCancelAsync(
            CommandJobId jobId,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            Prune(timeProvider.GetUtcNow());
            return ValueTask.FromResult(jobs.TryGetValue(jobId, out var entry)
                && entry.TryRequestCancellation(timeProvider.GetUtcNow()));
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            foreach (var entry in jobs.Values) {
                entry.TryRequestCancellation(timeProvider.GetUtcNow());
                entry.Dispose();
            }

            jobs.Clear();
        }

        private async Task RunAsync(JobEntry entry) {
            if (Volatile.Read(ref disposed) != 0) {
                entry.TryRequestCancellation(timeProvider.GetUtcNow());
            }

            if (entry.CancellationToken.IsCancellationRequested) {
                entry.MarkCanceled(timeProvider.GetUtcNow());
                return;
            }

            entry.MarkStarted(timeProvider.GetUtcNow());
            try {
                var context = new CommandJobExecutionContext {
                    JobId = entry.Id,
                    Request = entry.Request,
                    UpdateSink = new JobUpdateSink(entry, timeProvider),
                    CancellationToken = entry.CancellationToken,
                };
                var result = await executor.ExecuteAsync(context, entry.CancellationToken);
                entry.MarkCompleted(timeProvider.GetUtcNow(), result);
            }
            catch (OperationCanceledException) when (entry.CancellationToken.IsCancellationRequested) {
                entry.MarkCanceled(timeProvider.GetUtcNow());
            }
            catch (Exception ex) {
                entry.MarkFailed(timeProvider.GetUtcNow(), BuildFailureOutcome(ex));
            }
            finally {
                Prune(timeProvider.GetUtcNow());
            }
        }

        private CommandJobSnapshot CreateInitialSnapshot(CommandJobRequest request, DateTimeOffset now) {
            return new CommandJobSnapshot {
                Id = idFactory(),
                EndpointId = request.DispatchRequest.EndpointId,
                RawInput = request.DispatchRequest.RawInput,
                Title = request.Title,
                State = CommandJobState.Pending,
                CreatedUtc = now,
                UpdatedUtc = now,
                Metadata = request.Metadata,
            };
        }

        private static CommandOutcome BuildFailureOutcome(Exception ex) {
            return CommandOutcome.ErrorBuilder(GetString("Command failed, check logs for more details."))
                .AddLog(LogLevel.Error, ex.ToString())
                .Build();
        }

        private void Prune(DateTimeOffset now) {
            if (options.CompletedJobRetention != Timeout.InfiniteTimeSpan) {
                var cutoff = now - options.CompletedJobRetention;
                foreach (var pair in jobs) {
                    var snapshot = pair.Value.GetSnapshot();
                    if (!snapshot.Completed || snapshot.CompletedUtc is null || snapshot.CompletedUtc > cutoff) {
                        continue;
                    }

                    Remove(pair.Key, pair.Value);
                }
            }

            if (options.MaximumRetainedJobs <= 0 || jobs.Count <= options.MaximumRetainedJobs) {
                return;
            }

            var overflow = jobs.Count - options.MaximumRetainedJobs;
            if (overflow <= 0) {
                return;
            }

            var removable = jobs
                .Select(static pair => (pair.Key, Entry: pair.Value, Snapshot: pair.Value.GetSnapshot()))
                .Where(static item => item.Snapshot.Completed)
                .OrderBy(static item => item.Snapshot.CompletedUtc ?? item.Snapshot.UpdatedUtc)
                .ThenBy(static item => item.Snapshot.CreatedUtc)
                .Take(overflow)
                .ToArray();

            foreach (var item in removable) {
                Remove(item.Key, item.Entry);
            }
        }

        private void Remove(CommandJobId jobId, JobEntry entry) {
            if (jobs.TryRemove(jobId, out var removed)) {
                removed.Dispose();
            }
        }

        private static CommandJobRunnerOptions ValidateOptions(CommandJobRunnerOptions options) {
            if (options.MaximumRetainedJobs < 0) {
                throw new ArgumentOutOfRangeException(nameof(options), GetString("Maximum retained jobs must be zero or greater."));
            }

            if (options.CompletedJobRetention != Timeout.InfiniteTimeSpan
                && options.CompletedJobRetention < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(options), GetString("Completed job retention must be non-negative or infinite."));
            }

            return options;
        }

        private void ThrowIfDisposed() {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        }

        private sealed class JobUpdateSink(JobEntry entry, TimeProvider timeProvider) : ICommandJobUpdateSink
        {
            public void SetStatus(string? statusText) {
                entry.SetStatus(timeProvider.GetUtcNow(), statusText);
            }

            public void SetProgress(long current, long? total = null) {
                entry.SetProgress(timeProvider.GetUtcNow(), current, total);
            }

            public void ClearProgress() {
                entry.ClearProgress(timeProvider.GetUtcNow());
            }
        }

        private sealed class JobEntry : IDisposable
        {
            private readonly object gate = new();
            private readonly CancellationTokenSource cancellationSource = new();
            private CommandJobSnapshot snapshot;
            private int disposed;

            public JobEntry(CommandJobRequest request, CommandJobSnapshot snapshot) {
                Request = request;
                this.snapshot = snapshot;
            }

            public CommandJobId Id => snapshot.Id;

            public CommandJobRequest Request { get; }

            public CancellationToken CancellationToken => cancellationSource.Token;

            public Task? RunTask { get; set; }

            public CommandJobSnapshot GetSnapshot() {
                lock (gate) {
                    return snapshot;
                }
            }

            public bool TryRequestCancellation(DateTimeOffset now) {
                lock (gate) {
                    if (snapshot.Completed) {
                        return false;
                    }

                    snapshot = snapshot with {
                        State = snapshot.State == CommandJobState.Pending || snapshot.State == CommandJobState.Running
                            ? CommandJobState.CancelRequested
                            : snapshot.State,
                        CancellationRequested = true,
                        UpdatedUtc = now,
                    };
                }

                try {
                    cancellationSource.Cancel();
                }
                catch (ObjectDisposedException) {
                    return false;
                }

                return true;
            }

            public void MarkStarted(DateTimeOffset now) {
                lock (gate) {
                    if (snapshot.Completed || snapshot.StartedUtc is not null) {
                        return;
                    }

                    snapshot = snapshot with {
                        State = snapshot.CancellationRequested ? CommandJobState.CancelRequested : CommandJobState.Running,
                        StartedUtc = now,
                        UpdatedUtc = now,
                    };
                }
            }

            public void MarkCompleted(DateTimeOffset now, CommandJobExecutionResult result) {
                lock (gate) {
                    if (snapshot.Completed) {
                        return;
                    }

                    snapshot = snapshot with {
                        State = result.Succeeded ? CommandJobState.Succeeded : CommandJobState.Failed,
                        UpdatedUtc = now,
                        CompletedUtc = now,
                        Handled = result.Handled,
                        Matched = result.Matched,
                        Outcome = result.Outcome,
                    };
                }
            }

            public void MarkCanceled(DateTimeOffset now) {
                lock (gate) {
                    if (snapshot.Completed) {
                        return;
                    }

                    snapshot = snapshot with {
                        State = CommandJobState.Canceled,
                        CancellationRequested = true,
                        UpdatedUtc = now,
                        CompletedUtc = now,
                    };
                }
            }

            public void MarkFailed(DateTimeOffset now, CommandOutcome outcome) {
                lock (gate) {
                    if (snapshot.Completed) {
                        return;
                    }

                    snapshot = snapshot with {
                        State = CommandJobState.Failed,
                        UpdatedUtc = now,
                        CompletedUtc = now,
                        Outcome = outcome,
                    };
                }
            }

            public void SetStatus(DateTimeOffset now, string? statusText) {
                lock (gate) {
                    if (snapshot.Completed) {
                        return;
                    }

                    snapshot = snapshot with {
                        StatusText = string.IsNullOrWhiteSpace(statusText) ? null : statusText.Trim(),
                        UpdatedUtc = now,
                    };
                }
            }

            public void SetProgress(DateTimeOffset now, long current, long? total) {
                if (total.HasValue && total.Value < current) {
                    throw new ArgumentOutOfRangeException(nameof(total), GetString("Progress total must be greater than or equal to current."));
                }

                lock (gate) {
                    if (snapshot.Completed) {
                        return;
                    }

                    snapshot = snapshot with {
                        Progress = new CommandJobProgressSnapshot(current, total),
                        UpdatedUtc = now,
                    };
                }
            }

            public void ClearProgress(DateTimeOffset now) {
                lock (gate) {
                    if (snapshot.Completed || snapshot.Progress is null) {
                        return;
                    }

                    snapshot = snapshot with {
                        Progress = null,
                        UpdatedUtc = now,
                    };
                }
            }

            public void Dispose() {
                if (Interlocked.Exchange(ref disposed, 1) != 0) {
                    return;
                }

                cancellationSource.Dispose();
            }
        }
    }
}
