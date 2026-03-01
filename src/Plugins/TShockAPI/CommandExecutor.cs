using Microsoft.Xna.Framework;
using NuGet.Protocol.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TShockAPI.DB;
using UnifierTSL;
using UnifierTSL.Logging;
using UnifierTSL.Servers;

namespace TShockAPI
{
    public readonly record struct CommandExecutor(ServerContext? SourceServer, byte UserId, TSRestPlayer? RestPlayer = null)
    {
        public readonly string Name {
            get {
                return 
                    SourceServer is null
                        ? RestPlayer
                            ?.Name 
                            ?? "UnifiedConsole"
                        : IsClient
                            ? TShock.Players[UserId].Name
                            : $"Serv:{SourceServer.Name}";
            }
        }
        public readonly bool IsServer => SourceServer is null || UserId == byte.MaxValue;
        [MemberNotNullWhen(true, nameof(SourceServer))]
        [MemberNotNullWhen(true, nameof(Player))]
        public readonly bool IsClient => SourceServer is not null && UserId != byte.MaxValue;
        public readonly TSPlayer? Player => SourceServer is null
            ? RestPlayer
            : IsClient
                ? TShock.Players[UserId]
                : SourceServer.GetExtension<TSServerPlayer>();
        public readonly UserAccount Account => Player?.Account ?? CoordAccount;
        public readonly Group Group => Player?.Group ?? superAdminGroup;
        public readonly string IP => Player?.IP ?? "127.0.0.1";

        static CommandExecutor() {
            CoordAccount = new UserAccount { Name = "Coord", Group = superAdminGroup.Name };
        }
        static readonly SuperAdminGroup superAdminGroup = new SuperAdminGroup();
        static readonly UserAccount CoordAccount;

        public bool HasPermission(string permission) {
            if (RestPlayer is not null) {
                return RestPlayer.HasPermission(permission);
            }
            if (IsServer) {
                return true;
            }
            return TShock.Players[UserId].HasPermission(permission);
        }

        record LogHost(string Name, string? CurrentLogCategory) : ILoggerHost;
        readonly static RoleLogger Log = UnifierApi.CreateLogger(new LogHost("TShock", "ExcuCmd"));
        public void SendMessage(string message, Color color,
            [CallerFilePath] string? file = null,
            [CallerMemberName] string? member = null,
            [CallerLineNumber] int line = 0) {

            if (SourceServer is null) {
                if (RestPlayer is not null) {
                    RestPlayer.SendMessage(message, color);
                }
                else {
                    Log.Info(message, file: file, member: member, line: line);
                }
            }
            else {
                if (UserId == byte.MaxValue) {
                    Log.InfoWithMetadata(
                        message,
                        metadata: [new("ServerContext", SourceServer.Name)],
                        file: file,
                        member: member,
                        line: line);
                }
                else {
                    TShock.Players[UserId].SendMessage(message, color);
                }
            }
        }
        public void SendMessage(string message, byte r, byte g, byte b,
            [CallerFilePath] string? file = null,
            [CallerMemberName] string? member = null,
            [CallerLineNumber] int line = 0) {

            if (SourceServer is null) {
                if (RestPlayer is not null) {
                    RestPlayer.SendMessage(message, new Color(r, g, b));
                }
                else {
                    Log.Info(message, file: file, member: member, line: line);
                }
            }
            else {
                if (UserId == byte.MaxValue) {
                    Log.InfoWithMetadata(
                        message,
                        metadata: [new("ServerContext", SourceServer.Name)],
                        file: file,
                        member: member,
                        line: line);
                }
                else {
                    TShock.Players[UserId].SendMessage(message, new Color(r, g, b));
                }
            }
        }
        public void SendErrorMessage(string message,
            [CallerFilePath] string? file = null,
            [CallerMemberName] string? member = null,
            [CallerLineNumber] int line = 0) {

            if (SourceServer is null) {
                if (RestPlayer is not null) {
                    RestPlayer.SendErrorMessage(message);
                }
                else {
                    Log.Error(message, file: file, member: member, line: line);
                }
            }
            else {
                if (UserId == byte.MaxValue) {
                    Log.ErrorWithMetadata(
                        message,
                        metadata: [new("ServerContext", SourceServer.Name)],
                        file: file,
                        member: member,
                        line: line);
                }
                else {
                    TShock.Players[UserId].SendErrorMessage(message);
                }
            }
        }
        public void SendInfoMessage(string message,
            [CallerFilePath] string? file = null,
            [CallerMemberName] string? member = null,
            [CallerLineNumber] int line = 0) {


            if (SourceServer is null) {
                if (RestPlayer is not null) {
                    RestPlayer.SendInfoMessage(message);
                }
                else {
                    Log.Info(message, file: file, member: member, line: line);
                }
            }
            else {
                if (UserId == byte.MaxValue) {
                    Log.InfoWithMetadata(
                        message,
                        metadata: [new("ServerContext", SourceServer.Name)],
                        file: file,
                        member: member,
                        line: line);
                }
                else {
                    TShock.Players[UserId].SendInfoMessage(message);
                }
            }
        }
        public void SendWarningMessage(string message,
            [CallerFilePath] string? file = null,
            [CallerMemberName] string? member = null,
            [CallerLineNumber] int line = 0) {


            if (SourceServer is null) {
                if (RestPlayer is not null) {
                    RestPlayer.SendWarningMessage(message);
                }
                else {
                    Log.Warning(message, file: file, member: member, line: line);
                }
            }
            else {
                if (UserId == byte.MaxValue) {
                    Log.WarningWithMetadata(
                        message,
                        metadata: [new("ServerContext", SourceServer.Name)],
                        file: file,
                        member: member,
                        line: line);
                }
                else {
                    TShock.Players[UserId].SendWarningMessage(message);
                }
            }
        }
        public void SendSuccessMessage(string message,
            [CallerFilePath] string? file = null,
            [CallerMemberName] string? member = null,
            [CallerLineNumber] int line = 0) {

            if (SourceServer is null) {
                if (RestPlayer is not null) {
                    RestPlayer.SendSuccessMessage(message);
                }
                else {
                    Log.Success(message, file: file, member: member, line: line);
                }
            }
            else {
                if (UserId == byte.MaxValue) {
                    Log.SuccessWithMetadata(
                        message,
                        metadata: [new("ServerContext", SourceServer.Name)],
                        file: file,
                        member: member,
                        line: line);
                }
                else {
                    TShock.Players[UserId].SendSuccessMessage(message);
                }
            }
        }
        public void LogError(string message,
            [CallerFilePath] string? file = null,
            [CallerMemberName] string? member = null,
            [CallerLineNumber] int line = 0) {

            if (SourceServer is null) {
                Log.Error(message, file: file, member: member, line: line);
            }
            else {
                Log.ErrorWithMetadata(
                    message,
                    metadata: [new("ServerContext", SourceServer.Name)],
                    file: file,
                    member: member,
                    line: line);
            }
        }
        public void LogWarning(string message,
            [CallerFilePath] string? file = null,
            [CallerMemberName] string? member = null,
            [CallerLineNumber] int line = 0) {

            if (SourceServer is null) {
                Log.Warning(message, file: file, member: member, line: line);
            }
            else {
                Log.WarningWithMetadata(
                    message,
                    metadata: [new("ServerContext", SourceServer.Name)],
                    file: file,
                    member: member,
                    line: line);
            }
        }
        public void LogInfo(string message,
            [CallerFilePath] string? file = null,
            [CallerMemberName] string? member = null,
            [CallerLineNumber] int line = 0) {

            if (SourceServer is null) {
                Log.Info(message, file: file, member: member, line: line);
            }
            else {
                Log.InfoWithMetadata(
                    message,
                    metadata: [new("ServerContext", SourceServer.Name)],
                    file: file,
                    member: member,
                    line: line);
            }
        }
        public void LogSuccess(string message,
            [CallerFilePath] string? file = null,
            [CallerMemberName] string? member = null,
            [CallerLineNumber] int line = 0) {

            if (SourceServer is null) {
                Log.Success(message, file: file, member: member, line: line);
            }
            else {
                Log.SuccessWithMetadata(
                    message,
                    metadata: [new("ServerContext", SourceServer.Name)],
                    file: file,
                    member: member,
                    line: line);
            }
        }
        public void SendLogs(string log, Color color, TSPlayer? excludedPlayer = null,
            [CallerFilePath] string? file = null,
            [CallerMemberName] string? member = null,
            [CallerLineNumber] int line = 0) {

            LogInfo(log, file: file, member: member, line: line);
            if (SourceServer is not null) {
                var setting = TShock.Config.GetServerSettings(SourceServer.Name);
                foreach (TSPlayer player in TShock.Players) {
                    if (player is null) {
                        continue;
                    }
                    if (player.GetCurrentServer() != SourceServer) {
                        continue;
                    }
                    if (player != excludedPlayer && player.Active && player.HasPermission(Permissions.logs) &&
                            player.DisplayLogs && !setting.DisableSpewLogs)
                        player.SendMessage(log, color);
                }
            }
        }
        public void SendMultipleMatchError(IEnumerable<object> matches,
            [CallerFilePath] string? file = null,
            [CallerMemberName] string? member = null,
            [CallerLineNumber] int line = 0) {

            var player = Player;
            if (player is not null) {
                player.SendMultipleMatchError(matches);
            }
            else {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(GetString("More than one match found -- unable to decide which is correct: "));
                foreach (var match in PaginationTools.BuildLinesFromTerms(matches.ToArray())) {
                    sb.AppendLine(match);
                }
                sb.AppendLine(GetString("Use \"my query\" for items with spaces."));
                sb.Append(GetString("Use tsi:[number] or tsn:[username] to distinguish between user IDs and usernames."));

                LogError(sb.ToString(), file: file, member: member, line: line);
            }
        }
        public void SendPage(int pageNumber, IEnumerable dataToPaginate, int dataToPaginateCount, PaginationTools.Settings? settings = null) {
            if (RestPlayer is not null) {
                PaginationTools.SendPage(RestPlayer, pageNumber, dataToPaginate, dataToPaginateCount, settings);
                return;
            }
            if (IsClient) {
                PaginationTools.SendPage(TShock.Players[UserId], pageNumber, dataToPaginate, dataToPaginateCount, settings);
                return;
            }
            StringBuilder stringBuilder = new StringBuilder();

            settings ??= new PaginationTools.Settings();

            if (dataToPaginateCount == 0) {
                if (settings.NothingToDisplayString != null) {
                    SendMessage(settings.NothingToDisplayString, settings.HeaderTextColor);
                }
                return;
            }

            int pageCount = ((dataToPaginateCount - 1) / settings.MaxLinesPerPage) + 1;
            if (settings.PageLimit > 0 && pageCount > settings.PageLimit)
                pageCount = settings.PageLimit;
            if (pageNumber > pageCount)
                pageNumber = pageCount;

            if (settings.IncludeHeader) {
                stringBuilder.AppendLine(string.Format(settings.HeaderFormat, pageNumber, pageCount));
            }

            int listOffset = (pageNumber - 1) * settings.MaxLinesPerPage;
            int offsetCounter = 0;
            int lineCounter = 0;
            foreach (object lineData in dataToPaginate) {
                if (lineData == null)
                    continue;
                if (offsetCounter++ < listOffset)
                    continue;
                if (lineCounter++ == settings.MaxLinesPerPage)
                    break;

                string lineMessage;
                Color lineColor = settings.LineTextColor;
                if (lineData is Tuple<string, Color>) {
                    var lineFormat = (Tuple<string, Color>)lineData;
                    lineMessage = lineFormat.Item1;
                    lineColor = lineFormat.Item2;
                }
                else if (settings.LineFormatter != null) {
                    try {
                        Tuple<string, Color> lineFormat = settings.LineFormatter(lineData, offsetCounter, pageNumber);
                        if (lineFormat == null)
                            continue;

                        lineMessage = lineFormat.Item1;
                        lineColor = lineFormat.Item2;
                    }
                    catch (Exception ex) {
                        throw new InvalidOperationException(
                          GetString("The method referenced by LineFormatter has thrown an exception. See inner exception for details."), ex);
                    }
                }
                else {
                    lineMessage = lineData.ToString() ?? "";
                }

                if (lineMessage != null) {
                    stringBuilder.AppendLine(lineMessage);
                }
            }

            if (lineCounter == 0) {
                if (settings.NothingToDisplayString != null) {
                    stringBuilder.AppendLine(settings.NothingToDisplayString);
                }
            }
            else if (settings.IncludeFooter && pageNumber + 1 <= pageCount) {
                stringBuilder.AppendLine(string.Format(settings.FooterFormat, pageNumber + 1, pageNumber, pageCount));
            }

            if (stringBuilder.Length > 0 && stringBuilder[^1] == '\n') {
                stringBuilder.Length--;
                if (stringBuilder.Length > 0 && stringBuilder[^1] == '\r') // Windows-style \r\n
                {
                    stringBuilder.Length--;
                }
            }

            SendInfoMessage(stringBuilder.ToString());
        }

        public void SendPage(int pageNumber, IList dataToPaginate, PaginationTools.Settings? settings = null) {
            SendPage(pageNumber, dataToPaginate, dataToPaginate.Count, settings);
        }
        public void SendFileTextAsMessage(string file) {
            if (RestPlayer is not null) {
                RestPlayer.SendFileTextAsMessage(file);
                return;
            }
            if (IsClient) {
                TShock.Players[UserId].SendFileTextAsMessage(file);
                return;
            }
            StringBuilder stringBuilder = new StringBuilder();

            string foo = "";
            bool containsOldFormat = false;
            using (var tr = new StreamReader(file)) {
                Color lineColor;
                while ((foo = tr.ReadLine()) != null) {
                    lineColor = Color.White;
                    if (string.IsNullOrWhiteSpace(foo)) {
                        continue;
                    }

                    var players = new List<string>();

                    foreach (TSPlayer ply in TShock.Players) {
                        if (ply != null && ply.Active) {
                            players.Add(ply.Name);
                        }
                    }

                    if (SourceServer is not null) {
                        foo = foo.Replace("%map%", SourceServer.Name);
                    }

                    foo = foo.Replace("%players%", String.Join(", ", players));
                    foo = foo.Replace("%specifier%", TShock.Config.GlobalSettings.CommandSpecifier);
                    foo = foo.Replace("%onlineplayers%",  Utils.GetActivePlayerCount().ToString());
                    foo = foo.Replace("%serverslots%", TShock.Config.GlobalSettings.MaxSlots.ToString());

                    stringBuilder.AppendLine(foo);
                }
            }

            if (stringBuilder.Length > 0 && stringBuilder[^1] == '\n') {
                stringBuilder.Length--;
                if (stringBuilder.Length > 0 && stringBuilder[^1] == '\r') // Windows-style \r\n
                {
                    stringBuilder.Length--;
                }
            }

            SendInfoMessage(stringBuilder.ToString());
        }
    }
}
