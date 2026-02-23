using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnifierTSL;
using UnifierTSL.Servers;

namespace TShockAPI.DB
{
    public class RegionManager
    {
        [Table("Regions")]
        public class RegionTable
        {
            [Column, PrimaryKey, Identity]
            public int Id { get; set; }
            [Column] public int X1 { get; set; }
            [Column] public int Y1 { get; set; }
            [Column] public int Width { get; set; }
            [Column] public int Height { get; set; }
            [Column(DataType = DataType.VarChar, Length = 50)]
            public required string RegionName { get; set; }
            [Column(DataType = DataType.VarChar, Length = 50)]
            public required string WorldID { get; set; }
            [Column] public required string UserIds { get; set; }
            [Column] public int Protected { get; set; }
            [Column] public required string Groups { get; set; }
            [Column(DataType = DataType.VarChar, Length = 50)]
            public required string Owner { get; set; }
            [Column] public int Z { get; set; }
        }
        public List<Region> Regions = new List<Region>();
        readonly DataConnection database;
        readonly ITable<RegionTable> regionTable;
        public RegionManager(DataConnection db) {
            database = db;
            regionTable = database.CreateTable<RegionTable>(tableOptions: TableOptions.CreateIfNotExists);
        }
        /// <summary>
        /// Reloads all regions.
        /// </summary>
        public void Reload() {
            try {
                Regions.Clear();

                foreach (var record in regionTable)
                {
                    string[] splitids = record.UserIds.Split([','], StringSplitOptions.RemoveEmptyEntries);

                    Region r = new Region(record.Id, new Rectangle(record.X1, record.Y1, record.Width, record.Height),
                                        record.RegionName, record.Owner, record.Protected != 0, record.WorldID, record.Z);
                    r.SetAllowedGroups(record.Groups);

                    try
                    {
                        foreach (string t in splitids)
                        {
                            if (int.TryParse(t, out int userid))
                                r.AllowedIDs.Add(userid);
                            else
                                TShock.Log.Warning(GetString($"One of your UserIDs is not a usable integer: {t}"));
                        }
                    }
                    catch (Exception e)
                    {
                        TShock.Log.Error(GetString("Your database contains invalid UserIDs (they should be integers)."));
                        TShock.Log.Error(GetString("A lot of things will fail because of this. You must manually delete and re-create the allowed field."));
                        TShock.Log.Error(e.ToString());
                        TShock.Log.Error(e.StackTrace);
                    }

                    Regions.Add(r);
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
        }

        /// <summary>
        /// Adds a region to the database.
        /// </summary>
        public bool AddRegion(int tx, int ty, int width, int height, string regionname, string owner, string worldid, int z = 0) {
            if (GetRegionByName(regionname, worldid) != null) {
                return false;
            }
            try {
                var newRegion = new RegionTable {
                    X1 = tx,
                    Y1 = ty,
                    Width = width,
                    Height = height,
                    RegionName = regionname,
                    WorldID = worldid,
                    UserIds = "",
                    Protected = 1,
                    Groups = "",
                    Owner = owner,
                    Z = z
                };

                newRegion.Id = database.InsertWithInt32Identity(newRegion);

                Region region = new Region(newRegion.Id, new Rectangle(tx, ty, width, height), regionname, owner, true, worldid, z);
                Regions.Add(region);
                Hooks.RegionHooks.OnRegionCreated(region);
                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return false;
        }

        /// <summary>
        /// Deletes the region from this world with a given ID.
        /// </summary>
        public bool DeleteRegion(string worldid, int id) {
            try {
                regionTable.Where(r => r.Id == id && r.WorldID == worldid).Delete();

                var region = Regions.FirstOrDefault(r => r.ID == id && r.WorldID == worldid);
                Regions.RemoveAll(r => r.ID == id && r.WorldID == worldid);
                Hooks.RegionHooks.OnRegionDeleted(region);
                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return false;
        }

        /// <summary>
        /// Deletes the region from this world with a given name.
        /// </summary>
        public bool DeleteRegion(string worldid, string name) {
            try {
                regionTable.Where(r => r.RegionName == name && r.WorldID == worldid).Delete();

                var region = Regions.FirstOrDefault(r => r.Name == name && r.WorldID == worldid);
                Regions.RemoveAll(r => r.Name == name && r.WorldID == worldid);
                Hooks.RegionHooks.OnRegionDeleted(region);
                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return false;
        }

        /// <summary>
        /// Sets the protected state of the region with a given ID.
        /// </summary>
        public bool SetRegionState(string worldid, int id, bool state) {
            try {
                regionTable.Where(r => r.Id == id && r.WorldID == worldid)
                          .Set(r => r.Protected, state ? 1 : 0)
                          .Update();

                var region = GetRegionByID(id, worldid);
                if (region != null) {
                    region.DisableBuild = state;
                }
                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
                return false;
            }
        }

        /// <summary>
        /// Sets the protected state of the region with a given name.
        /// </summary>
        public bool SetRegionState(string worldid, string name, bool state) {
            try {
                regionTable.Where(r => r.RegionName == name && r.WorldID == worldid)
                          .Set(r => r.Protected, state ? 1 : 0)
                          .Update();

                var region = GetRegionByName(name, worldid);
                if (region != null)
                    region.DisableBuild = state;
                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
                return false;
            }
        }

        public bool ResizeRegion(string worldid, string regionName, int addAmount, int direction) {
            try {
                var regionRecord = regionTable.FirstOrDefault(r => r.RegionName == regionName && r.WorldID == worldid);
                if (regionRecord == null)
                    return false;

                int X = regionRecord.X1;
                int Y = regionRecord.Y1;
                int height = regionRecord.Height;
                int width = regionRecord.Width;

                switch (direction) {
                    case 0:
                        Y -= addAmount;
                        height += addAmount;
                        break;
                    case 1:
                        width += addAmount;
                        break;
                    case 2:
                        height += addAmount;
                        break;
                    case 3:
                        X -= addAmount;
                        width += addAmount;
                        break;
                    default:
                        return false;
                }

                foreach (var region in Regions.Where(r => r.Name == regionName))
                    region.Area = new Rectangle(X, Y, width, height);

                regionTable.Where(r => r.RegionName == regionName && r.WorldID == worldid)
                          .Set(r => r.X1, X)
                          .Set(r => r.Y1, Y)
                          .Set(r => r.Width, width)
                          .Set(r => r.Height, height)
                          .Update();

                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return false;
        }

        /// <summary>
        /// Renames a region
        /// </summary>
        public bool RenameRegion(string worldid, string oldName, string newName) {

            try {
                var region = Regions.FirstOrDefault(r => r.Name == oldName && r.WorldID == worldid);
                if (region == null)
                    return false;

                int updated = regionTable.Where(r => r.RegionName == oldName && r.WorldID == worldid)
                                        .Set(r => r.RegionName, newName)
                                        .Update();

                if (updated > 0) {
                    region.Name = newName;
                    Hooks.RegionHooks.OnRegionRenamed(region, oldName, newName);
                    return true;
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }

            return false;
        }

        /// <summary>
        /// Removes an allowed user from a region
        /// </summary>
        public bool RemoveUser(string worldid, string regionName, string userName) {
            var r = GetRegionByName(regionName, worldid);
            if (r != null) {
                if (!r.RemoveID(TShock.UserAccounts.GetUserAccountID(userName))) {
                    return false;
                }

                string ids = string.Join(",", r.AllowedIDs);
                regionTable.Where(r => r.RegionName == regionName && r.WorldID == worldid)
                          .Set(r => r.UserIds, ids)
                          .Update();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds a user to a region's allowed user list
        /// </summary>
        public bool AddNewUser(string worldid, string regionName, string userName) {
            try {
                var regionRecord = regionTable.FirstOrDefault(r => r.RegionName == regionName && r.WorldID == worldid);
                if (regionRecord == null)
                    return false;

                string mergedIDs = regionRecord.UserIds;
                string userIdToAdd = Convert.ToString(TShock.UserAccounts.GetUserAccountID(userName));
                string[] ids = mergedIDs.Split(',');

                if (ids.Contains(userIdToAdd))
                    return true;

                if (string.IsNullOrEmpty(mergedIDs))
                    mergedIDs = userIdToAdd;
                else
                    mergedIDs = string.Concat(mergedIDs, ",", userIdToAdd);

                regionTable.Where(r => r.RegionName == regionName && r.WorldID == worldid)
                          .Set(r => r.UserIds, mergedIDs)
                          .Update();

                foreach (var r in Regions) {
                    if (r.Name == regionName && r.WorldID == worldid)
                        r.SetAllowedIDs(mergedIDs);
                }
                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return false;
        }

        /// <summary>
        /// Sets the position of a region.
        /// </summary>
        public bool PositionRegion(string worldid, string regionName, int x, int y, int width, int height) {
            try {
                Region region = Regions.First(r => string.Equals(regionName, r.Name, StringComparison.OrdinalIgnoreCase));
                region.Area = new Rectangle(x, y, width, height);

                regionTable.Where(r => r.RegionName == regionName && r.WorldID == worldid)
                          .Set(r => r.X1, x)
                          .Set(r => r.Y1, y)
                          .Set(r => r.Width, width)
                          .Set(r => r.Height, height)
                          .Update();

                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return false;
        }

        /// <summary>
        /// Gets all the regions names from world
        /// </summary>
        public List<Region> ListAllRegions(string worldid) {
            var regions = new List<Region>();
            try {
                var query = regionTable.Where(r => r.WorldID == worldid).Select(r => r.RegionName);
                foreach (var name in query) {
                    regions.Add(new Region { Name = name });
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return regions;
        }

        /// <summary>
        /// Changes the owner of the region with the given name
        /// </summary>
        public bool ChangeOwner(string worldid, string regionName, string newOwner) {
            var region = GetRegionByName(regionName, worldid);
            if (region != null) {
                region.Owner = newOwner;
                regionTable.Where(r => r.RegionName == regionName && r.WorldID == worldid)
                          .Set(r => r.Owner, newOwner)
                          .Update();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Allows a group to use a region
        /// </summary>
        public bool AllowGroup(string worldid, string regionName, string groupName) {
            try {
                var regionRecord = regionTable.FirstOrDefault(r => r.RegionName == regionName && r.WorldID == worldid);
                if (regionRecord == null)
                    return false;

                string mergedGroups = regionRecord.Groups;
                string[] groups = mergedGroups.Split(',');

                if (groups.Contains(groupName))
                    return true;

                if (mergedGroups != "")
                    mergedGroups += ",";
                mergedGroups += groupName;

                regionTable.Where(r => r.RegionName == regionName && r.WorldID == worldid)
                          .Set(r => r.Groups, mergedGroups)
                          .Update();

                Region r = GetRegionByName(regionName, worldid);
                if (r != null) {
                    r.SetAllowedGroups(mergedGroups);
                }
                else {
                    return false;
                }

                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
                return false;
            }
        }

        /// <summary>
        /// Removes a group's access to a region
        /// </summary>
        public bool RemoveGroup(string worldid, string regionName, string group) {
            var r = GetRegionByName(regionName, worldid);
            if (r != null) {
                r.RemoveGroup(group);
                string groups = string.Join(",", r.AllowedGroups);
                regionTable.Where(r => r.RegionName == regionName && r.WorldID == worldid)
                          .Set(r => r.Groups, groups)
                          .Update();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sets the Z index of a given region
        /// </summary>
        public bool SetZ( string worldid, string name, int z) {
            try {
                regionTable.Where(r => r.RegionName == name && r.WorldID == worldid)
                          .Set(r => r.Z, z)
                          .Update();

                var region = GetRegionByName(name, worldid);
                if (region != null)
                    region.Z = z;
                return true;
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
                return false;
            }
        }

        /// <summary>
        /// Returns a region with the given name
        /// </summary>
        /// <param name="name">Region name</param>
        /// <returns>The region with the given name, or null if not found</returns>
        public Region? GetRegionByName(string name, string worldId) => Regions.FirstOrDefault(r => r.Name.Equals(name) && r.WorldID == worldId);

        /// <summary>
        /// Returns a region with the given ID
        /// </summary>
        /// <param name="id">Region ID</param>
        /// <returns>The region with the given ID, or null if not found</returns>
        public Region? GetRegionByID(int id, string worldId) => Regions.FirstOrDefault(r => r.ID == id && r.WorldID == worldId);

        /// <summary>
        /// Returns the <see cref="Region"/> with the highest Z index of the given list
        /// </summary>
        /// <param name="regions">List of Regions to compare</param>
        /// <returns></returns>
        public Region? GetTopRegion(IEnumerable<Region> regions) {
            Region? ret = null;
            foreach (Region r in regions) {
                if (ret == null)
                    ret = r;
                else {
                    if (r.Z > ret.Z)
                        ret = r;
                }
            }
            return ret;
        }
        /// <summary>
        /// Checks if a given player can build in a region at the given (x, y) coordinate
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="ply">Player to check permissions with</param>
        /// <returns>Whether the player can build at the given (x, y) coordinate</returns>
        public bool CanBuild(ServerContext server, int x, int y, TSPlayer ply) {
            if (!ply.HasPermission(Permissions.canbuild)) {
                return false;
            }
            Region? top = null;

            foreach (Region region in Regions.ToList()) {
                if (region.InArea(x, y)) {
                    if (top == null || region.Z > top.Z)
                        top = region;
                }
            }
            return top == null || top.HasPermissionToBuildInRegion(ply);
        }

        /// <summary>
        /// Checks if any regions exist at the given (x, y) coordinate
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <returns>Whether any regions exist at the given (x, y) coordinate</returns>
        public bool InArea(string worldid, int x, int y) 
            => Regions.Any(r => worldid == r.WorldID && r.InArea(x, y));

        /// <summary>
        /// Checks if any regions exist at the given (x, y) coordinate
        /// and returns an IEnumerable containing their names
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <returns>The names of any regions that exist at the given (x, y) coordinate</returns>
        public IEnumerable<string> InAreaRegionName(string worldid, int x, int y) 
            => Regions
            .Where(r => worldid == r.WorldID && r.InArea(x, y))
            .Select(r => r.Name);

        /// <summary>
        /// Checks if any regions exist at the given (x, y) coordinate
        /// and returns an IEnumerable containing their IDs
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <returns>The IDs of any regions that exist at the given (x, y) coordinate</returns>
        public IEnumerable<int> InAreaRegionID(string worldid, int x, int y)
            => Regions
            .Where(r => worldid == r.WorldID && r.InArea(x, y))
            .Select(r => r.ID);

        /// <summary>
        /// Checks if any regions exist at the given (x, y) coordinate
        /// and returns an IEnumerable containing their <see cref="Region"/> objects
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <returns>The <see cref="Region"/> objects of any regions that exist at the given (x, y) coordinate</returns>
        public IEnumerable<Region> InAreaRegion(string worldid, int x, int y)
            => Regions
            .Where(r => worldid == r.WorldID && r.InArea(x, y));
    }

    public class Region
    {
        public int ID { get; set; }
        public Rectangle Area { get; set; }
        public string Name { get; set; }
        public string Owner { get; set; }
        public bool DisableBuild { get; set; }
        public string WorldID { get; set; }
        public List<int> AllowedIDs { get; set; }
        public List<string> AllowedGroups { get; set; }
        public int Z { get; set; }

        public Region(int id, Rectangle region, string name, string owner, bool disablebuild, string RegionWorldIDz, int z)
            : this() {
            ID = id;
            Area = region;
            Name = name;
            Owner = owner;
            DisableBuild = disablebuild;
            WorldID = RegionWorldIDz;
            Z = z;
        }

        public Region() {
            Area = Rectangle.Empty;
            Name = string.Empty;
            DisableBuild = true;
            WorldID = string.Empty;
            AllowedIDs = new List<int>();
            AllowedGroups = new List<string>();
            Owner = string.Empty;
            Z = 0;
        }

        /// <summary>
        /// Checks if a given point is in the region's area
        /// </summary>
        /// <param name="point">Point to check</param>
        /// <returns>Whether the point exists in the region's area</returns>
        public bool InArea(Rectangle point) {
            return InArea(point.X, point.Y);
        }

        /// <summary>
        /// Checks if a given (x, y) coordinate is in the region's area
        /// </summary>
        /// <param name="x">X coordinate to check</param>
        /// <param name="y">Y coordinate to check</param>
        /// <returns>Whether the coordinate exists in the region's area</returns>
        public bool InArea(int x, int y) //overloaded with x,y
        {
            /*
			DO NOT CHANGE TO Area.Contains(x, y)!
			Area.Contains does not account for the right and bottom 'border' of the rectangle,
			which results in regions being trimmed.
			*/
            return x >= Area.X && x <= Area.X + Area.Width && y >= Area.Y && y <= Area.Y + Area.Height;
        }

        /// <summary>
        /// Checks if a given player has permission to build in the region
        /// </summary>
        /// <param name="ply">Player to check permissions with</param>
        /// <returns>Whether the player has permission</returns>
        public bool HasPermissionToBuildInRegion(TSPlayer ply) {
            if (!DisableBuild) {
                return true;
            }
            if (!ply.IsLoggedIn) {
                if (!ply.HasBeenNaggedAboutLoggingIn) {
                    ply.SendMessage(GetString("You must be logged in to take advantage of protected regions."), Color.Red);
                    ply.HasBeenNaggedAboutLoggingIn = true;
                }
                return false;
            }

            return ply.HasPermission(Permissions.editregion) || AllowedIDs.Contains(ply.Account.ID) || AllowedGroups.Contains(ply.Group.Name) || Owner == ply.Account.Name;
        }

        /// <summary>
        /// Sets the user IDs which are allowed to use the region
        /// </summary>
        /// <param name="ids">String of IDs to set</param>
        public void SetAllowedIDs(string ids) {
            string[] idArr = ids.Split(',');
            List<int> idList = new List<int>();

            foreach (string id in idArr) {
                int i = 0;
                if (int.TryParse(id, out i) && i != 0) {
                    idList.Add(i);
                }
            }
            AllowedIDs = idList;
        }

        /// <summary>
        /// Sets the group names which are allowed to use the region
        /// </summary>
        /// <param name="groups">String of group names to set</param>
        public void SetAllowedGroups(string groups) {
            // prevent null pointer exceptions
            if (!string.IsNullOrEmpty(groups)) {
                List<string> groupList = groups.Split(',').ToList();

                for (int i = 0; i < groupList.Count; i++) {
                    groupList[i] = groupList[i].Trim();
                }

                AllowedGroups = groupList;
            }
        }

        /// <summary>
        /// Removes a user's access to the region
        /// </summary>
        /// <param name="id">User ID to remove</param>
        /// <returns>true if the user was found and removed from the region's allowed users</returns>
        public bool RemoveID(int id) {
            return AllowedIDs.Remove(id);
        }

        /// <summary>
        /// Removes a group's access to the region
        /// </summary>
        /// <param name="groupName">Group name to remove</param>
        /// <returns></returns>
        public bool RemoveGroup(string groupName) {
            return AllowedGroups.Remove(groupName);
        }
    }
}
