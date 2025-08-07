using TShockAPI;
using UnifierTSL.Module;
using UnifierTSL.Module.Dependencies;
[assembly: ModuleDependencies<TShockDependences>]

namespace TShockAPI
{
    public class TShockDependences : IDependencyProvider
    {
        const string Prefix = nameof(TShockAPI) + ".";
        public IReadOnlyList<ModuleDependency> GetDependencies() {
            var asm = typeof(TShockDependences).Assembly;
            return [
                new ManagedEmbeddedDependency(asm, Prefix + "HttpServer.dll"),
                new NugetDependency(asm, "GetText.NET", new Version(8, 0, 5)),
                new NugetDependency(asm, "BCrypt.Net-Next", new Version(4, 0, 3)),
                new NugetDependency(asm, "linq2db", new Version(5, 4, 1)),
                new NugetDependency(asm, "MySql.Data", new Version(9, 2, 0)),
                new NugetDependency(asm, "Microsoft.Data.Sqlite", new Version(9, 0, 0)),
                new NugetDependency(asm, "Npgsql", new Version(9, 0, 3)),
            ];
        }
    }
}
