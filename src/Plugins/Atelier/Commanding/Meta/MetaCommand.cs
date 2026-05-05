namespace Atelier.Commanding.Meta
{
    internal enum MetaCommandKind : byte
    {
        Unknown,
        Help,
        Reset,
        Clear,
        Imports,
        Target,
        Paste,
        Transient,
    }

    internal sealed record MetaCommand(
        MetaCommandKind Kind,
        string Name,
        string HeaderLine,
        string HeaderRemainder,
        string BodyText,
        string TransientCode);

    internal sealed record MetaCommandInfo(
        MetaCommandKind Kind,
        string Name,
        string Arguments,
        string Summary,
        MetaCommandArgumentHint[] ArgumentHints);

    internal sealed record MetaCommandArgumentHint(
        int ArgumentIndex,
        string Value,
        string Summary);

    internal static class MetaCommands {
        private static readonly MetaCommandInfo[] Items = [
            new(MetaCommandKind.Help, "help", string.Empty, "Show REPL usage guidance.", []),
            new(MetaCommandKind.Reset, "reset", string.Empty, "Reset session runtime state and draft.", []),
            new(MetaCommandKind.Clear, "clear", string.Empty, "Clear transcript output and redraw current frame.", []),
            new(MetaCommandKind.Imports, "imports", string.Empty, "Show baseline/effective imports and reference paths.", []),
            new(MetaCommandKind.Target, "target", string.Empty, "Show current target and host.", []),
            new(MetaCommandKind.Paste, "paste", "[on|off]", "Use Ctrl+Enter as submit for the next pasted source block.", [
                new(0, "on", "Enable paste mode for the next submit."),
                new(0, "off", "Disable paste mode and restore smart submit."),
            ]),
            new(MetaCommandKind.Transient, "transient", "<code>", "Run transient code without committing.", []),
        ];

        public static IReadOnlyList<MetaCommandInfo> All => Items;

        public static bool TryResolve(string commandName, out MetaCommandInfo command) {
            var name = commandName.Trim();
            foreach (var candidate in Items) {
                if (!string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                command = candidate;
                return true;
            }

            command = default!;
            return false;
        }

        public static MetaCommandKind ResolveKind(string commandName) {
            return TryResolve(commandName, out var command) ? command.Kind : MetaCommandKind.Unknown;
        }
    }
}
