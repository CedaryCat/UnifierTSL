using NuGet.Versioning;
using System.Collections.Immutable;
using UnifierTSL.Logging;

namespace UnifierTSL.Module.Dependencies
{
    public interface IDependencyLibraryExtractor
    {
        ImmutableArray<LibraryEntry> Extract(RoleLogger logger);
    }
    public readonly record struct LibraryEntry(Lazy<Stream> Stream, DependencyKind Kind, string FilePath, NuGetVersion Version, string LibraryName);
}
