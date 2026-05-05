using System.Reflection;
using System.Text;
using MonoMod.RuntimeDetour;
using UnifierTSL.Surface.Activities;
using UnifierTSL.Surface;
using UnifierTSL.Surface.Prompting;
using UnifierTSL.Surface.Prompting.Model;
using UnifierTSL.Surface.Status;
using UnifierTSL.Terminal.Shell;

namespace UnifierTSL.Surface.Hosting;

internal static class LauncherSurfaceConsole
{
    private static readonly SemaphoreSlim readSync = new(1, 1);
    private static readonly AsyncLocal<int> rawAccessDepth = new();
    private static readonly TextReader originalIn = Console.In;
    private static readonly TextWriter originalOut = Console.Out;
    private static readonly Encoding originalOutputEncoding = Console.OutputEncoding;
    private static readonly IConsoleInterceptionBridge interceptionBridge = new ConsoleInterceptionBridge();
    private static ILauncherSurfaceHost? host;
    private static int initialized;

    public static void Initialize(ILauncherSurfaceHost bootstrapHost) {
        ArgumentNullException.ThrowIfNull(bootstrapHost);
        if (Interlocked.CompareExchange(ref initialized, 1, 0) != 0) {
            ConfigureHost(bootstrapHost);
            return;
        }

        host = bootstrapHost;
        Console.SetIn(TextReader.Synchronized(new LegacyConsoleReader()));
        Console.SetOut(TextWriter.Synchronized(new LegacyConsoleWriter()));
        Console.SetError(TextWriter.Synchronized(new LegacyConsoleWriter()));
        SurfaceRuntimeOptions.SetRootActivityActiveProvider(HasActiveSurfaceActivity);
        RefreshAppearanceSettings();
    }

    internal static IConsoleInterceptionBridge InterceptionBridge => interceptionBridge;

    internal static bool IsRawAccessActive => rawAccessDepth.Value != 0;

    internal static Encoding OriginalOutputEncoding => originalOutputEncoding;

    public static bool IsInteractive => CurrentHost.IsInteractive;

    public static bool UseColorfulStatus => SurfaceRuntimeOptions.UseColorfulStatus;

    internal static string? ReadOriginalLine() {
        return originalIn.ReadLine();
    }

    internal static void WriteOriginal(string? value) {
        originalOut.Write(value);
    }

    public static void ConfigureHost(ILauncherSurfaceHost value) {
        ArgumentNullException.ThrowIfNull(value);

        readSync.Wait();
        try {
            var previous = CurrentHost;
            host = value;
            if (!ReferenceEquals(previous, value)) {
                previous.Dispose();
            }
            RefreshAppearanceSettings();
        }
        finally {
            readSync.Release();
        }
    }

    public static string ReadLine(PromptSurfaceSpec contextSpec, bool trim = false) {
        readSync.Wait();
        try {
            return CurrentHost.ReadLine(contextSpec, trim: trim);
        }
        finally {
            readSync.Release();
        }
    }

    public static string ReadLine(string? prompt = null, bool trim = false) {
        var contextSpec = PromptSurfaceSpec.CreatePlain(prompt);
        return ReadLine(contextSpec, trim);
    }

    public static ConsoleKeyInfo ReadKey(bool intercept = false) {
        readSync.Wait();
        try {
            return CurrentHost.ReadKey(intercept);
        }
        finally {
            readSync.Release();
        }
    }

    public static bool IsKeyAvailable() {
        return CurrentHost.IsKeyAvailable();
    }

    public static void Write(string? text) {
        if (string.IsNullOrEmpty(text)) {
            return;
        }

        CurrentHost.Write(text);
    }

    public static void WriteLine(string? text = null) {
        CurrentHost.Write((text ?? string.Empty) + Environment.NewLine);
    }

    public static void WriteAnsi(string text) {
        CurrentHost.WriteAnsi(text);
    }

    public static IDisposable BeginSurfaceActivityStatus(string category, string message) {
        return CurrentHost.BeginSurfaceActivityStatus(category, message);
    }

