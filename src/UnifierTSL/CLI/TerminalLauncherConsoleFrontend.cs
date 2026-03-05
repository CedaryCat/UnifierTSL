using System.Diagnostics;
using UnifierTSL.CLI.Sessions;
using UnifierTSL.ConsoleClient.Shell;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI;

public sealed class TerminalLauncherConsoleFrontend : ILauncherConsoleFrontend
{
    private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];
    private readonly IInteractiveFrontend shell = new ConsoleShell();
    private readonly Lock scopeSync = new();
    private readonly HashSet<TimedWorkStatusScope> activeScopes = [];
    private int disposed;

    public bool IsInteractive => shell.IsInteractive;

    public string ReadLine(ReadLineContextSpec contextSpec, bool trim = false)
    {
        ArgumentNullException.ThrowIfNull(contextSpec);
        ReadLineMaterializationScenario scenario = ResolveShellScenario();
        IReadLineSemanticProvider provider = ConsoleCommandHintRegistry.CreateProvider(contextSpec, scenario, scenario);
        return ReadLineWithProvider(provider, trim);
    }

    public string ReadCommandLine(ServerContext? server = null)
    {
        ReadLineContextSpec contextSpec = ConsoleCommandHintRegistry.CreateCommandLineContextSpec(server);
        ReadLineMaterializationScenario scenario = ResolveShellScenario();
        IReadLineSemanticProvider provider = ConsoleCommandHintRegistry.CreateProvider(contextSpec, scenario, scenario);
        return ReadLineWithProvider(provider, trim: true);
    }

    public void WriteAnsi(string text)
    {
        shell.AppendLog(text, isAnsi: true);
    }

    public void WritePlain(string text)
    {
        shell.AppendLog(text, isAnsi: false);
    }

    public IDisposable BeginTimedWorkStatus(string category, string message)
    {
        if (Volatile.Read(ref disposed) != 0 || !shell.IsInteractive) {
            return NoopDisposable.Instance;
        }

        TimedWorkStatusScope scope = new(this, shell, category, message);
        if (!TryRegisterScope(scope)) {
            scope.Dispose();
            return NoopDisposable.Instance;
        }

        scope.Start();
        return scope;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) {
            return;
        }

        TimedWorkStatusScope[] scopes;
        lock (scopeSync) {
            scopes = [.. activeScopes];
            activeScopes.Clear();
        }

        foreach (TimedWorkStatusScope scope in scopes) {
            scope.Dispose();
        }

        shell.Dispose();
    }

    private string ReadLineWithProvider(IReadLineSemanticProvider provider, bool trim)
    {
        ArgumentNullException.ThrowIfNull(provider);

        ReadLineSemanticSnapshot initialSemantic = provider.BuildInitial();
        ReadLineRenderSnapshot initialRender = SemanticToRenderMapper.Map(initialSemantic);

        string line = shell.ReadLine(
            initialRender,
            trim: false,
            onInputStateChanged: state => {
                ReadLineSemanticSnapshot updated = provider.BuildReactive(state);
                shell.UpdateReadLineContext(SemanticToRenderMapper.Map(updated));
            });

        return trim ? line.Trim() : line;
    }

    private ReadLineMaterializationScenario ResolveShellScenario()
    {
        return shell.IsInteractive
            ? ReadLineMaterializationScenario.LocalInteractive
            : ReadLineMaterializationScenario.NonInteractiveFallback;
    }

    private bool TryRegisterScope(TimedWorkStatusScope scope)
    {
        lock (scopeSync) {
            if (Volatile.Read(ref disposed) != 0) {
                return false;
            }

            activeScopes.Add(scope);
            return true;
        }
    }

    private void UnregisterScope(TimedWorkStatusScope scope)
    {
        lock (scopeSync) {
            activeScopes.Remove(scope);
        }
    }

    private sealed class TimedWorkStatusScope : IDisposable
    {
        private readonly object sync = new();
        private readonly TerminalLauncherConsoleFrontend owner;
        private readonly IInteractiveFrontend shell;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly string category;
        private readonly string message;
        private Timer? timer;

        private int frameIndex;
        private int disposed;

        public TimedWorkStatusScope(TerminalLauncherConsoleFrontend owner, IInteractiveFrontend shell, string category, string message)
        {
            this.owner = owner;
            this.shell = shell;
            this.category = string.IsNullOrWhiteSpace(category) ? "Work" : category.Trim();
            this.message = string.IsNullOrWhiteSpace(message) ? "Running..." : message.Trim();
        }

        public void Start()
        {
            PublishFrame();
            lock (sync) {
                if (Volatile.Read(ref disposed) != 0) {
                    return;
                }
                timer = new Timer(static state => ((TimedWorkStatusScope)state!).PublishFrame(), this, 120, 120);
            }
        }

        public void Dispose()
        {
            DisposeCore(clearTransientStatus: true);
        }

        private void PublishFrame()
        {
            bool frontendDisposed = false;
            lock (sync) {
                if (Volatile.Read(ref disposed) != 0) {
                    return;
                }

                int index = Interlocked.Increment(ref frameIndex);
                string spinner = SpinnerFrames[index % SpinnerFrames.Length];
                string summary = $"TimedWork:{category}";
                long elapsedMs = stopwatch.ElapsedMilliseconds;

                List<string> details = [
                    $"task: {message}",
                    $"elapsed: {elapsedMs:0} ms"
                ];

                try {
                    shell.SetTransientStatus(summary, details, spinner);
                }
                catch (ObjectDisposedException) {
                    frontendDisposed = true;
                }
            }

            if (frontendDisposed) {
                DisposeCore(clearTransientStatus: false);
            }
        }

        private void DisposeCore(bool clearTransientStatus)
        {
            Timer? localTimer = null;
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }

            lock (sync) {
                localTimer = timer;
                timer = null;
                stopwatch.Stop();
            }

            try {
                localTimer?.Dispose();
            }
            catch {
            }

            owner.UnregisterScope(this);

            if (!clearTransientStatus) {
                return;
            }

            try {
                shell.ClearTransientStatus();
            }
            catch (ObjectDisposedException) {
            }
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
