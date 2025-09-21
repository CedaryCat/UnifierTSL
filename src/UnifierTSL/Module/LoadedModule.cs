using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using UnifierTSL.Extensions;
using UnifierTSL.FileSystem;
using UnifierTSL.Module.Dependencies;

namespace UnifierTSL.Module
{
    public record LoadedModule(
        ModuleLoadContext Context,
        Assembly Assembly,
        ImmutableArray<ModuleDependency> Dependencies,
        FileSignature Signature,
        LoadedModule? CoreModule)
    {
        /// <summary>
        /// The modules that depend on this module
        /// </summary>
        public ImmutableArray<LoadedModule> DependentModules => dependentModules;
        private ImmutableArray<LoadedModule> dependentModules = [];
        /// <summary>
        /// The modules that this module depends on
        /// </summary>
        public ImmutableArray<LoadedModule> DependencyModules => dependencyModules;
        private ImmutableArray<LoadedModule> dependencyModules = [];

        public static void Reference(LoadedModule dependency, LoadedModule dependent) {
            if (!dependent.dependencyModules.Contains(dependency)) {
                ImmutableInterlocked.Update(ref dependent.dependencyModules, x => x.Add(dependency));
            }
            if (!dependency.dependentModules.Contains(dependent)) {
                ImmutableInterlocked.Update(ref dependency.dependentModules, x => x.Add(dependent));
            }
        }
        public static void Unreference(LoadedModule dependency, LoadedModule dependent) {
            ImmutableInterlocked.Update(ref dependent.dependencyModules, x => x.Remove(dependency));
            ImmutableInterlocked.Update(ref dependency.dependentModules, x => x.Remove(dependent));
        }

        public bool Unloaded {
            get {
                if (CoreModule is not null) {
                    return CoreModule.Unloaded;
                }
                return unloaded;
            }
        }
        private bool unloaded;
        public void Unload() {
            if (CoreModule is not null) {
                return;
            }
            if (Unloaded) return;

            foreach (LoadedModule dependent in DependentModules) {
                if (dependent.CoreModule == this) {
                    dependent.Unreference();
                }
                else {
                    dependent.Unload();
                }
            }
            Unreference();

            unloaded = true;
            Context.Unload();
        }
        private void Unreference() {
            foreach (LoadedModule dependency in DependencyModules) {
                LoadedModule.Unreference(dependency, this);
            }
            foreach (LoadedModule dependent in DependentModules) {
                LoadedModule.Unreference(this, dependent);
            }
        }
        private IEnumerable<LoadedModule> EnumerateDependentOrder(
            bool includeSelf,
            bool preorder,
            HashSet<LoadedModule>? visited = null) {

            visited ??= [];

            if (!visited.Add(this)) yield break;

            if (includeSelf && preorder) {
                yield return this;
            }

            foreach (LoadedModule dep in DependentModules) {
                foreach (LoadedModule child in dep.EnumerateDependentOrder(includeSelf: true, preorder, visited)) {
                    yield return child;
                }
            }

            if (includeSelf && !preorder) {
                yield return this;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="includeSelf"></param>
        /// <param name="preorder">If true, the dependency modules will be ahead of the dependent modules.</param>
        /// <param name="parent"></param>
        /// <param name="visited"></param>
        /// <returns></returns>
        public ImmutableArray<LoadedModule> GetDependentOrder(bool includeSelf, bool preorder)
            => EnumerateDependentOrder(includeSelf, preorder).ToImmutableArray();

        public bool TryProxyLoad(LoadedModule requester, AssemblyName target, [NotNullWhen(true)] out Assembly? result) {
            result = null;

            if (!requester.Assembly.GetReferencedAssemblies().Any(x => x.Name == Assembly.GetName().Name)) {
                return false;
            }
            string moduleDir = Path.GetDirectoryName(Signature.FilePath)!;
            string matchFile = Path.Combine(moduleDir, "lib", target.Name + ".dll");

            if (!File.Exists(matchFile)) {
                return false;
            }

            Reference(this, requester);

            result = Context.Assemblies.FirstOrDefault(x => x.GetName().Name == target.Name);
            result ??= Context.LoadFromStream(matchFile);
            return true;
        }
    }
}
