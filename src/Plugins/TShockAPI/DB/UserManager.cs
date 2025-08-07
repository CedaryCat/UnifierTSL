using BCrypt.Net;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using LinqToDB.SqlQuery;
using ReLogic.Peripherals.RGB.Logitech;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TShockAPI.Hooks;

namespace TShockAPI.DB
{
    public class UserAccountManager
    {
        [Table("Users")]
        class UserTable
        {
            [Column(DataType = DataType.Int32, IsPrimaryKey = true, IsIdentity = true)]
            public int ID { get; set; }

            [Column(DataType = DataType.VarChar, Length = 32)]
            public required string Username { get; set; }

            [Column(DataType = DataType.VarChar, Length = 128)]
            public required string Password { get; set; }

            [Column(DataType = DataType.VarChar, Length = 128)]
            public required string UUID { get; set; }

            [Column(DataType = DataType.Text)]
            public required string Usergroup { get; set; }

            [Column(DataType = DataType.Text)]
            public required string Registered { get; set; }

            [Column(DataType = DataType.Text)]
            public string LastAccessed { get; set; } = "";

            [Column(DataType = DataType.Text)]
            public string KnownIPs { get; set; } = "";
        }
        readonly DataConnection database;
        readonly ITable<UserTable> _tables;
        public UserAccountManager(DataConnection db) {
            database = db;
            _tables = database.CreateTable<UserTable>(tableOptions: TableOptions.CreateIfNotExists);
        }
        private UserAccount LoadUserAccountFromTable(UserAccount account, UserTable row) {
            account.ID = row.ID;
            account.Group = row.Usergroup;
            account.Password = row.Password;
            account.UUID = row.UUID;
            account.Name = row.Username;
            account.Registered = row.Registered;
            account.LastAccessed = row.LastAccessed;
            account.KnownIps = row.KnownIPs;
            return account;
        }

        internal void AfterRenameGroup(string oldGroup, string newGroup) {
            _tables
                .Where(u => u.Usergroup == oldGroup)
                .Set(u => u.Usergroup, newGroup)
                .Update();
        }

        public void AddUserAccount(UserAccount account) {
            if (!TShock.Groups.GroupExists(account.Group))
                throw new GroupNotExistsException(account.Group);

            // Username must be globally unique
            try {
                bool exists = _tables.Any(u => u.Username == account.Name);
                if (exists)
                    throw new UserAccountExistsException(account.Name);

                var now = DateTime.UtcNow.ToString("s");
                var newRow = new UserTable {
                    Username = account.Name,
                    Password = account.Password,
                    UUID = account.UUID,
                    Usergroup = account.Group,
                    Registered = now
                };

                int inserted = _tables.Insert(() => new UserTable {
                    Username = newRow.Username,
                    Password = newRow.Password,
                    UUID = newRow.UUID,
                    Usergroup = newRow.Usergroup,
                    Registered = newRow.Registered
                });

                if (inserted < 1)
                    throw new UserAccountExistsException(account.Name);

                Hooks.AccountHooks.OnAccountCreate(account);
            }
            catch (UserAccountExistsException) {
                throw;
            }
            catch (Exception ex) {
                // Some providers might still throw on uniqueness race; try to detect username collision by rechecking
                if (_tables.Any(u => u.Username == account.Name))
                    throw new UserAccountExistsException(account.Name);
                throw new UserAccountManagerException(GetString($"AddUser SQL returned an error ({ex.Message})"), ex);
            }
        }

        public void RemoveUserAccount(UserAccount account) {
            try {
                TShock.Players.Where(p => p?.IsLoggedIn == true && p.Account.Name == account.Name)
                              .ForEach(p => p.Logout());

                UserAccount tempuser = GetUserAccount(account);

                int affected = _tables.Where(u => u.Username == account.Name)
                                      .Delete();

                if (affected < 1)
                    throw new UserAccountNotExistException(account.Name);

                Hooks.AccountHooks.OnAccountDelete(tempuser);
            }
            catch (Exception ex) when (!(ex is UserAccountNotExistException)) {
                throw new UserAccountManagerException(GetString("RemoveUser SQL returned an error"), ex);
            }
        }

