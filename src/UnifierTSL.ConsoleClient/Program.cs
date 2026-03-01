namespace UnifierTSL.ConsoleClient
{

    public class Program
    {
        public static void Main(string[] args) {
            if (args.Length == 0) {
                Console.WriteLine("Pipe name not specified");
                return;
            }

            var pipeName = args[0];
            using var client = new ConsoleClient(pipeName);

            ConsoleClientLogic.Run(client).Wait();
        }
    }
}
