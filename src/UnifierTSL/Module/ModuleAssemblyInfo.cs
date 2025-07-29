using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL.Module.Dependencies;

namespace UnifierTSL.Module
{
    public record ModuleAssemblyInfo(AssemblyLoadContext Context, Assembly Assembly, ImmutableArray<ModuleDependency> Dependencies);
}