        public void SetUserAccountPassword(UserAccount account, string password) {
            try {
                account.CreateBCryptHash(password);

                int updated = _tables.Where(u => u.Username == account.Name)
                                     .Set(u => u.Password, account.Password)
                                     .Update();

                if (updated == 0)
                    throw new UserAccountNotExistException(account.Name);
            }
            catch (Exception ex) when (!(ex is UserAccountNotExistException)) {
                throw new UserAccountManagerException(GetString("SetUserPassword SQL returned an error"), ex);
            }
        }

        public void SetUserAccountUUID(UserAccount account, string uuid) {
            try {
                int updated = _tables.Where(u => u.Username == account.Name)
                                     .Set(u => u.UUID, uuid)
                                     .Update();

                if (updated == 0)
                    throw new UserAccountNotExistException(account.Name);
            }
            catch (Exception ex) when (ex is not UserAccountNotExistException) {
                throw new UserAccountManagerException(GetString("SetUserUUID SQL returned an error"), ex);
            }
        }

        public void SetUserGroup(UserAccount account, string group) {
            Group grp = TShock.Groups.GetGroupByName(group);
            if (null == grp)
                throw new GroupNotExistsException(group);

            if (AccountHooks.OnAccountGroupUpdate(account, ref grp))
                throw new UserGroupUpdateLockedException(account.Name);

            int updated = _tables.Where(u => u.Username == account.Name)
                                 .Set(u => u.Usergroup, grp.Name)
                                 .Update();

            if (updated == 0)
                throw new UserAccountNotExistException(account.Name);

            try {
                foreach (var player in TShock.Players.Where(p => p != null && p.Account != null && p.Account.Name == account.Name)) {
                    player.Group = grp;
                }
            }
            catch (Exception ex) {
                throw new UserAccountManagerException(GetString("SetUserGroup SQL returned an error"), ex);
            }
        }

        public void SetUserGroup(TSPlayer author, UserAccount account, string group) {
            Group grp = TShock.Groups.GetGroupByName(group);
            if (null == grp)
                throw new GroupNotExistsException(group);

            if (AccountHooks.OnAccountGroupUpdate(account, author, ref grp))
                throw new UserGroupUpdateLockedException(account.Name);

            int updated = _tables.Where(u => u.Username == account.Name)
                                 .Set(u => u.Usergroup, grp.Name)
                                 .Update();

            if (updated == 0)
                throw new UserAccountNotExistException(account.Name);

            try {
                foreach (var player in TShock.Players.Where(p => p != null && p.Account != null && p.Account.Name == account.Name)) {
                    player.Group = grp;
                }
            }
            catch (Exception ex) {
                throw new UserAccountManagerException(GetString("SetUserGroup SQL returned an error"), ex);
            }
        }

        public void UpdateLogin(UserAccount account) {
            try {
                var now = DateTime.UtcNow.ToString("s");
                int updated = _tables.Where(u => u.Username == account.Name)
                                     .Set(u => u.LastAccessed, now)
                                     .Set(u => u.KnownIPs, account.KnownIps)
                                     .Update();

                if (updated == 0)
                    throw new UserAccountNotExistException(account.Name);
            }
            catch (Exception ex) when (!(ex is UserAccountNotExistException)) {
                throw new UserAccountManagerException(GetString("UpdateLogin SQL returned an error"), ex);
            }
        }

        public int GetUserAccountID(string username) {
            try {
                var row = _tables.Where(u => u.Username == username)
                                 .FirstOrDefault();

                if (row != null)
                    return row.ID;
            }
            catch (Exception ex) {
                TShock.Log.Error(GetString($"FetchHashedPasswordAndGroup SQL returned an error: {ex}"));
            }
            return -1;
        }

        public UserAccount GetUserAccount(UserAccount account) {
            bool multiple = false;
            UserTable? row = null;
            string type;
            object arg;

            if (account.ID != 0) {
                row = _tables.Where(u => u.ID == account.ID).FirstOrDefault();
                type = "id";
                arg = account.ID;
            }
            else {
                row = _tables.Where(u => u.Username == account.Name).FirstOrDefault();
                type = "name";
                arg = account.Name;
            }

            try {
                if (row != null) {
                    // check for multiple matches manually (shouldn't happen unless data corruption)
                    IEnumerable<UserTable> matches;
                    if (account.ID != 0)
                        matches = _tables.Where(u => u.ID == account.ID).ToList();
                    else
                        matches = _tables.Where(u => u.Username == account.Name).ToList();

                    if (matches.Count() > 1)
                        multiple = true;

                    if (!multiple) {
                        return LoadUserAccountFromTable(account, row);
                    }
                }
            }
            catch (Exception ex) {
                throw new UserAccountManagerException(GetString($"GetUser SQL returned an error {ex.Message}"), ex);
            }

            if (multiple)
                throw new UserAccountManagerException(GetString($"Multiple user accounts found for {type} '{arg}'"));

            throw new UserAccountNotExistException(account.Name);
        }

