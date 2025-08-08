using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace UnifierTSL.Publisher
{
    internal class Program
    {
        static void Main(string[] args) {
            var options = CLIHelper.ParseArguements(args);
            if (options.TryGetValue("--excluded-plugins", out var excludedPlugins)) {
                excludedPlugins = [.. excludedPlugins
                    .Select(p => p.Split(',' , StringSplitOptions.RemoveEmptyEntries))
                    .SelectMany(p => p)
                    .Select(p => p.Trim())];
            }
            excludedPlugins ??= [];

            var task = Run(RuntimeInformation.RuntimeIdentifier, excludedPlugins);
            task.Wait();
            if (task.IsFaulted) throw task.Exception;
        }

        static async Task Run(string rid, IReadOnlyList<string> excludedPlugins) {
            var package = new PackageLayoutManager(rid);

            await package.InputAppTools(
                new AppToolsPublisher([
                    "UnifierTSL.ConsoleClient\\UnifierTSL.ConsoleClient.csproj",
                ])
                .PublishApps(rid));

            await package.InputPlugins(
                new PluginsBuilder("Plugins").BuildPlugins(rid, excludedPlugins));

            await package.InputCoreProgram(
                new CoreAppBuilder("UnifierTSL\\UnifierTSL.csproj").Build(rid));
        }
    }
}
