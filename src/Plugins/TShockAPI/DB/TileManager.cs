using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TShockAPI.Hooks;

namespace TShockAPI.DB
{
    public class TileManager
    {
        class TileBansTable {
            [PrimaryKey]
            public int TileId { get; set; }
            public string AllowedGroups { get; set; } = "";
        }
        readonly DataConnection database;
        readonly ITable<TileBansTable> tileBansTable;
        public List<TileBan> TileBans = new List<TileBan>();
        public TileManager(DataConnection db) { 
            database = db;
            tileBansTable = database.CreateTable<TileBansTable>(tableOptions: TableOptions.CreateIfNotExists);
            UpdateBans();
        }
        public void UpdateBans() {
            TileBans.Clear();

            var bans = tileBansTable.ToList();
            foreach (var ban in bans) {
                TileBan tileBan = new TileBan((short)ban.TileId);
                tileBan.SetAllowedGroups(ban.AllowedGroups);
                TileBans.Add(tileBan);
            }
        }

        public void AddNewBan(short id = 0) {
            try {
                database.Insert(new TileBansTable { TileId = id, AllowedGroups = "" });

                if (!TileIsBanned(id, null))
                    TileBans.Add(new TileBan(id));
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
        }

        public void RemoveBan(short id) {
            if (!TileIsBanned(id, null))
                return;
            try {
                tileBansTable.Where(x => x.TileId == id).Delete();
                TileBans.Remove(new TileBan(id));
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
        }

        public bool AllowGroup(short id, string name) {
            var b = GetBanById(id);
            if (b != null) {
                try {
                    string groupsNew = string.Join(",", b.AllowedGroups);
                    if (groupsNew.Length > 0)
                        groupsNew += ",";
                    groupsNew += name;
                    b.SetAllowedGroups(groupsNew);

                    int q = tileBansTable
                        .Where(x => x.TileId == id)
                        .Set(x => x.AllowedGroups, groupsNew)
                        .Update();

                    return q > 0;
                }
                catch (Exception ex) {
                    TShock.Log.Error(ex.ToString());
                }
            }

            return false;
        }

        public bool RemoveGroup(short id, string group) {
            var b = GetBanById(id);
            if (b != null) {
                try {
                    b.RemoveGroup(group);
                    string groups = string.Join(",", b.AllowedGroups);
                    int q = tileBansTable
                        .Where(x => x.TileId == id)
                        .Set(x => x.AllowedGroups, groups)
                        .Update();

                    if (q > 0)
                        return true;
                }
                catch (Exception ex) {
                    TShock.Log.Error(ex.ToString());
                }
            }
            return false;
        }

        public bool TileIsBanned(short id) {
            if (TileBans.Contains(new TileBan(id))) {
                return true;
            }
            return false;
        }

        public bool TileIsBanned(short id, TSPlayer? ply) {
            if (TileBans.Contains(new TileBan(id))) {
                var b = GetBanById(id);
                if (b == null) {
                    return false;
                }
                return !b.HasPermissionToPlaceTile(ply);
            }
            return false;
        }
        public TileBan? GetBanById(short id) {
            foreach (TileBan b in TileBans) {
                if (b.ID == id) {
                    return b;
                }
            }
            return null;
        }
    }
    public class TileBan : IEquatable<TileBan>
    {
        public short ID { get; set; }
        public List<string> AllowedGroups { get; set; }

        public TileBan(short id)
            : this() {
            ID = id;
            AllowedGroups = new List<string>();
        }

        public TileBan() {
            ID = 0;
            AllowedGroups = new List<string>();
        }

        public bool Equals(TileBan? other) {
            if (other == null) return false;
            return ID == other.ID;
        }

        public bool HasPermissionToPlaceTile(TSPlayer? ply) {
            if (ply == null)
                return false;

            if (ply.HasPermission(Permissions.canusebannedtiles))
                return true;

            PermissionHookResult hookResult = PlayerHooks.OnPlayerTilebanPermission(ply, this);
            if (hookResult != PermissionHookResult.Unhandled)
                return hookResult == PermissionHookResult.Granted;

            var cur = ply.Group;
            var traversed = new List<Group>();
            while (cur != null) {
                if (AllowedGroups.Contains(cur.Name)) {
                    return true;
                }
                if (traversed.Contains(cur)) {
                    throw new InvalidOperationException(GetString($"Infinite group parenting ({cur.Name})"));
                }
                traversed.Add(cur);
                cur = cur.Parent;
            }
            return false;
            // could add in the other permissions in this class instead of a giant if switch.
        }

        public void SetAllowedGroups(string groups) {
            // prevent null pointer exceptions
            if (!string.IsNullOrEmpty(groups)) {
                List<string> groupArr = groups.Split(',').ToList();

                for (int i = 0; i < groupArr.Count; i++) {
                    groupArr[i] = groupArr[i].Trim();
                    //Console.WriteLine(groupArr[i]);
                }
                AllowedGroups = groupArr;
            }
        }

        public bool RemoveGroup(string groupName) {
            return AllowedGroups.Remove(groupName);
        }

        public override string ToString() {
            return ID + (AllowedGroups.Count > 0 ? " (" + string.Join(",", AllowedGroups) + ")" : "");
        }
    }
}
