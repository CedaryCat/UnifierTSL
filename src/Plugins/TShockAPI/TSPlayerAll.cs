using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using TrProtocol.Models;
using UnifierTSL.Servers;

namespace TShockAPI
{
    /// <summary>
    /// Represents the TShock wildcard player selector for command parameters that accept "*".
    /// </summary>
    public sealed class TSPlayerAll : TSPlayer
    {
        public TSPlayerAll(ServerContext? scope = null, int? excludedPlayerIndex = null) : base("All")
        {
            Scope = scope;
            ExcludedPlayerIndex = excludedPlayerIndex;
        }

        public ServerContext? Scope { get; }

        public bool IsServerScoped => Scope is not null;

        public int? ExcludedPlayerIndex { get; }

        public sealed override ServerContext GetCurrentServer()
        {
            return Scope ?? throw new InvalidOperationException(GetString("TSPlayerAll is not bound to a single server scope."));
        }

        public IReadOnlyList<TSPlayer> ResolveTargets()
        {
            return [.. TShock.Players
                .Where(static player => player is not null)
                .Where(static player => player!.Active)
                .Where(player => Scope is null || ReferenceEquals(player!.GetCurrentServer(), Scope))
                .Where(player => ExcludedPlayerIndex is null || player!.Index != ExcludedPlayerIndex.Value)
                .Select(static player => player!)];
        }

        public override void Disconnect(string reason)
        {
            ForEachTarget(player => player.Disconnect(reason));
        }

        [Obsolete("This method may not send tiles the way you would expect it to. The (x,y) coordinates are the top left corner of the tile square, switch to " + nameof(SendTileSquareCentered) + " if you wish for the coordindates to be the center of the square.")]
        public override bool SendTileSquare(int x, int y, int size = 10)
        {
            return AnyTarget(player => player.SendTileSquare(x, y, size));
        }

        public override bool SendTileSquareCentered(int x, int y, byte size = 10)
        {
            return AnyTarget(player => player.SendTileSquareCentered(x, y, size));
        }

        public override bool SendTileRect(short x, short y, byte width = 10, byte length = 10, TileChangeType changeType = TileChangeType.None)
        {
            return AnyTarget(player => player.SendTileRect(x, y, width, length, changeType));
        }

        public override void GiveItem(int type, int stack, int prefix = 0)
        {
            ForEachTarget(player => player.GiveItem(type, stack, prefix));
        }

        public override void GiveItem(NetItem item)
        {
            ForEachTarget(player => player.GiveItem(item));
        }

        public override void SendInfoMessage(string msg)
        {
            ForEachTarget(player => player.SendInfoMessage(msg));
        }

        public override void SendSuccessMessage(string msg)
        {
            ForEachTarget(player => player.SendSuccessMessage(msg));
        }

        public override void SendWarningMessage(string msg)
        {
            ForEachTarget(player => player.SendWarningMessage(msg));
        }

        public override void SendErrorMessage(string msg)
        {
            ForEachTarget(player => player.SendErrorMessage(msg));
        }

        public override void SendMessage(string msg, Color color)
        {
            ForEachTarget(player => player.SendMessage(msg, color));
        }

        public override void SendMessage(string msg, byte red, byte green, byte blue)
        {
            ForEachTarget(player => player.SendMessage(msg, red, green, blue));
        }

        public override void SendMessageFromPlayer(string msg, byte red, byte green, byte blue, int ply)
        {
            ForEachTarget(player => player.SendMessageFromPlayer(msg, red, green, blue, ply));
        }

        public override void DamagePlayer(int damage)
        {
            ForEachTarget(player => player.DamagePlayer(damage));
        }

        public override void DamagePlayer(int damage, PlayerDeathReason reason)
        {
            ForEachTarget(player => player.DamagePlayer(damage, reason));
        }

        public override void KillPlayer()
        {
            ForEachTarget(static player => player.KillPlayer());
        }

        public override void KillPlayer(PlayerDeathReason reason)
        {
            ForEachTarget(player => player.KillPlayer(reason));
        }

        public override void SetTeam(int team)
        {
            ForEachTarget(player => player.SetTeam(team));
        }

        public override void SetPvP(bool mode, bool withMsg = false)
        {
            ForEachTarget(player => player.SetPvP(mode, withMsg));
        }

        public override void Disable(string reason = "", DisableFlags flags = DisableFlags.WriteToLog)
        {
            ForEachTarget(player => player.Disable(reason, flags));
        }

        public override void Whoopie(object time)
        {
            ForEachTarget(player => player.Whoopie(time));
        }

        public override void SetBuff(int type, int time = 3600, bool bypass = false)
        {
            ForEachTarget(player => player.SetBuff(type, time, bypass));
        }

        public override void SendData(PacketTypes msgType, string text = "", int number = 0, float number2 = 0f, float number3 = 0f, float number4 = 0f, int number5 = 0)
        {
            ForEachTarget(player => player.SendData(msgType, text, number, number2, number3, number4, number5));
        }

        public override void SendDataFromPlayer(PacketTypes msgType, int ply, string text = "", float number2 = 0f, float number3 = 0f, float number4 = 0f, int number5 = 0)
        {
            ForEachTarget(player => player.SendDataFromPlayer(msgType, ply, text, number2, number3, number4, number5));
        }

        public override void SendRawData(byte[] data)
        {
            ForEachTarget(player => player.SendRawData(data));
        }

        private bool AnyTarget(Func<TSPlayer, bool> action)
        {
            bool handled = false;
            foreach (TSPlayer player in ResolveTargets()) {
                handled |= action(player);
            }

            return handled;
        }

        private void ForEachTarget(Action<TSPlayer> action)
        {
            foreach (TSPlayer player in ResolveTargets()) {
                action(player);
            }
        }
    }
}
