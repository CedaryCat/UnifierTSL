using System.Collections.Immutable;
using System.Reflection;

namespace Atelier.Session.Execution
{
    internal sealed class ExecutionSession : IDisposable
    {
        private readonly ExecutionConfiguration configuration;
        private readonly ManagedPluginAssemblyCatalog managedPluginCatalog;
        private readonly ExecutionLoadContext loadContext;
        private readonly Dictionary<string, Assembly> persistentSubmissionAssemblies = new(StringComparer.OrdinalIgnoreCase);
        private object?[] submissionArray;
        private ImmutableArray<string> attachedManagedPluginKeys = [];
        private int submissionCount;

        public ExecutionSession(
            ExecutionConfiguration configuration,
            ManagedPluginAssemblyCatalog managedPluginCatalog,
            object hostObject) {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.managedPluginCatalog = managedPluginCatalog ?? throw new ArgumentNullException(nameof(managedPluginCatalog));

            submissionArray = [hostObject ?? throw new ArgumentNullException(nameof(hostObject)), null];
            loadContext = new ExecutionLoadContext(
                $"AtelierExecution:{Guid.NewGuid():N}",
                ResolvePersistentAssembly);
            loadContext.RegisterOwnedAssembly(configuration.HostAssembly);
        }

        public ImmutableArray<string> AttachedManagedPluginKeys => attachedManagedPluginKeys;

        public ExecutionLaunchResult LaunchPersistent(
            PreparedSubmission submission,
            ImmutableArray<string> managedPluginKeys,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            attachedManagedPluginKeys = [.. attachedManagedPluginKeys.Union(managedPluginKeys, StringComparer.Ordinal)];
            var executionArray = CloneSubmissionArray();
            var assembly = loadContext.LoadAssembly(submission.PeImage, submission.PdbImage);
            var executionTask = LaunchFactory(assembly, submission, executionArray, cancellationToken);
            if (executionTask.IsCompleted) {
                return CompletePersistentLaunch(assembly, executionArray, executionTask);
            }

            CommitPersistentLaunch(assembly, executionArray);
            return new ExecutionLaunchResult(null, executionTask, true);
        }

        public ExecutionLaunchResult LaunchTransient(
            PreparedSubmission submission,
            ImmutableArray<string> transientManagedPluginKeys,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            var transientContext = new ExecutionLoadContext(
                $"AtelierTransient:{Guid.NewGuid():N}",
                assemblyName => ResolveTransientAssembly(assemblyName, transientManagedPluginKeys));
            var executionArray = CloneSubmissionArray();
            var assembly = transientContext.LoadAssembly(submission.PeImage, submission.PdbImage);
            var executionTask = LaunchFactory(assembly, submission, executionArray, cancellationToken);
            if (executionTask.IsCompleted) {
                try {
                        return new ExecutionLaunchResult(executionTask.GetAwaiter().GetResult(), null, false);
                }
                finally {
                    transientContext.Unload();
                }
            }

                return new ExecutionLaunchResult(
                    null,
                    AwaitTransientCompletionAsync(executionTask, transientContext),
                    false);
        }

        public void Dispose() {
            loadContext.Unload();
        }

        private Assembly? ResolvePersistentAssembly(AssemblyName assemblyName) {
            if (AssemblyName.ReferenceMatchesDefinition(configuration.HostAssembly.GetName(), assemblyName)) {
                return configuration.HostAssembly;
            }

            if (TryResolvePersistentSubmissionAssembly(assemblyName, out var submissionAssembly)) {
                return submissionAssembly;
            }

            if (managedPluginCatalog.TryResolveAttachedAssembly(attachedManagedPluginKeys, assemblyName, out var managedAssembly)) {
                return managedAssembly;
            }

            return ResolveDefaultAssembly(assemblyName);
        }

