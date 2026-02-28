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
    public class ProjectileManagager
    {
        [Table(Name = "ProjectileBans")]
        class ProjectileBansTable {
            [Column(DataType = DataType.Int32)]
            public int ProjectileID { get; set; }
            [Column(DataType = DataType.Text)]
            public required string AllowedGroups { get; set; }
        }
        readonly DataConnection database;
        readonly ITable<ProjectileBansTable> projectileBansTable;
        public List<ProjectileBan> ProjectileBans = new List<ProjectileBan>();
        public ProjectileManagager(DataConnection db) {
            database = db;
            projectileBansTable = database.CreateTable<ProjectileBansTable>(tableOptions: TableOptions.CreateIfNotExists);
            UpdateBans();
        }
        public void UpdateBans() {
            ProjectileBans.Clear();

            try {
                var rows = projectileBansTable.ToList(); // SELECT * FROM ProjectileBans

                foreach (var row in rows) {
                    ProjectileBan ban = new ProjectileBan((short)row.ProjectileID);
                    ban.SetAllowedGroups(row.AllowedGroups);
                    ProjectileBans.Add(ban);
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
        }

        public void AddNewBan(short id = 0) {
            try {
                projectileBansTable.Insert(() => new ProjectileBansTable {
                    ProjectileID = id,
                    AllowedGroups = ""
                });

                if (!ProjectileIsBanned(id, null))
                    ProjectileBans.Add(new ProjectileBan(id));
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
        }

        public void RemoveBan(short id) {
            if (!ProjectileIsBanned(id, null))
                return;

            try {
                projectileBansTable
                    .Where(b => b.ProjectileID == id)
                    .Delete();

                ProjectileBans.Remove(new ProjectileBan(id));
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

                    int q = projectileBansTable
                        .Where(x => x.ProjectileID == id)
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

                    int q = projectileBansTable
                        .Where(x => x.ProjectileID == id)
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
        public bool ProjectileIsBanned(short id) => ProjectileBans.Contains(new(id));

        public bool ProjectileIsBanned(short id, TSPlayer? ply) {
            if (ProjectileBans.Contains(new ProjectileBan(id))) {
                var b = GetBanById(id);
                if (b == null) {
                    return false;
                }
                return !b.HasPermissionToCreateProjectile(ply);
            }
            return false;
        }
        public ProjectileBan? GetBanById(short id) {
            foreach (ProjectileBan b in ProjectileBans) {
                if (b.ID == id) {
                    return b;
                }
            }
            return null;
        }
    }
    public class ProjectileBan : IEquatable<ProjectileBan>
    {
        public short ID { get; set; }
        public List<string> AllowedGroups { get; set; }

        public ProjectileBan(short id)
            : this() {
            ID = id;
            AllowedGroups = new List<string>();
        }

        public ProjectileBan() {
            ID = 0;
            AllowedGroups = new List<string>();
        }

        public bool Equals(ProjectileBan? other) {
            if (other == null) return false;
            return ID == other.ID;
        }

        public bool HasPermissionToCreateProjectile(TSPlayer? ply) {
            if (ply == null)
                return false;

            if (ply.HasPermission(Permissions.canusebannedprojectiles))
                return true;

            PermissionHookResult hookResult = PlayerHooks.OnPlayerProjbanPermission(ply, this);
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

        public bool RemoveGroup(string groupName) => AllowedGroups.Remove(groupName);

        public override string ToString() => ID + (AllowedGroups.Count > 0 ? $" ({string.Join(",", AllowedGroups)})" : "");
    }
}
