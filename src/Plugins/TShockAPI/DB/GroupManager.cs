using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using System.Collections;
using System.Diagnostics;

namespace TShockAPI.DB
{
    public class GroupManager : IEnumerable<Group>
    {
        [Table(Name = "GroupList")]
        class GroupList
        {
            [PrimaryKey]
            [Column(Length = 32, DataType = DataType.VarChar)]
            public required string GroupName { get; set; }

            [Column( Length = 32, DataType = DataType.VarChar)]
            public required string Parent { get; set; }

            [Column(DataType = DataType.Text)]
            public required string Commands { get; set; }

            [Column(DataType = DataType.Text)]
            public required string ChatColor { get; set; }

            [Column(DataType = DataType.Text)]
            public string Prefix { get; set; } = "";

            [Column(DataType = DataType.Text)]
            public string Suffix { get; set; } = "";
        }

        readonly DataConnection database;
        public readonly List<Group> groups = [];
        readonly ITable<GroupList> groupListTable;
        public GroupManager(DataConnection db) {
            database = db;
            groupListTable = database.CreateTable<GroupList>(tableOptions: TableOptions.CreateIfNotExists);

            if (!db.GetTable<GroupList>().Any()) {

                // Add default groups if they don't exist
                AddDefaultGroup(TShock.Config.GlobalSettings.DefaultGuestGroupName, "",
                    string.Join(",",
                        Permissions.canbuild,
                        Permissions.canregister,
                        Permissions.canlogin,
                        Permissions.canpartychat,
                        Permissions.cantalkinthird,
                        Permissions.canchat,
                        Permissions.synclocalarea,
                        Permissions.sendemoji));

                AddDefaultGroup(TShock.Config.GlobalSettings.DefaultRegistrationGroupName, TShock.Config.GlobalSettings.DefaultGuestGroupName,
                    string.Join(",",
                        Permissions.warp,
                        Permissions.canchangepassword,
                        Permissions.canlogout,
                        Permissions.summonboss,
                        Permissions.spawnpets,
                        Permissions.worldupgrades,
                        Permissions.whisper,
                        Permissions.wormhole,
                        Permissions.canpaint,
                        Permissions.pylon,
                        Permissions.tppotion,
                        Permissions.magicconch,
                        Permissions.demonconch));

                AddDefaultGroup("vip", TShock.Config.GlobalSettings.DefaultRegistrationGroupName,
                    string.Join(",",
                        Permissions.reservedslot,
                        Permissions.renamenpc,
                        Permissions.startinvasion,
                        Permissions.summonboss,
                        Permissions.whisper,
                        Permissions.wormhole));

                AddDefaultGroup("insecure-guest", "",
                    string.Join(",",
                        Permissions.canbuild,
                        Permissions.canregister,
                        Permissions.canlogin,
                        Permissions.canpartychat,
                        Permissions.cantalkinthird,
                        Permissions.canchat,
                        Permissions.synclocalarea,
                        Permissions.sendemoji,
                        Permissions.warp,
                        Permissions.summonboss,
                        Permissions.spawnpets,
                        Permissions.worldupgrades,
                        Permissions.startinvasion,
                        Permissions.whisper,
                        Permissions.wormhole,
                        Permissions.canpaint,
                        Permissions.pylon,
                        Permissions.whisper,
                        Permissions.wormhole,
                        Permissions.tppotion,
                        Permissions.magicconch,
                        Permissions.demonconch,
                        Permissions.movenpc,
                        Permissions.worldupgrades,
                        Permissions.rod,
                        Permissions.hurttownnpc,
                        Permissions.startdd2,
                        Permissions.spawnpets));

                AddDefaultGroup("newadmin", "vip",
                    string.Join(",",
                        Permissions.kick,
                        Permissions.editspawn,
                        Permissions.reservedslot,
                        Permissions.annoy,
                        Permissions.checkaccountinfo,
                        Permissions.getpos,
                        Permissions.mute,
                        Permissions.rod,
                        Permissions.savessc,
                        Permissions.seeids,
                        "tshock.world.time.*"));

                AddDefaultGroup("admin", "newadmin",
                    string.Join(",",
                        Permissions.ban,
                        Permissions.whitelist,
                        Permissions.spawnboss,
                        Permissions.spawnmob,
                        Permissions.managewarp,
                        Permissions.time,
                        Permissions.tp,
                        Permissions.slap,
                        Permissions.kill,
                        Permissions.logs,
                        Permissions.immunetokick,
                        Permissions.tpothers,
                        Permissions.advaccountinfo,
                        Permissions.broadcast,
                        Permissions.home,
                        Permissions.tpallothers,
                        Permissions.tpallow,
                        Permissions.tpnpc,
                        Permissions.tppos,
                        Permissions.tpsilent,
                        Permissions.userinfo,
                        Permissions.spawn));

                AddDefaultGroup("trustedadmin", "admin",
                    string.Join(",",
                        Permissions.maintenance,
                        "tshock.cfg.*",
                        "tshock.world.*",
                        Permissions.butcher,
                        Permissions.item,
                        Permissions.give,
                        Permissions.heal,
                        Permissions.immunetoban,
                        Permissions.usebanneditem,
                        Permissions.allowclientsideworldedit,
                        Permissions.buff,
                        Permissions.buffplayer,
                        Permissions.clear,
                        Permissions.clearangler,
                        Permissions.godmode,
                        Permissions.godmodeother,
                        Permissions.ignoredamagecap,
                        Permissions.ignorehp,
                        Permissions.ignorekilltiledetection,
                        Permissions.ignoreliquidsetdetection,
                        Permissions.ignoremp,
                        Permissions.ignorepaintdetection,
                        Permissions.ignoreplacetiledetection,
                        Permissions.ignoreprojectiledetection,
                        Permissions.ignorestackhackdetection,
                        Permissions.invade,
                        Permissions.startdd2,
                        Permissions.uploaddata,
                        Permissions.uploadothersdata,
                        Permissions.spawnpets,
                        Permissions.journey_timefreeze,
                        Permissions.journey_timeset,
                        Permissions.journey_timespeed,
                        Permissions.journey_godmode,
                        Permissions.journey_windstrength,
                        Permissions.journey_windfreeze,
                        Permissions.journey_rainstrength,
                        Permissions.journey_rainfreeze,
                        Permissions.journey_placementrange,
                        Permissions.journey_setdifficulty,
                        Permissions.journey_biomespreadfreeze,
                        Permissions.journey_setspawnrate,
                        Permissions.journey_contributeresearch));

                AddDefaultGroup("owner", "trustedadmin",
                    string.Join(",",
                        Permissions.su,
                        Permissions.allowdroppingbanneditems,
                        Permissions.antibuild,
                        Permissions.canusebannedprojectiles,
                        Permissions.canusebannedtiles,
                        Permissions.managegroup,
                        Permissions.manageitem,
                        Permissions.manageprojectile,
                        Permissions.manageregion,
                        Permissions.managetile,
                        Permissions.maxspawns,
                        Permissions.serverinfo,
                        Permissions.settempgroup,
                        Permissions.spawnrate,
                        Permissions.tpoverride,
                        Permissions.createdumps));
            }

            // Load Permissions from the DB
            LoadPermisions();

            Group.DefaultGroup = GetGroupByName(TShock.Config.GlobalSettings.DefaultGuestGroupName);

            AssertCoreGroupsPresent();
        }