        private Assembly? ResolveTransientAssembly(AssemblyName assemblyName, ImmutableArray<string> transientManagedPluginKeys) {
            if (TryResolvePersistentSubmissionAssembly(assemblyName, out var submissionAssembly)) {
                return submissionAssembly;
            }

            var attachedKeys = attachedManagedPluginKeys.Length == 0
                ? transientManagedPluginKeys
                : [.. attachedManagedPluginKeys.Union(transientManagedPluginKeys, StringComparer.Ordinal)];
            if (managedPluginCatalog.TryResolveAttachedAssembly(attachedKeys, assemblyName, out var managedAssembly)) {
                return managedAssembly;
            }

            if (AssemblyName.ReferenceMatchesDefinition(configuration.HostAssembly.GetName(), assemblyName)) {
                return configuration.HostAssembly;
            }

            return ResolveDefaultAssembly(assemblyName);
        }

        private bool TryResolvePersistentSubmissionAssembly(AssemblyName assemblyName, out Assembly assembly) {
            if (assemblyName.FullName is { Length: > 0 } fullName
                && persistentSubmissionAssemblies.TryGetValue(fullName, out var resolvedAssembly)) {
                assembly = resolvedAssembly;
                return true;
            }

            foreach (var loadedAssembly in persistentSubmissionAssemblies.Values) {
                if (!AssemblyName.ReferenceMatchesDefinition(loadedAssembly.GetName(), assemblyName)) {
                    continue;
                }

                assembly = loadedAssembly;
                return true;
            }

            assembly = null!;
            return false;
        }

        private static Assembly? ResolveDefaultAssembly(AssemblyName assemblyName) {
            foreach (var loadedAssembly in AppDomain.CurrentDomain.GetAssemblies()) {
                if (AssemblyName.ReferenceMatchesDefinition(loadedAssembly.GetName(), assemblyName)) {
                    return loadedAssembly;
                }
            }

            return null;
        }

        private object?[] CloneSubmissionArray() {
            var targetLength = Math.Max(submissionArray.Length, submissionCount + 2);
            var clone = new object?[targetLength];
            Array.Copy(submissionArray, clone, submissionArray.Length);
            return clone;
        }

        private void CommitPersistentLaunch(Assembly assembly, object?[] executionArray) {
            if (assembly.GetName().FullName is { Length: > 0 } fullName) {
                persistentSubmissionAssemblies[fullName] = assembly;
            }

            loadContext.RegisterOwnedAssembly(assembly);
            submissionArray = executionArray;
            submissionCount++;
        }

        private ExecutionLaunchResult CompletePersistentLaunch(
            Assembly assembly,
            object?[] executionArray,
            Task<object?> executionTask) {
            var result = executionTask.GetAwaiter().GetResult();
            CommitPersistentLaunch(assembly, executionArray);
            return new ExecutionLaunchResult(result, null, true);
        }

        private static async Task<object?> AwaitTransientCompletionAsync(Task<object?> executionTask, ExecutionLoadContext transientContext) {
            try {
                return await executionTask.ConfigureAwait(false);
            }
            finally {
                transientContext.Unload();
            }
        }

        private static Task<object?> LaunchFactory(
            Assembly assembly,
            PreparedSubmission submission,
            object?[] submissionArray,
            CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            var entryType = assembly.GetType(submission.EntryTypeName, throwOnError: true, ignoreCase: false)
                ?? throw new InvalidOperationException(GetString($"Atelier submission entry type '{submission.EntryTypeName}' was not found."));
            var entryMethod = entryType.GetMethod(
                submission.EntryMethodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(object[])],
                modifiers: null)
                ?? throw new InvalidOperationException(GetString($"Atelier submission entry method '{submission.EntryMethodName}' was not found."));
            return (Task<object?>)entryMethod.Invoke(null, [submissionArray])!;
        }
    }

    internal sealed record ExecutionLaunchResult(
        object? ImmediateReturnValue,
        Task<object?>? PendingTask,
        bool StateAccepted);

    internal sealed record PreparedSubmission(
        byte[] PeImage,
        byte[] PdbImage,
        string EntryTypeName,
        string EntryMethodName);
}
