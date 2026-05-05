using Microsoft.Xna.Framework;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using TShockAPI.Commanding;
using TShockAPI.DB;
using TShockAPI.Hooks;
using UnifierTSL;
using UnifierTSL.Servers;

namespace TShockAPI
{
    public static class Commands
    {
        public static List<Command> ChatCommands = new List<Command>();
        public static ReadOnlyCollection<Command> TShockCommands { get; private set; } = new ReadOnlyCollection<Command>(new List<Command>());

        /// <summary>
        /// The command specifier, defaults to "/"
        /// </summary>
        public static string Specifier {
            get { return string.IsNullOrWhiteSpace(TShock.Config.GlobalSettings.CommandSpecifier) ? "/" : TShock.Config.GlobalSettings.CommandSpecifier; }
        }

        /// <summary>
        /// The silent command specifier, defaults to "."
        /// </summary>
        public static string SilentSpecifier {
            get { return string.IsNullOrWhiteSpace(TShock.Config.GlobalSettings.CommandSilentSpecifier) ? "." : TShock.Config.GlobalSettings.CommandSilentSpecifier; }
        }

        private delegate void AddChatCommand(string permission, CommandDelegate command, params string[] names);

        public static IReadOnlyList<Command> GetLegacyCommandCatalog()
        {
            if (TShockCommands.Count == 0) {
                TShockCommands = new ReadOnlyCollection<Command>(BuildLegacyCommands());
            }

            return TShockCommands;
        }

        public static IReadOnlyList<Command> GetCommandCatalog()
        {
            return GetLegacyCommandCatalog();
        }

        public static IReadOnlyList<Command> GetRegisteredCommands()
        {
            IReadOnlyList<Command> legacyCommands = GetRegisteredLegacyCommands();
            List<Command> registered = legacyCommands.Count == 0
                ? []
                : [.. legacyCommands];
            foreach (TSCommandCatalogEntry declarativeEntry in TSCommandBridge.GetRegisteredCommandCatalog().Where(static entry => !entry.IsLegacy)) {
                string[] names = [declarativeEntry.PrimaryName, .. declarativeEntry.Aliases];
                Command metadata = declarativeEntry.Permissions.Length > 0
                    ? new Command([.. declarativeEntry.Permissions], static _ => { }, names)
                    : new Command(static _ => { }, names);
                metadata.HelpText = declarativeEntry.HelpText;
                metadata.HelpDesc = declarativeEntry.HelpLines.Length == 0 ? null : [.. declarativeEntry.HelpLines];
                metadata.DoLog = declarativeEntry.DoLog;
                metadata.AllowServer = declarativeEntry.AllowServer;
                metadata.AllowCoord = declarativeEntry.AllowCoord;
                registered.Add(metadata);
            }

            return registered.Count == 0
                ? Array.Empty<Command>()
                : [.. registered];
        }

        public static IReadOnlyList<Command> GetRegisteredLegacyCommands()
        {
            return ChatCommands.Count == 0
                ? Array.Empty<Command>()
                : [.. ChatCommands];
        }

        public static bool IsLegacyCommandRegistered(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName)) {
                return false;
            }