        internal void AssertCoreGroupsPresent() {
            if (!GroupExists(TShock.Config.GlobalSettings.DefaultGuestGroupName)) {
                TShock.Log.Error(GetString("The guest group could not be found. This may indicate a typo in the configuration file, or that the group was renamed or deleted."));
                throw new Exception(GetString("The guest group could not be found."));
            }

            if (!GroupExists(TShock.Config.GlobalSettings.DefaultRegistrationGroupName)) {
                TShock.Log.Error(GetString("The default usergroup could not be found. This may indicate a typo in the configuration file, or that the group was renamed or deleted."));
                throw new Exception(GetString("The default usergroup could not be found."));
            }
        }

        /// <summary>
        /// Asserts that the group reference can be safely assigned to the player object.
        /// <para>If this assertion fails, and <paramref name="kick"/> is true, the player is disconnected. If <paramref name="kick"/> is false, the player will receive an error message.</para>
        /// </summary>
        /// <param name="player">The player in question</param>
        /// <param name="group">The group we want to assign them</param>
        /// <param name="kick">Whether or not failing this check disconnects the player.</param>
        /// <returns></returns>
        public bool AssertGroupValid(TSPlayer player, Group group, bool kick) {
            if (group == null) {
                if (kick)
                    player.Disconnect(GetString("Your account's group could not be loaded. Please contact server administrators about this."));
                else
                    player.SendErrorMessage(GetString("Your account's group could not be loaded. Please contact server administrators about this."));
                return false;
            }

            return true;
        }

