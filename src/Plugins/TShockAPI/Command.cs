using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using UnifierTSL.Events.Handlers;
using UnifierTSL.Servers;

namespace TShockAPI
{
    public delegate void CommandDelegate(CommandArgs args);

    public class CommandArgs : EventArgs
    {
        public readonly CommandExecutor Executor;
        public ServerContext? Server => Executor.SourceServer;
        public string Message { get; private set; }
        [MemberNotNull(nameof(Server))]
        public TSPlayer? Player { get; private set; }
        public bool Silent { get; private set; }

        /// <summary>
        /// Parameters passed to the argument. Does not include the command name.
        /// IE '/kick "jerk face"' will only have 1 argument
        /// </summary>
        public List<string> Parameters { get; private set; }

        public Player? TPlayer {
            get { return Player?.TPlayer; }
        }

        public CommandArgs(string message, CommandExecutor executor, List<string> args) {
            Message = message;
            Executor = executor;
            Player = executor.Player;
            Parameters = args;
            Silent = false;
        }

        public CommandArgs(string message, bool silent, CommandExecutor sender, List<string> args) {
            Message = message;
            Executor = sender;
            Player = sender.Player;
            Parameters = args;
            Silent = silent;
        }
    }

    public class Command
    {
        public bool AllowCoord { get; set; }
        /// <summary>
        /// Gets or sets whether to allow non-players to use this command.
        /// </summary>
        public bool AllowServer { get; set; }
        /// <summary>
        /// Gets or sets whether to do logging of this command.
        /// </summary>
        public bool DoLog { get; set; }
        /// <summary>
        /// Gets or sets the help text of this command.
        /// </summary>
        public string HelpText { get; set; }
        /// <summary>
        /// Gets or sets an extended description of this command.
        /// </summary>
        public string[]? HelpDesc { get; set; }
        /// <summary>
        /// Gets the name of the command.
        /// </summary>
        public string Name { get { return Names[0]; } }
        /// <summary>
        /// Gets the names of the command.
        /// </summary>
        public List<string> Names { get; protected set; }
        /// <summary>
        /// Gets the permissions of the command.
        /// </summary>
        public List<string> Permissions { get; protected set; }

        private CommandDelegate commandDelegate;
        public CommandDelegate CommandDelegate {
            get { return commandDelegate; }
            set {
                if (value == null)
                    throw new ArgumentNullException();

                commandDelegate = value;
            }
        }

        public Command(List<string> permissions, CommandDelegate cmd, params string[] names)
            : this(cmd, names) {
            Permissions = permissions;
        }

        public Command(string permissions, CommandDelegate cmd, params string[] names)
            : this(cmd, names) {
            Permissions = new List<string> { permissions };
        }

        public Command(CommandDelegate cmd, params string[] names) {
            ArgumentNullException.ThrowIfNull(cmd);
            if (names == null || names.Length < 1)
                throw new ArgumentException(null, nameof(names));
            
            AllowServer = true;
            AllowCoord = true;
            commandDelegate = cmd;
            DoLog = true;
            HelpText = GetString("No help available.");
            HelpDesc = null;
            Names = [.. names];
            Permissions = [];
        }

        public bool Run(CommandArgs args) {
            if (!CanRun(args.Executor))
                return false;

            try {
                CommandDelegate(args);
            }
            catch (Exception e) {
                args.Executor.SendErrorMessage(GetString("Command failed, check logs for more details."));
                TShock.Log.Error(e.ToString());
            }

            return true;
        }

        public bool Run(string msg, bool silent, CommandExecutor executor, List<string> parms) {
            return Run(new CommandArgs(msg, silent, executor, parms));
        }

        public bool Run(string msg, CommandExecutor executor, List<string> parms) {
            return Run(msg, false, executor, parms);
        }

        public bool HasAlias(string name) {
            return Names.Contains(name);
        }

        public bool CanRun(CommandExecutor executor) {
            if (executor.IsServer) {
                return true;
            }
            if (Permissions == null || Permissions.Count < 1) {
                return true;
            }
            var player = executor.Player!;
            foreach (var Permission in Permissions) {
                if (player.HasPermission(Permission)) {
                    return true;
                }
            }
            return false;
        }
    }
}