        public List<UserAccount> GetUserAccounts() {
            try {
                return [.. _tables
                    .Select(u => new UserAccount {
                        ID = u.ID,
                        Group = u.Usergroup,
                        Password = u.Password,
                        UUID = u.UUID,
                        Name = u.Username,
                        Registered = u.Registered,
                        LastAccessed = u.LastAccessed,
                        KnownIps = u.KnownIPs
                    })];
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return [];
        }

        public List<UserAccount> GetUserAccountsByName(string username, bool notAtStart = false) {
            try {
                string pattern = notAtStart ? $"%{username}%" : $"{username}%";
                return [.. _tables
                    .Where(u => Sql.Like(u.Username, pattern))
                    .Select(u => new UserAccount {
                        ID = u.ID,
                        Group = u.Usergroup,
                        Password = u.Password,
                        UUID = u.UUID,
                        Name = u.Username,
                        Registered = u.Registered,
                        LastAccessed = u.LastAccessed,
                        KnownIps = u.KnownIPs
                    })];
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return [];
        }

        /// <summary>Gets a user account object by name.</summary>
        /// <param name="name">The user's name.</param>
        /// <returns>The user account object returned from the search.</returns>
        public UserAccount? GetUserAccountByName(string name) {
            try {
                return GetUserAccount(new UserAccount { Name = name });
            }
            catch (UserAccountManagerException) {
                return null;
            }
        }

        /// <summary>Gets a user account object by their user account ID.</summary>
        /// <param name="id">The user's ID.</param>
        /// <returns>The user account object returned from the search.</returns>
        public UserAccount? GetUserAccountByID(int id) {
            try {
                return GetUserAccount(new UserAccount { ID = id });
            }
            catch (UserAccountManagerException) {
                return null;
            }
        }
    }

    /// <summary>A database user account.</summary>
    public class UserAccount : IEquatable<UserAccount>
    {
        /// <summary>The database ID of the user account.</summary>
        public int ID { get; set; }

        /// <summary>The user's name.</summary>
        public string Name { get; set; }

        /// <summary>The hashed password for the user account.</summary>
        public string Password { get; internal set; }

        /// <summary>The user's saved Universally Unique Identifier token.</summary>
        public string UUID { get; set; }

        /// <summary>The group object that the user account is a part of.</summary>
        public string Group { get; set; }

        /// <summary>The unix epoch corresponding to the registration date of the user account.</summary>
        public string Registered { get; set; }

        /// <summary>The unix epoch corresponding to the last access date of the user account.</summary>
        public string LastAccessed { get; set; }

        /// <summary>A JSON serialized list of known IP addresses for a user account.</summary>
        public string KnownIps { get; set; }

        /// <summary>Constructor for the user account object, assuming you define everything yourself.</summary>
        /// <param name="name">The user's name.</param>
        /// <param name="pass">The user's password hash.</param>
        /// <param name="uuid">The user's UUID.</param>
        /// <param name="group">The user's group name.</param>
        /// <param name="registered">The unix epoch for the registration date.</param>
        /// <param name="last">The unix epoch for the last access date.</param>
        /// <param name="known">The known IPs for the user account, serialized as a JSON object</param>
        /// <returns>A completed user account object.</returns>
        public UserAccount(string name, string pass, string uuid, string group, string registered, string last, string known) {
            Name = name;
            Password = pass;
            UUID = uuid;
            Group = group;
            Registered = registered;
            LastAccessed = last;
            KnownIps = known;
        }

        /// <summary>Default constructor for a user account object; holds no data.</summary>
        /// <returns>A user account object.</returns>
        public UserAccount() {
            Name = "";
            Password = "";
            UUID = "";
            Group = "";
            Registered = "";
            LastAccessed = "";
            KnownIps = "";
        }

        /// <summary>
        /// Verifies if a password matches the one stored in the database.
        /// If the password is stored in an unsafe hashing algorithm, it will be converted to BCrypt.
        /// If the password is stored using BCrypt, it will be re-saved if the work factor in the config
        /// is greater than the existing work factor with the new work factor.
        /// </summary>
        /// <param name="password">The password to check against the user account object.</param>
        /// <returns>bool true, if the password matched, or false, if it didn't.</returns>
        public bool VerifyPassword(string password) {
            try {
                if (BCrypt.Net.BCrypt.Verify(password, Password)) {
                    // If necessary, perform an upgrade to the highest work factor.
                    UpgradePasswordWorkFactor(password);
                    return true;
                }
            }
            catch (SaltParseException) {
                TShock.Log.Error(GetString($"Unable to verify the password hash for user {Name} ({ID})"));
                return false;
            }
            return false;
        }

        /// <summary>Upgrades a password to the highest work factor available in the config.</summary>
        /// <param name="password">The raw user account password (unhashed) to upgrade</param>
        protected void UpgradePasswordWorkFactor(string password) {
            // If the destination work factor is not greater, we won't upgrade it or re-hash it
            int currentWorkFactor;
            try {
                currentWorkFactor = int.Parse((Password.Split('$')[2]));
            }
            catch (FormatException) {
                TShock.Log.Warning(GetString("Not upgrading work factor because bcrypt hash in an invalid format."));
                return;
            }

            if (currentWorkFactor < TShock.Config.GlobalSettings.BCryptWorkFactor) {
                try {
                    TShock.UserAccounts.SetUserAccountPassword(this, password);
                }
                catch (UserAccountManagerException e) {
                    TShock.Log.Error(e.ToString());
                }
            }
        }

        /// <summary>Creates a BCrypt hash for a user account and stores it in this object.</summary>
        /// <param name="password">The plain text password to hash</param>
        public void CreateBCryptHash(string password) {
            if (password.Trim().Length < Math.Max(4, TShock.Config.GlobalSettings.MinimumPasswordLength)) {
                int minLength = TShock.Config.GlobalSettings.MinimumPasswordLength;
                throw new ArgumentOutOfRangeException("password", GetString($"Password must be at least {minLength} characters."));
            }
            try {
                Password = BCrypt.Net.BCrypt.HashPassword(password.Trim(), TShock.Config.GlobalSettings.BCryptWorkFactor);
            }
            catch (ArgumentOutOfRangeException) {
                TShock.Log.Error(GetString("Invalid BCrypt work factor in config file! Creating new hash using default work factor."));
                Password = BCrypt.Net.BCrypt.HashPassword(password.Trim());
            }
        }

        /// <summary>Creates a BCrypt hash for a user account and stores it in this object.</summary>
        /// <param name="password">The plain text password to hash</param>
        /// <param name="workFactor">The work factor to use in generating the password hash</param>
        public void CreateBCryptHash(string password, int workFactor) {
            if (password.Trim().Length < Math.Max(4, TShock.Config.GlobalSettings.MinimumPasswordLength)) {
                int minLength = TShock.Config.GlobalSettings.MinimumPasswordLength;
                throw new ArgumentOutOfRangeException("password", GetString($"Password must be at least {minLength} characters."));
            }
            Password = BCrypt.Net.BCrypt.HashPassword(password.Trim(), workFactor);
        }

        #region IEquatable

        /// <summary>Indicates whether the current <see cref="UserAccount"/> is equal to another <see cref="UserAccount"/>.</summary>
        /// <returns>true if the <see cref="UserAccount"/> is equal to the <paramref name="other" /> parameter; otherwise, false.</returns>
        /// <param name="other">An <see cref="UserAccount"/> to compare with this <see cref="UserAccount"/>.</param>
        public bool Equals(UserAccount? other) {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return ID == other.ID && string.Equals(Name, other.Name);
        }

        /// <summary>Indicates whether the current <see cref="UserAccount"/> is equal to another object.</summary>
        /// <returns>true if the <see cref="UserAccount"/> is equal to the <paramref name="obj" /> parameter; otherwise, false.</returns>
        /// <param name="obj">An <see cref="object"/> to compare with this <see cref="UserAccount"/>.</param>
        public override bool Equals(object? obj) {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj is not UserAccount other) return false;
            return Equals(other);
        }

        /// <summary>Serves as the hash function. </summary>
        /// <returns>A hash code for the current <see cref="UserAccount"/>.</returns>
        public override int GetHashCode() {
            unchecked {
                return (ID * 397) ^ (Name != null ? Name.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Compares equality of two <see cref="UserAccount"/> objects.
        /// </summary>
        /// <param name="left">Left hand of the comparison.</param>
        /// <param name="right">Right hand of the comparison.</param>
        /// <returns>true if the <see cref="UserAccount"/> objects are equal; otherwise, false.</returns>
        public static bool operator ==(UserAccount? left, UserAccount? right) {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares equality of two <see cref="UserAccount"/> objects.
        /// </summary>
        /// <param name="left">Left hand of the comparison.</param>
        /// <param name="right">Right hand of the comparison.</param>
        /// <returns>true if the <see cref="UserAccount"/> objects aren't equal; otherwise, false.</returns>
        public static bool operator !=(UserAccount? left, UserAccount? right) {
            return !Equals(left, right);
        }

        #endregion

        /// <summary>
        /// Converts the UserAccount to it's string representation
        /// </summary>
        /// <returns>Returns the UserAccount string representation</returns>
        public override string ToString() => Name;
    }

    /// <summary>UserAccountManagerException - An exception generated by the user account manager.</summary>
    [Serializable]
    public class UserAccountManagerException : Exception
    {
        /// <summary>Creates a new UserAccountManagerException object.</summary>
        /// <param name="message">The message for the object.</param>
        /// <returns>A new UserAccountManagerException object.</returns>
        public UserAccountManagerException(string message)
            : base(message) {
        }

        /// <summary>Creates a new UserAccountManager Object with an internal exception.</summary>
        /// <param name="message">The message for the object.</param>
        /// <param name="inner">The inner exception for the object.</param>
        /// <returns>A new UserAccountManagerException with a defined inner exception.</returns>
        public UserAccountManagerException(string message, Exception inner)
            : base(message, inner) {
        }
    }

    /// <summary>A UserExistsException object, used when a user account already exists when attempting to create a new one.</summary>
    [Serializable]
    public class UserAccountExistsException : UserAccountManagerException
    {
        /// <summary>Creates a new UserAccountExistsException object.</summary>
        /// <param name="name">The name of the user account that already exists.</param>
        /// <returns>A UserAccountExistsException object with the user's name passed in the message.</returns>
        public UserAccountExistsException(string name)
            : base(GetString($"User account {name} already exists")) {
        }
    }

    /// <summary>A UserNotExistException, used when a user does not exist and a query failed as a result of it.</summary>
    [Serializable]
    public class UserAccountNotExistException : UserAccountManagerException
    {
        /// <summary>Creates a new UserAccountNotExistException object, with the user account name in the message.</summary>
        /// <param name="name">The user account name to be passed in the message.</param>
        /// <returns>A new UserAccountNotExistException object with a message containing the user account name that does not exist.</returns>
        public UserAccountNotExistException(string name)
            : base(GetString($"User account {name} does not exist")) {
        }
    }

    /// <summary>The UserGroupUpdateLockedException used when the user group update failed and the request failed as a result.</summary>.
    [Serializable]
    public class UserGroupUpdateLockedException : UserAccountManagerException
    {
        /// <summary>Creates a new UserGroupUpdateLockedException object.</summary>
        /// <param name="name">The name of the user who failed to change the group.</param>
        /// <returns>New UserGroupUpdateLockedException object with a message containing the name of the user account that failed to change the group.</returns>
        public UserGroupUpdateLockedException(string name) :
            base(GetString($"Unable to update group of user {name}.")) {
        }
    }


    /// <summary>A GroupNotExistsException, used when a group does not exist.</summary>
    [Serializable]
    public class GroupNotExistsException : UserAccountManagerException
    {
        /// <summary>Creates a new GroupNotExistsException object with the group's name in the message.</summary>
        /// <param name="group">The group name.</param>
        /// <returns>A new GroupNotExistsException with the group that does not exist's name in the message.</returns>
        public GroupNotExistsException(string group)
            : base(GetString($"Group {group} does not exist")) {
        }
    }
}