            string normalizedName = commandName.Trim();
            return GetRegisteredLegacyCommands().Any(command => command.HasAlias(normalizedName));
        }

        public static bool RequiresLegacyDispatch(CommandExecutor executor, string? commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName)) {
                return false;
            }

            var normalizedName = commandName.Trim();
            if (IsLegacyCommandRegistered(normalizedName)) {
                return true;
            }

            var player = executor.InGamePlayer;
            return player is not null && player.AwaitingResponse.ContainsKey(normalizedName);
        }

        public static IDisposable InitCommands() {
            IReadOnlyList<Command> tshockCommands = GetLegacyCommandCatalog();
            RegisterLegacyCommands(tshockCommands);
            return new LegacyCommandRegistration(tshockCommands);
        }

        private static List<Command> BuildLegacyCommands() {
            List<Command> tshockCommands = new List<Command>(100);

            return tshockCommands;
        }

        internal static void RegisterCommand(Command command)
        {
            ArgumentNullException.ThrowIfNull(command);
            ChatCommands.Add(command);
        }

        internal static void UnregisterCommand(Command command)
        {
            ArgumentNullException.ThrowIfNull(command);
            ChatCommands.Remove(command);
        }

        private static void RegisterLegacyCommands(IReadOnlyList<Command> commands)
        {
            foreach (Command command in commands) {
                RegisterCommand(command);
            }
        }

        private static void UnregisterLegacyCommands(IReadOnlyList<Command> commands)
        {
            foreach (Command command in commands) {
                UnregisterCommand(command);
            }
        }

        private sealed class LegacyCommandRegistration(IReadOnlyList<Command> commands) : IDisposable
        {
            private int disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0) {
                    return;
                }

                UnregisterLegacyCommands(commands);
            }
        }

        public static bool HandleCommand(CommandExecutor executor, string text) {
            string cmdText = text[1..];
            string cmdPrefix = text[0].ToString();
            bool silent = false;

            if (cmdPrefix == SilentSpecifier)
                silent = true;

            int index = -1;
            for (int i = 0; i < cmdText.Length; i++) {
                if (IsWhiteSpace(cmdText[i])) {
                    index = i;
                    break;
                }
            }
            string cmdName;
            if (index == 0) // Space after the command specifier should not be supported
            {
                executor.SendErrorMessage(GetString("You entered a space after {0} instead of a command. Type {0}help for a list of valid commands.", Specifier));
                return true;
            }
            else if (index < 0)
                cmdName = cmdText.ToLowerInvariant();
            else
                cmdName = cmdText.Substring(0, index).ToLowerInvariant();

            List<string> args;
            if (index < 0)
                args = new List<string>();
            else
                args = ParseParameters(cmdText.Substring(index));

            IEnumerable<Command> cmds = ChatCommands.FindAll(c => c.HasAlias(cmdName));

            var player = executor.InGamePlayer;

            if (player is not null && Hooks.PlayerHooks.OnPlayerCommand(player, cmdName, cmdText, args, ref cmds, cmdPrefix))
                return true;

            if (!cmds.Any()) {
                if (player is not null && player.AwaitingResponse.ContainsKey(cmdName)) {
                    Action<CommandArgs> call = player.AwaitingResponse[cmdName];
                    player.AwaitingResponse.Remove(cmdName);
                    call(new CommandArgs(cmdText, executor, args));
                    return true;
                }
                executor.SendErrorMessage(GetString("Invalid command entered. Type {0}help for a list of valid commands.", Specifier));
                return true;
            }
            foreach (Command cmd in cmds) {
                if (!cmd.CanRun(executor)) {
                    if (cmd.DoLog)
                        executor.SendLogs(GetString("{0} tried to execute {1}{2}.", executor.Name, Specifier, cmdText), Color.PaleVioletRed, player);
                    else
                        executor.SendLogs(GetString("{0} tried to execute (args omitted) {1}{2}.", executor.Name, Specifier, cmdName), Color.PaleVioletRed, player);
                    executor.SendErrorMessage(GetString("You do not have access to this command."));
                    if (executor.HasPermission(Permissions.su)) {
                        executor.SendInfoMessage(GetString("You can use '{0}sudo {0}{1}' to override this check.", Specifier, cmdText));
                    }
                }
                else if (!cmd.AllowServer && !executor.IsClient) {
                    executor.SendErrorMessage(GetString("You must use this command in-game."));
                }
                else if (!cmd.AllowCoord && executor.SourceServer is null) {
                    executor.SendErrorMessage(GetString("You must use this command in sepcific server."));
                }
                else {
                    if (cmd.DoLog)
                        executor.SendLogs(GetString("{0} executed: {1}{2}.", executor.Name, silent ? SilentSpecifier : Specifier, cmdText), Color.PaleVioletRed, player);
                    else
                        executor.SendLogs(GetString("{0} executed (args omitted): {1}{2}.", executor.Name, silent ? SilentSpecifier : Specifier, cmdName), Color.PaleVioletRed, player);

                    CommandArgs arguments = new(cmdText, silent, executor, args);
                    bool handled = PlayerHooks.OnPrePlayerCommand(cmd, ref arguments);
                    if (!handled)
                        cmd.Run(arguments);
                    PlayerHooks.OnPostPlayerCommand(cmd, arguments, handled);
                }
            }
            return true;
        }

        /// <summary>
        /// Parses a string of parameters into a list. Handles quotes.
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        private static List<String> ParseParameters(string str) {
            var ret = new List<string>();
            var sb = new StringBuilder();
            bool instr = false;
            for (int i = 0; i < str.Length; i++) {
                char c = str[i];

                if (c == '\\' && ++i < str.Length) {
                    if (str[i] != '"' && str[i] != ' ' && str[i] != '\\')
                        sb.Append('\\');
                    sb.Append(str[i]);
                }
                else if (c == '"') {
                    instr = !instr;
                    if (!instr) {
                        ret.Add(sb.ToString());
                        sb.Clear();
                    }
                    else if (sb.Length > 0) {
                        ret.Add(sb.ToString());
                        sb.Clear();
                    }
                }
                else if (IsWhiteSpace(c) && !instr) {
                    if (sb.Length > 0) {
                        ret.Add(sb.ToString());
                        sb.Clear();
                    }
                }
                else
                    sb.Append(c);
            }
            if (sb.Length > 0)
                ret.Add(sb.ToString());

            return ret;
        }

        private static bool IsWhiteSpace(char c) {
            return c == ' ' || c == '\t' || c == '\n';
        }

        #region Cause Events and Spawn Monsters Commands

        #endregion Cause Events and Spawn Monsters Commands


        #region Server Config Commands

        #endregion Server Config Commands

        #region Time/PvpFun Commands

        #endregion Time/PvpFun Commands

        #region World Protection Commands

        #endregion World Protection Commands

        #region Game Commands

        #endregion Game Commands

        public static bool TryParsePageNumber(List<string> commandParameters, int expectedParameterIndex, CommandExecutor errorMessageReceiver, out int pageNumber) {
            pageNumber = 1;
            if (commandParameters.Count <= expectedParameterIndex)
                return true;

            string pageNumberRaw = commandParameters[expectedParameterIndex];
            if (!int.TryParse(pageNumberRaw, out pageNumber) || pageNumber < 1) {
                errorMessageReceiver.SendErrorMessage(GetString("\"{0}\" is not a valid page number.", pageNumberRaw));

                pageNumber = 1;
                return false;
            }

            return true;
        }
    }
}