    public static SurfaceActivityScope BeginSurfaceActivityScope(
        string category,
        string message,
        ActivityDisplayOptions display = default,
        CancellationToken cancellationToken = default) {
        var current = CurrentHost;
        if (current is ITrackedLauncherSurfaceActivityHost trackedHost) {
            var activity = trackedHost.BeginTrackedSurfaceActivityStatus(
                category,
                message,
                display,
                cancellationToken);
            return new SurfaceActivityScope(activity, activity, cancellationToken);
        }

        var scope = current.BeginSurfaceActivityStatus(category, message);
        return new SurfaceActivityScope(activity: null, scope, cancellationToken);
    }

    internal static bool HasActiveSurfaceActivity() {
        var current = host;
        return current is ITrackedLauncherSurfaceActivityHost trackedHost
            && trackedHost.HasActiveSurfaceActivity;
    }

    public static void RefreshAppearanceSettings() {
        CurrentHost.RefreshAppearanceSettings();
    }

    public static void ApplyRuntimeSettings(LauncherRuntimeSettings settings, bool notify) {
        ApplyRuntimeSettings(settings.ConsoleStatus, settings.ColorfulConsoleStatus, notify);
    }

    public static void ApplyRuntimeSettings(StatusProjectionSettings settings, bool colorfulStatus, bool notify) {
        if (!SurfaceRuntimeOptions.ApplyStatusSettings(settings, colorfulStatus)) {
            return;
        }

        RefreshAppearanceSettings();

        if (!notify) {
            return;
        }

        SurfaceRuntimeOptions.NotifyStatusAppearanceChanged();
    }

    private static ConsoleKeyInfo HandleReadKey(OrigReadKey orig) {
        if (IsRawAccessActive) {
            return orig();
        }

        return ReadKey();
    }

    private static ConsoleKeyInfo HandleReadKey(OrigReadKeyIntercept orig, bool intercept) {
        if (IsRawAccessActive) {
            return orig(intercept);
        }

        return ReadKey(intercept);
    }

    private static bool HandleKeyAvailable(OrigKeyAvailable orig) {
        if (IsRawAccessActive) {
            return orig();
        }

        return IsKeyAvailable();
    }

    private static RawAccessScope BeginRawAccess() {
        rawAccessDepth.Value++;
        return new();
    }

    private static ILauncherSurfaceHost CurrentHost => host
        ?? throw new InvalidOperationException(GetString($"{nameof(LauncherSurfaceConsole)} was not initialized."));

    private delegate ConsoleKeyInfo OrigReadKey();
    private delegate ConsoleKeyInfo OrigReadKeyIntercept(bool intercept);
    private delegate bool OrigKeyAvailable();

    private sealed class ConsoleInterceptionBridge : IConsoleInterceptionBridge
    {
        public TextReader OriginalIn => originalIn;

        public TextWriter OriginalOut => originalOut;

        public Encoding OriginalOutputEncoding => originalOutputEncoding;

        public IDisposable BeginRawAccess() {
            return LauncherSurfaceConsole.BeginRawAccess();
        }
    }

    private sealed class LegacyConsoleHooks : IDisposable
    {
        private static readonly MethodInfo ReadKeyMethod = typeof(Console).GetMethod(nameof(Console.ReadKey), Type.EmptyTypes)
            ?? throw new MissingMethodException(typeof(Console).FullName, nameof(Console.ReadKey));
        private static readonly MethodInfo ReadKeyInterceptMethod = typeof(Console).GetMethod(nameof(Console.ReadKey), [typeof(bool)])
            ?? throw new MissingMethodException(typeof(Console).FullName, $"{nameof(Console.ReadKey)}(bool)");
        private static readonly MethodInfo KeyAvailableMethod = typeof(Console).GetProperty(nameof(Console.KeyAvailable), BindingFlags.Public | BindingFlags.Static)?.GetMethod
            ?? throw new MissingMethodException(typeof(Console).FullName, $"get_{nameof(Console.KeyAvailable)}");
        private static readonly MethodInfo HandleReadKeyMethod = GetRequiredMethod(nameof(HandleReadKey), [typeof(OrigReadKey)]);
        private static readonly MethodInfo HandleReadKeyInterceptMethod = GetRequiredMethod(nameof(HandleReadKey), [typeof(OrigReadKeyIntercept), typeof(bool)]);
        private static readonly MethodInfo HandleKeyAvailableMethod = typeof(LauncherSurfaceConsole).GetMethod(nameof(HandleKeyAvailable), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(LauncherSurfaceConsole).FullName, nameof(HandleKeyAvailable));

