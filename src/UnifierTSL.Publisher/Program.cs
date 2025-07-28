using System.Threading.Tasks;

namespace UnifierTSL.Publisher
{
    internal class Program
    {
        static async Task Main(string[] args) {

            var package = new PackageLayoutManager();

            await package.InputAppTools(
                await new AppToolsPublisher([
                    "UnifierTSL.ConsoleClient\\UnifierTSL.ConsoleClient.csproj",
                ])
                .PublishApps());

            await package.InputCoreProgram(
                await new CoreAppBuilder("UnifierTSL\\UnifierTSL.csproj").Build());
        }
    }
}