        private void AddDefaultGroup(string name, string parent, string permissions) {
            if (!GroupExists(name))
                AddGroup(name, parent, permissions, Group.defaultChatColor);
        }

        /// <summary>
        /// Determines whether the given group exists.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <returns><c>true</c> if it does; otherwise, <c>false</c>.</returns>
        public bool GroupExists(string group) {
            if (group == "superadmin")
                return true;

            return groups.Any(g => g.Name.Equals(group));
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        /// <summary>
        /// Gets the enumerator.
        /// </summary>
        /// <returns>The enumerator.</returns>
        public IEnumerator<Group> GetEnumerator() {
            return groups.GetEnumerator();
        }

        /// <summary>
        /// Gets the group matching the specified name.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The group.</returns>
        public Group? GetGroupByName(string name) {
            var ret = groups.Where(g => g.Name == name);
            return 1 == ret.Count() ? ret.ElementAt(0) : null;
        }

        public void AddGroup(string name, string parentname, string permissions, string chatcolor) {
            if (GroupExists(name)) {
                throw new GroupExistsException(name);
            }

            var group = new Group(name, null, chatcolor) {
                Permissions = permissions
            };

            if (!string.IsNullOrWhiteSpace(parentname)) {
                var parent = groups.FirstOrDefault(gp => gp.Name == parentname);
                if (parent == null || name == parentname) {
                    var error = GetString($"Invalid parent group {parentname} for group {group.Name}");
                    TShock.Log.Error(error);
                    throw new GroupManagerException(error);
                }
                group.Parent = parent;
            }

            int inserted = groupListTable.Insert(() => new GroupList {
                GroupName = name,
                Parent = parentname,
                Commands = permissions,
                ChatColor = chatcolor
            });

            if (inserted == 1) {
                groups.Add(group);
            }
            else {
                throw new GroupManagerException(GetString($"Failed to add group {name}."));
            }
        }
        public void UpdateGroup(string name, string parentname, string permissions, string chatcolor, string suffix, string prefix) {
            var group = GetGroupByName(name);
            if (group == null)
                throw new GroupNotExistException(name);

            Group? parent = null;
            if (!string.IsNullOrWhiteSpace(parentname)) {
                parent = GetGroupByName(parentname);
                if (parent == null || parent == group)
                    throw new GroupManagerException(GetString($"Invalid parent group {parentname} for group {name}."));

                // Check if the new parent would cause loops.
                List<Group> groupChain = new List<Group> { group, parent };
                var checkingGroup = parent.Parent;
                while (checkingGroup != null) {
                    if (groupChain.Contains(checkingGroup))
                        throw new GroupManagerException(
                            GetString($"Parenting group {group} to {parentname} would cause loops in the parent chain."));

                    groupChain.Add(checkingGroup);
                    checkingGroup = checkingGroup.Parent;
                }
            }

            // Ensure any group validation is also persisted to the DB.
            var newGroup = new Group(name, parent, chatcolor, permissions) {
                Prefix = prefix,
                Suffix = suffix
            };

            int updated = groupListTable
                .Where(gl => gl.GroupName == name)
                .Set(gl => gl.Parent, parentname)
                .Set(gl => gl.Commands, newGroup.Permissions)
                .Set(gl => gl.ChatColor, newGroup.ChatColor)
                .Set(gl => gl.Suffix, suffix)
                .Set(gl => gl.Prefix, prefix)
                .Update();

            if (updated != 1)
                throw new GroupManagerException(GetString($"Failed to update group \"{name}\"."));

            group.ChatColor = chatcolor;
            group.Permissions = permissions;
            group.Parent = parent;
            group.Prefix = prefix;
            group.Suffix = suffix;
        }

        public string RenameGroup(string name, string newName) {
            if (!GroupExists(name)) {
                throw new GroupNotExistException(name);
            }

            if (GroupExists(newName)) {
                throw new GroupExistsException(newName);
            }

            try {
                groupListTable
                  .Where(gl => gl.GroupName == name)
                  .Set(gl => gl.GroupName, () => newName)
                  .Update();

                var oldGroup = GetGroupByName(name)!;
                var newGroup = new Group(newName, oldGroup.Parent, oldGroup.ChatColor, oldGroup.Permissions) {
                    Prefix = oldGroup.Prefix,
                    Suffix = oldGroup.Suffix
                };
                groups.Remove(oldGroup);
                groups.Add(newGroup);

                groupListTable
                  .Where(gl => gl.Parent == name)
                  .Set(gl => gl.Parent, () => newName)
                  .Update();

                foreach (var group in groups.Where(g => g.Parent != null && g.Parent == oldGroup)) {
                    group.Parent = newGroup;
                }

                bool anyChanged = false;
                if (TShock.Config.GlobalSettings.DefaultGuestGroupName == oldGroup.Name) {
                    TShock.Config.GlobalSettings.DefaultGuestGroupName = newGroup.Name;
                    anyChanged = true;
                    Group.DefaultGroup = newGroup;
                }
                if (TShock.Config.GlobalSettings.DefaultRegistrationGroupName == oldGroup.Name) {
                    TShock.Config.GlobalSettings.DefaultRegistrationGroupName = newGroup.Name;
                    anyChanged = true;
                }
                if (anyChanged) {
                    TShock.Config.SaveToFile();
                }

                TShock.UserAccounts.AfterRenameGroup(oldGroup.Name, newGroup.Name);

                foreach (var player in TShock.Players.Where(p => p?.Group == oldGroup)) {
                    player.Group = newGroup;
                }

                return GetString($"Group {name} has been renamed to {newName}.");
            }
            catch (Exception ex) {
                TShock.Log.Error(GetString($"An exception has occurred during database transaction: {ex.Message}"));
            }

            throw new GroupManagerException(GetString($"Failed to rename group {name}."));
        }
        public string DeleteGroup(string name, bool exceptions = false) {
            if (!GroupExists(name)) {
                if (exceptions)
                    throw new GroupNotExistException(name);
                return GetString($"Group {name} doesn't exist.");
            }

            if (name == Group.DefaultGroup?.Name) {
                if (exceptions)
                    throw new GroupManagerException(GetString("You can't remove the default guest group."));
                return GetString("You can't remove the default guest group.");
            }

            int affected = groupListTable
                .Where(g => g.GroupName == name)
                .Delete();

            if (affected == 1) {
                groups.Remove(GetGroupByName(name)!);
                return GetString($"Group {name} has been deleted successfully.");
            }

            if (exceptions)
                throw new GroupManagerException(GetString($"Failed to delete group {name}."));
            return GetString($"Failed to delete group {name}.");
        }
        public string AddPermissions(string name, List<string> permissions) {
            if (!GroupExists(name))
                return GetString($"Group {name} doesn't exist.");

            var group = GetGroupByName(name)!;
            var oldperms = group.Permissions; // Store old permissions in case of error
            permissions.ForEach(p => group.AddPermission(p));

            var updated = groupListTable
                .Where(gl => gl.GroupName == name)
                .Set(gl => gl.Commands, group.Permissions)
                .Update();

            if (updated == 1)
                return GetString($"Group {name} has been modified successfully.");

            // Restore old permissions so DB and internal object are in a consistent state
            group.Permissions = oldperms;
            return "";
        }
        public string DeletePermissions(string name, List<string> permissions) {
            if (!GroupExists(name))
                return GetString($"Group {name} doesn't exist.");

            var group = GetGroupByName(name)!;
            var oldperms = group.Permissions; // Store old permissions in case of error
            permissions.ForEach(group.RemovePermission);

            // Use LINQ2DB to update the Commands column for the group
            var updated = groupListTable
                .Where(gl => gl.GroupName == name)
                .Set(gl => gl.Commands, group.Permissions)
                .Update();

            if (updated == 1)
                return GetString($"Group {name} has been modified successfully.");

            // Restore old permissions so DB and internal object are in a consistent state
            group.Permissions = oldperms;
            return "";
        }
        public void LoadPermisions() {
            try {
                List<Group> newGroups = new List<Group>(groups.Count);
                Dictionary<string, string> newGroupParents = new Dictionary<string, string>(groups.Count);

                var groupEntries = groupListTable.ToList();

                foreach (var entry in groupEntries) {
                    string groupName = entry.GroupName;
                    if (groupName == "superadmin") {
                        TShock.Log.Warning(GetString("Group \"superadmin\" is defined in the database even though it's a reserved group name."));
                        continue;
                    }

                    newGroups.Add(new Group(groupName, null, entry.ChatColor, entry.Commands) {
                        Prefix = entry.Prefix,
                        Suffix = entry.Suffix,
                    });

                    try {
                        newGroupParents.Add(groupName, entry.Parent);
                    }
                    catch (ArgumentException) {
                        // Just in case somebody messed with the unique primary key.
                        TShock.Log.Error(GetString($"The group {groupName} appeared more than once. Keeping current group settings."));
                        return;
                    }
                }

                try {
                    // Get rid of deleted groups.
                    for (int i = 0; i < groups.Count; i++)
                        if (newGroups.All(g => g.Name != groups[i].Name))
                            groups.RemoveAt(i--);

                    // Apply changed group settings while keeping the current instances and add new groups.
                    foreach (Group newGroup in newGroups) {
                        var currentGroup = groups.FirstOrDefault(g => g.Name == newGroup.Name);
                        if (currentGroup != null)
                            newGroup.AssignTo(currentGroup);
                        else
                            groups.Add(newGroup);
                    }

                    // Resolve parent groups.
                    for (int i = 0; i < groups.Count; i++) {
                        Group group = groups[i];
                        if (!newGroupParents.TryGetValue(group.Name, out var parentGroupName) || string.IsNullOrEmpty(parentGroupName))
                            continue;

                        group.Parent = groups.FirstOrDefault(g => g.Name == parentGroupName);
                        if (group.Parent == null) {
                            TShock.Log.Error(
                                GetString($"Group {group.Name} is referencing a non existent parent group {parentGroupName}, parent reference was removed."));
                        }
                        else {
                            if (group.Parent == group)
                                TShock.Log.Warning(
                                    GetString($"Group {group.Name} is referencing itself as parent group; parent reference was removed."));

                            List<Group> groupChain = new List<Group> { group };
                            Group checkingGroup = group;
                            while (checkingGroup.Parent != null) {
                                if (groupChain.Contains(checkingGroup.Parent)) {
                                    TShock.Log.Error(
                                        GetString($"Group \"{checkingGroup.Name}\" is referencing parent group {checkingGroup.Parent.Name} which is already part of the parent chain. Parent reference removed."));

                                    checkingGroup.Parent = null;
                                    break;
                                }
                                groupChain.Add(checkingGroup);
                                checkingGroup = checkingGroup.Parent;
                            }
                        }
                    }
                }
                finally {
                    if (!groups.Any(g => g is SuperAdminGroup))
                        groups.Add(new SuperAdminGroup());
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(GetString($"Error on reloading groups: {ex}"));
            }
        }
    }


    /// <summary>
    /// Represents the base GroupManager exception.
    /// </summary>
    [Serializable]
    public class GroupManagerException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GroupManagerException"/> with the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public GroupManagerException(string message)
            : base(message) {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GroupManagerException"/> with the specified message and inner exception.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="inner">The inner exception.</param>
        public GroupManagerException(string message, Exception inner)
            : base(message, inner) {
        }
    }

    /// <summary>
    /// Represents the GroupExists exception.
    /// This exception is thrown whenever an attempt to add an existing group into the database is made.
    /// </summary>
    [Serializable]
    public class GroupExistsException : GroupManagerException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GroupExistsException"/> with the specified group name.
        /// </summary>
        /// <param name="name">The group name.</param>
        public GroupExistsException(string name)
            : base(GetString($"Group {name} already exists")) {
        }
    }

    /// <summary>
    /// Represents the GroupNotExist exception.
    /// This exception is thrown whenever we try to access a group that does not exist.
    /// </summary>
    [Serializable]
    public class GroupNotExistException : GroupManagerException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GroupNotExistException"/> with the specified group name.
        /// </summary>
        /// <param name="name">The group name.</param>
        public GroupNotExistException(string name)
            : base(GetString($"Group {name} does not exist")) {
        }
    }
}
