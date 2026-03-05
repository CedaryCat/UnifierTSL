namespace UnifierTSL.CLI.Sessions
{
    internal interface IReadSessionBroker : IDisposable
    {
        string ReadLine();

        ConsoleKeyInfo ReadKey(bool intercept);

        int Read();
    }
}
