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

            var task = Run(excludedPlugins);
            task.Wait();
            if (task.IsFaulted) throw task.Exception;
        }

        static async Task Run(IReadOnlyList<string> excludedPlugins) {
            var packages = PackageLayoutManager.CreateSupportPackages();
            await packages.InputAppTools(
                new AppToolsPublisher([
                    "UnifierTSL.ConsoleClient/UnifierTSL.ConsoleClient.csproj",
                ])
                .PublishApps());

            await packages.InputPlugins(
                new PluginsBuilder("Plugins").BuildPlugins(excludedPlugins));

            await packages.InputCoreProgram(
                new CoreAppBuilder("UnifierTSL/UnifierTSL.csproj").Build());
        }
    }
}
