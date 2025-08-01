using System.Threading.Tasks;

namespace UnifierTSL.Publisher
{
    internal class Program
    {
        static void Main(string[] args) {
            var task = Run();
            task.Wait();
            if (task.IsFaulted) throw task.Exception;
        }

        static async Task Run() {
            var package = new PackageLayoutManager();

            await package.InputAppTools(
                await new AppToolsPublisher([
                    "UnifierTSL.ConsoleClient\\UnifierTSL.ConsoleClient.csproj",
                ])
                .PublishApps());

            await package.InputPlugins(
                await new PluginsBuilder("Plugins").BuildPlugins());

            await package.InputCoreProgram(
                await new CoreAppBuilder("UnifierTSL\\UnifierTSL.csproj").Build());
        }
    }
}