        private readonly Hook readKeyHook = new(ReadKeyMethod, HandleReadKeyMethod);
        private readonly Hook readKeyInterceptHook = new(ReadKeyInterceptMethod, HandleReadKeyInterceptMethod);
        private readonly Hook keyAvailableHook = new(KeyAvailableMethod, HandleKeyAvailableMethod);

        public void Dispose() {
            keyAvailableHook.Dispose();
            readKeyInterceptHook.Dispose();
            readKeyHook.Dispose();
        }

        private static MethodInfo GetRequiredMethod(string name, Type[] parameterTypes) {
            return typeof(LauncherSurfaceConsole).GetMethod(
                       name,
                       BindingFlags.NonPublic | BindingFlags.Static,
                       binder: null,
                       parameterTypes,
                       modifiers: null)
                   ?? throw new MissingMethodException(typeof(LauncherSurfaceConsole).FullName, name);
        }
    }

    private sealed class LegacyConsoleWriter : TextWriter
    {
        public override Encoding Encoding => OriginalOutputEncoding;

        public override void Write(char value) {
            LauncherSurfaceConsole.Write(value.ToString());
        }

        public override void Write(char[] buffer, int index, int count) {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (index + count > buffer.Length) {
                throw new ArgumentException(GetString("Buffer range exceeds the available data."), nameof(count));
            }
            if (count == 0) {
                return;
            }

            LauncherSurfaceConsole.Write(new string(buffer, index, count));
        }

        public override void Write(string? value) {
            LauncherSurfaceConsole.Write(value);
        }

        public override void WriteLine() {
            LauncherSurfaceConsole.WriteLine();
        }

        public override void WriteLine(string? value) {
            LauncherSurfaceConsole.WriteLine(value);
        }
    }

    private sealed class LegacyConsoleReader : TextReader
    {
        private readonly Lock sync = new();
        private readonly Queue<char> bufferedChars = [];

        public override int Peek() {
            lock (sync) {
                return EnsureBufferedChars() && bufferedChars.TryPeek(out var ch) ? ch : -1;
            }
        }

        public override int Read() {
            lock (sync) {
                return EnsureBufferedChars() ? bufferedChars.Dequeue() : -1;
            }
        }

        public override int Read(char[] buffer, int index, int count) {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (index + count > buffer.Length) {
                throw new ArgumentException(GetString("Buffer range exceeds the available data."), nameof(count));
            }
            if (count == 0) {
                return 0;
            }

            lock (sync) {
                var read = 0;
                while (read < count && EnsureBufferedChars()) {
                    buffer[index + read] = bufferedChars.Dequeue();
                    read++;
                }

                return read;
            }
        }

        public override string? ReadLine() {
            lock (sync) {
                if (bufferedChars.Count == 0) {
                    return LauncherSurfaceConsole.ReadLine(trim: false);
                }

                var builder = new StringBuilder();
                while (EnsureBufferedChars()) {
                    var ch = bufferedChars.Dequeue();
                    if (ch == '\r') {
                        if (bufferedChars.TryPeek(out var next) && next == '\n') {
                            bufferedChars.Dequeue();
                        }
                        break;
                    }
                    if (ch == '\n') {
                        break;
                    }

                    builder.Append(ch);
                }

                return builder.ToString();
            }
        }

        private bool EnsureBufferedChars() {
            if (bufferedChars.Count != 0) {
                return true;
            }

            var line = LauncherSurfaceConsole.ReadLine(trim: false);
            foreach (var ch in line) {
                bufferedChars.Enqueue(ch);
            }
            foreach (var ch in Environment.NewLine) {
                bufferedChars.Enqueue(ch);
            }

            return bufferedChars.Count != 0;
        }
    }

    private readonly struct RawAccessScope : IDisposable
    {
        public void Dispose() {
            rawAccessDepth.Value = Math.Max(0, rawAccessDepth.Value - 1);
        }
    }
}
