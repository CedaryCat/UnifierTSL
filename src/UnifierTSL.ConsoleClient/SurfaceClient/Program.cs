using UnifierTSL.SurfaceClient.Hosting;
using UnifierTSL.SurfaceClient.Transport;

namespace UnifierTSL.SurfaceClient;

internal class Program {
    internal static void Main(string[] args) {
        if (args.Length == 0) {
            Console.WriteLine("Pipe name not specified");
            return;
        }

        string pipeName = args[0];
        using PipeSurfaceClientTransport client = new(pipeName);
        using TerminalSurfaceClientRuntime runtime = new(client);
        runtime.RunAsync().GetAwaiter().GetResult();
    }
}
