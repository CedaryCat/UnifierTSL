namespace UnifierTSL.ConsoleClient.Shell
{
    public readonly record struct LineEditorTransientStatus(
        string Spinner,
        string Summary,
        IReadOnlyList<string> DetailLines,
        int PanelHeight);
}
