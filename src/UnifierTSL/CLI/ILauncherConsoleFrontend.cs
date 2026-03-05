using UnifierTSL.CLI.Sessions;
using UnifierTSL.Servers;

namespace UnifierTSL.CLI;

public interface ILauncherConsoleFrontend : IDisposable
{
    bool IsInteractive { get; }

    string ReadLine(ReadLineContextSpec contextSpec, bool trim = false);

    string ReadCommandLine(ServerContext? server = null);

    void WriteAnsi(string text);

    void WritePlain(string text);

    IDisposable BeginTimedWorkStatus(string category, string message);
}
