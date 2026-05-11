using System.Text;

namespace UnifierTSL.Terminal.Shell;

public interface IConsoleInterceptionBridge
{
    TextReader OriginalIn { get; }

    TextWriter OriginalOut { get; }

    Encoding OriginalOutputEncoding { get; }

    IDisposable BeginRawAccess();
}
