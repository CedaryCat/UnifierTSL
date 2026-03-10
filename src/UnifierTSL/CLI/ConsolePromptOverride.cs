using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnifiedServerProcess;
using UnifierTSL.CLI.Prompting;

namespace UnifierTSL.CLI;

public static class ConsolePromptOverride
{
    private sealed class OverrideEntry(
        ConsoleSystemContext console,
        ConsolePromptOverrideKey key,
        ConsolePromptSpec prompt,
        OverrideEntry? next)
    {
        public ConsoleSystemContext Console { get; } = console;

        public ConsolePromptOverrideKey Key { get; } = key;

        public ConsolePromptSpec Prompt { get; } = prompt;

        public OverrideEntry? Next { get; } = next;
    }

    private sealed class ResolverRegistrationHandle(long registrationId) : IDisposable
    {
        private long currentRegistrationId = registrationId;

        public void Dispose() {
            long id = Interlocked.Exchange(ref currentRegistrationId, 0);
            if (id == 0) {
                return;
            }

            scopeResolvers.TryRemove(id, out _);
        }
    }

    private static readonly ConcurrentDictionary<long, Func<ConsoleSystemContext, ConsolePromptOverrideKey, ConsolePromptSpec?>> scopeResolvers = new();
    private static readonly AsyncLocal<OverrideEntry?> oneShotOverrides = new();
    private static readonly AsyncLocal<OverrideEntry?> persistentOverrides = new();
    private static long nextResolverId;

/// <summary>
/// Compatibility bridge for ILHook-oriented prompt overrides.
/// </summary>
    public static string? ReadLineOverride(ConsoleSystemContext console, string scope) {
        ConsolePromptSpec prompt = ResolvePrompt(console, ConsolePromptOverrideKey.Parse(scope));
        return ConsolePromptInput.ReadLine(console, prompt);
    }

    /// <summary>
    /// Queues a one-shot prompt override for the next ordinary ReadLine call on the given console instance.
    /// This API is intended for ILHook/internal adaptation. Regular prompt-aware code should call ConsolePromptInput.ReadLine instead.
    /// </summary>
    public static void BeginOneShotReadLineOverride(ConsoleSystemContext console, string scope) {
        var key = ConsolePromptOverrideKey.Parse(scope);
        ConsolePromptSpec prompt = ResolvePrompt(console, key);
        oneShotOverrides.Value = new OverrideEntry(console, key, prompt, oneShotOverrides.Value);
    }

    /// <summary>
    /// Queues a persistent prompt override for ordinary ReadLine calls until EndReadLineOverride is called.
    /// This API is intended for ILHook/internal adaptation. Regular prompt-aware code should call ConsolePromptInput.ReadLine instead.
    /// </summary>
    public static void BeginReadLineOverride(ConsoleSystemContext console, string scope) {
        var key = ConsolePromptOverrideKey.Parse(scope);
        ConsolePromptSpec prompt = ResolvePrompt(console, key);
        persistentOverrides.Value = new OverrideEntry(console, key, prompt, persistentOverrides.Value);
    }

    /// <summary>
    /// Ends a previously queued persistent prompt override for the given console/scope pair.
    /// This API is intended for ILHook/internal adaptation. Regular prompt-aware code should call ConsolePromptInput.ReadLine instead.
    /// </summary>
    public static void EndReadLineOverride(ConsoleSystemContext console, string scope) {
        ArgumentNullException.ThrowIfNull(console);
        var key = ConsolePromptOverrideKey.Parse(scope);
        OverrideEntry? head = persistentOverrides.Value;
        if (!TryRemoveMatchingOverride(head, console, key, out _, out OverrideEntry? nextHead)) {
            throw new InvalidOperationException(
                $"No active persistent console prompt override matched scope '{key}' for console type '{console.GetType().FullName}'.");
        }

        persistentOverrides.Value = nextHead;
    }

    /// <summary>
    /// Registers a scope resolver for ILHook-oriented prompt overrides.
    /// The returned handle removes the resolver when disposed.
    /// </summary>
    public static IDisposable RegisterScopeResolver(Func<ConsoleSystemContext, ConsolePromptOverrideKey, ConsolePromptSpec?> resolver) {
        ArgumentNullException.ThrowIfNull(resolver);
        long id = Interlocked.Increment(ref nextResolverId);
        scopeResolvers[id] = resolver;
        return new ResolverRegistrationHandle(id);
    }

    internal static bool TryResolvePendingReadLineOverride(
        ConsoleSystemContext console,
        [NotNullWhen(true)] out ConsolePromptSpec? prompt) {
        ArgumentNullException.ThrowIfNull(console);
        prompt = null;

        OverrideEntry? oneShotHead = oneShotOverrides.Value;
        if (TryRemoveMatchingOverride(oneShotHead, console, key: null, out prompt, out OverrideEntry? nextOneShotHead)) {
            oneShotOverrides.Value = nextOneShotHead;
            return true;
        }

        return TryFindMatchingOverride(persistentOverrides.Value, console, out prompt);
    }

    private static ConsolePromptSpec ResolvePrompt(ConsoleSystemContext console, ConsolePromptOverrideKey key) {
        ConsolePromptInput.ResolveRemoteConsole(console);

        foreach ((long _, Func<ConsoleSystemContext, ConsolePromptOverrideKey, ConsolePromptSpec?> resolver) in scopeResolvers
            .OrderByDescending(static pair => pair.Key)) {
            ConsolePromptSpec? prompt = resolver(console, key);
            if (prompt is not null) {
                return prompt;
            }
        }

        throw new InvalidOperationException($"No console prompt override is registered for scope '{key}'.");
    }

    private static bool TryFindMatchingOverride(
        OverrideEntry? current,
        ConsoleSystemContext console,
        [NotNullWhen(true)] out ConsolePromptSpec? prompt) {
        if (current is null) {
            prompt = null;
            return false;
        }

        if (ReferenceEquals(current.Console, console)) {
            prompt = current.Prompt;
            return true;
        }

        return TryFindMatchingOverride(current.Next, console, out prompt);
    }

    private static bool TryRemoveMatchingOverride(
        OverrideEntry? current,
        ConsoleSystemContext console,
        ConsolePromptOverrideKey? key,
        [NotNullWhen(true)] out ConsolePromptSpec? prompt,
        out OverrideEntry? next) {
        if (current is null) {
            prompt = null;
            next = null;
            return false;
        }

        if (ReferenceEquals(current.Console, console)
            && (!key.HasValue || current.Key.Equals(key.Value))) {
            prompt = current.Prompt;
            next = current.Next;
            return true;
        }

        if (!TryRemoveMatchingOverride(current.Next, console, key, out prompt, out OverrideEntry? nextTail)) {
            next = current;
            return false;
        }

        next = ReferenceEquals(nextTail, current.Next)
            ? current
            : new OverrideEntry(current.Console, current.Key, current.Prompt, nextTail);
        return true;
    }
}
