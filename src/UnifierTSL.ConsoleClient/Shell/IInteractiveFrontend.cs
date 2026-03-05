namespace UnifierTSL.ConsoleClient.Shell
{
    public interface IInteractiveFrontend : IDisposable
    {
        bool IsInteractive { get; }

        bool SupportsVirtualTerminal { get; }

        void AppendLog(string text, bool isAnsi);

        void SetTransientStatus(
            string summary,
            IReadOnlyList<string>? detailLines = null,
            string spinner = "|",
            int panelHeight = 3);

        void ClearTransientStatus();

        void Clear();

        string ReadLine(
            ReadLineRenderSnapshot? render = null,
            bool trim = false,
            CancellationToken cancellationToken = default,
            Action<ReadLineReactiveState>? onInputStateChanged = null);

        void UpdateReadLineContext(ReadLineRenderSnapshot render);
    }
}
