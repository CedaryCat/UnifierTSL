using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SqlServer;
using LinqToDB.Mapping;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using NotNull = LinqToDB.Mapping.NotNullAttribute;

namespace TShockAPI.DB
{
    /// <summary>
    /// Model class that represents a ban entry in the TShock database.
    /// </summary>
    public class Ban
    {
        /// <summary>
        /// A unique ID assigned to this ban
        /// </summary>
        public int TicketNumber { get; set; }

        /// <summary>
        /// An identifiable piece of information to ban
        /// </summary>
        public string Identifier { get; set; }

        /// <summary>
        /// Gets or sets the ban reason.
        /// </summary>
        /// <value>The ban reason.</value>
        public string? Reason { get; set; }

        /// <summary>
        /// Gets or sets the name of the user who added this ban entry.
        /// </summary>
        /// <value>The banning user.</value>
        public string? BanningUser { get; set; }

        /// <summary>
        /// DateTime from which the ban will take effect
        /// </summary>
        public DateTime BanDateTime { get; set; }

        /// <summary>
        /// DateTime at which the ban will end
        /// </summary>
        public DateTime ExpirationDateTime { get; set; }

        /// <summary>
        /// Returns a string in the format dd:mm:hh:ss indicating the time until the ban expires.
        /// If the ban is not set to expire (ExpirationDateTime == DateTime.MaxValue), returns the string 'Never'
        /// </summary>
        /// <returns></returns>
        public string GetPrettyExpirationString() {
            if (ExpirationDateTime == DateTime.MaxValue) {
                return "Never";
            }

            TimeSpan ts = (ExpirationDateTime - DateTime.UtcNow).Duration(); // Use duration to avoid pesky negatives for expired bans
            return $"{ts.Days:00}:{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        }

        /// <summary>
        /// Returns a string in the format dd:mm:hh:ss indicating the time elapsed since the ban was added.
        /// </summary>
        /// <returns></returns>
        public string GetPrettyTimeSinceBanString() {
            TimeSpan ts = (DateTime.UtcNow - BanDateTime).Duration();
            return $"{ts.Days:00}:{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TShockAPI.DB.Ban"/> class.
        /// </summary>
        /// <param name="ticketNumber">Unique ID assigned to the ban</param>
        /// <param name="identifier">Identifier to apply the ban to</param>
        /// <param name="reason">Reason for the ban</param>
        /// <param name="banningUser">Account name that executed the ban</param>
        /// <param name="start">System ticks at which the ban began</param>
        /// <param name="end">System ticks at which the ban will end</param>
        public Ban(int ticketNumber, string identifier, string reason, string banningUser, long start, long end)
            : this(ticketNumber, identifier, reason, banningUser, new DateTime(start), new DateTime(end)) {
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="TShockAPI.DB.Ban"/> class.
        /// </summary>
        /// <param name="ticketNumber">Unique ID assigned to the ban</param>
        /// <param name="identifier">Identifier to apply the ban to</param>
        /// <param name="reason">Reason for the ban</param>
        /// <param name="banningUser">Account name that executed the ban</param>
        /// <param name="start">DateTime at which the ban will start</param>
        /// <param name="end">DateTime at which the ban will end</param>
        public Ban(int ticketNumber, string identifier, string? reason, string? banningUser, DateTime start, DateTime end) {
            TicketNumber = ticketNumber;
            Identifier = identifier;
            Reason = reason;
            BanningUser = banningUser;
            BanDateTime = start;
            ExpirationDateTime = end;
        }
    }

    public class BanManager
    {

        [Table(Name = "PlayerBans")]
        private class PlayerBan
        {
            [PrimaryKey, Identity]
            public int TicketNumber { get; set; }

            [Column, NotNull]
            public string Identifier { get; set; } = null!;

            [Column, Nullable]
            public string? Reason { get; set; }

            [Column, Nullable]
            public string? BanningUser { get; set; }

            // Storing as ticks or epoch? Keep same semantics as original (Int64)
            [Column, NotNull]
            public long Date { get; set; }

            [Column, NotNull]
            public long Expiration { get; set; }
        }

        ITable<PlayerBan> bansTable;
        private readonly DataConnection db;

        private Dictionary<int, Ban> _bans;

        /// <summary>
        /// Readonly dictionary of Bans, keyed on ban ticket number.
        /// </summary>
        public ReadOnlyDictionary<int, Ban> Bans => new ReadOnlyDictionary<int, Ban>(_bans);

        /// <summary>
        /// Event invoked when a ban is checked for validity
        /// </summary>
        public static event EventHandler<BanEventArgs>? OnBanValidate;
        /// <summary>
        /// Event invoked before a ban is added
        /// </summary>
        public static event EventHandler<BanPreAddEventArgs>? OnBanPreAdd;
        /// <summary>
        /// Event invoked after a ban is added
        /// </summary>
        public static event EventHandler<BanEventArgs>? OnBanPostAdd;

        public BanManager(DataConnection dataConnection) {
            db = dataConnection;
            bansTable = db.CreateTable<PlayerBan>(tableOptions: TableOptions.CreateIfNotExists);

            UpdateBans();

            OnBanValidate += BanValidateCheck;
            OnBanPreAdd += BanAddedCheck;
        }


        /// <summary>
        /// Updates the <see cref="_bans"/> collection from database.
        /// </summary>
        [MemberNotNull(nameof(_bans))]
        public void UpdateBans() {
            _bans = RetrieveAllBans().ToDictionary(b => b.TicketNumber);
        }

        internal bool CheckBan(TSPlayer player) {
            List<string> identifiers = new List<string>
            {
                $"{Identifier.Name}{player.Name}",
                $"{Identifier.IP}{player.IP}"
            };

            if (player.UUID != null && player.UUID.Length > 0) {
                identifiers.Add($"{Identifier.UUID}{player.UUID}");
            }

            if (player.Account != null) {
                identifiers.Add($"{Identifier.Account}{player.Account.Name}");
            }

            Ban ban = Bans.FirstOrDefault(b => identifiers.Contains(b.Value.Identifier) && IsValidBan(b.Value, player)).Value;

            if (ban != null) {
                if (ban.ExpirationDateTime == DateTime.MaxValue) {
                    player.Disconnect(GetParticularString("{0} is ban number, {1} is ban reason", $"#{ban.TicketNumber} - You are banned: {ban.Reason}"));
                    return true;
                }

                TimeSpan ts = ban.ExpirationDateTime - DateTime.UtcNow;
                player.Disconnect(GetParticularString("{0} is ban number, {1} is ban reason, {2} is a timestamp", $"#{ban.TicketNumber} - You are banned: {ban.Reason} ({ban.GetPrettyExpirationString()} remaining)"));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether or not a ban is valid
        /// </summary>
        /// <param name="ban"></param>
        /// <param name="player"></param>
        /// <returns></returns>
        public bool IsValidBan(Ban ban, TSPlayer player) {
            BanEventArgs args = new BanEventArgs {
                Ban = ban,
                Player = player
            };

            OnBanValidate?.Invoke(this, args);

            return args.Valid;
        }

        internal void BanValidateCheck(object? sender, BanEventArgs args) {
            //Only perform validation if the event has not been cancelled before we got here
            if (args.Valid) {
                //We consider a ban to be valid if the start time is before now and the end time is after now
                args.Valid = (DateTime.UtcNow > args.Ban.BanDateTime && DateTime.UtcNow < args.Ban.ExpirationDateTime);
            }
        }

        internal void BanAddedCheck(object? sender, BanPreAddEventArgs args) {
            //Only perform validation if the event has not been cancelled before we got here
            if (args.Valid) {
                //We consider a ban valid to add if no other *current* bans exist for the identifier provided.
                //E.g., if a previous ban has expired, a new ban is valid.
                //However, if a previous ban on the provided identifier is still in effect, a new ban is not valid
                args.Valid = !Bans.Any(b => b.Value.Identifier == args.Identifier && b.Value.ExpirationDateTime > DateTime.UtcNow);
                args.Message = args.Valid ? null : GetString("The ban is invalid because a current ban for this identifier already exists.");
            }
        }

        /// <summary>
        /// Adds a new ban for the given identifier. Returns a Ban object if the ban was added, else null
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="reason"></param>
        /// <param name="banningUser"></param>
        /// <param name="fromDate"></param>
        /// <param name="toDate"></param>
        /// <returns></returns>
        public AddBanResult InsertBan(string identifier, string? reason, string? banningUser, DateTime fromDate, DateTime toDate) {
            BanPreAddEventArgs args = new BanPreAddEventArgs {
                Identifier = identifier,
                Reason = reason,
                BanningUser = banningUser,
                BanDateTime = fromDate,
                ExpirationDateTime = toDate
            };
            return InsertBan(args);
        }

        public AddBanResult InsertBan(BanPreAddEventArgs args) {
            OnBanPreAdd?.Invoke(this, args);

            if (!args.Valid) {
                string message = args.Message ?? GetString("The ban was not valid for an unknown reason.");
                return new AddBanResult { Message = message };
            }

            try {
                var playerBan = new PlayerBan {
                    Identifier = args.Identifier,
                    Reason = args.Reason,
                    BanningUser = args.BanningUser,
                    Date = args.BanDateTime.Ticks,
                    Expiration = args.ExpirationDateTime.Ticks
                };

                var insertedIdObj = db.InsertWithIdentity(playerBan);
                if (insertedIdObj == null) {
                    return new AddBanResult { Message = GetString("Inserting the ban into the database failed.") };
                }

                int ticketId = Convert.ToInt32(insertedIdObj);

                var b = new Ban(ticketId, args.Identifier, args.Reason, args.BanningUser, args.BanDateTime, args.ExpirationDateTime);
                _bans[ticketId] = b;

                OnBanPostAdd?.Invoke(this, new BanEventArgs { Ban = b, Player = null });

                return new AddBanResult { Ban = b };
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
                return new AddBanResult { Message = GetString("Inserting the ban into the database failed.") };
            }
        }

        public bool RemoveBan(int ticketNumber, bool fullDelete = false) {
            try {
                if (fullDelete) {
                    int rows = db
                        .GetTable<PlayerBan>()
                        .Where(pb => pb.TicketNumber == ticketNumber)
                        .Delete();

                    _bans.Remove(ticketNumber);
                    return rows > 0;
                }
                else {
                    var nowTicks = DateTime.UtcNow.Ticks;

                    int rows = db
                        .GetTable<PlayerBan>()
                            .Where(pb => pb.TicketNumber == ticketNumber)
                            .Set(pb => pb.Expiration, nowTicks)
                            .Update();

                    if (_bans.TryGetValue(ticketNumber, out var existingBan)) {
                        existingBan.ExpirationDateTime = DateTime.UtcNow;
                    }

                    return rows > 0;
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
                return false;
            }
        }

        public Ban? GetBanById(int id) {
            if (_bans.TryGetValue(id, out var cached))
                return cached;

            var pb = db.GetTable<PlayerBan>()
                       .FirstOrDefault(b => b.TicketNumber == id);

            if (pb != null) {
                var ban = new Ban(pb.TicketNumber, pb.Identifier, pb.Reason, pb.BanningUser,
                                  new DateTime(pb.Date, DateTimeKind.Utc),
                                  new DateTime(pb.Expiration, DateTimeKind.Utc));
                return ban;
            }

            return null;
        }

        public IEnumerable<Ban> RetrieveBansByIdentifier(string identifier, bool currentOnly = true) {
            var query = db.GetTable<PlayerBan>()
                          .Where(pb => pb.Identifier == identifier);

            if (currentOnly) {
                var nowTicks = DateTime.UtcNow.Ticks;
                query = query.Where(pb => pb.Expiration > nowTicks);
            }

            foreach (var pb in query) {
                yield return new Ban(pb.TicketNumber, pb.Identifier, pb.Reason, pb.BanningUser,
                                     new DateTime(pb.Date, DateTimeKind.Utc),
                                     new DateTime(pb.Expiration, DateTimeKind.Utc));
            }
        }

        public IEnumerable<Ban> GetBansByIdentifiers(bool currentOnly = true, params string[] identifiers) {
            if (identifiers == null || identifiers.Length == 0)
                yield break;

            var query = db.GetTable<PlayerBan>()
                          .Where(pb => identifiers.Contains(pb.Identifier));

            if (currentOnly) {
                var nowTicks = DateTime.UtcNow.Ticks;
                query = query.Where(pb => pb.Expiration > nowTicks);
            }

            foreach (var pb in query) {
                yield return new Ban(pb.TicketNumber, pb.Identifier, pb.Reason, pb.BanningUser,
                                     new DateTime(pb.Date, DateTimeKind.Utc),
                                     new DateTime(pb.Expiration, DateTimeKind.Utc));
            }
        }

        public IEnumerable<Ban> RetrieveAllBans() => RetrieveAllBansSorted(BanSortMethod.AddedNewestToOldest);

        public IEnumerable<Ban> RetrieveAllBansSorted(BanSortMethod sortMethod) {
            List<Ban> banlist = [];
            try {
                IQueryable<PlayerBan> query = db.GetTable<PlayerBan>();

                query = sortMethod switch {
                    BanSortMethod.AddedNewestToOldest => query.OrderByDescending(pb => pb.Date),
                    BanSortMethod.AddedOldestToNewest => query.OrderBy(pb => pb.Date),
                    BanSortMethod.ExpirationSoonestToLatest => query.OrderBy(pb => pb.Expiration),
                    BanSortMethod.ExpirationLatestToSoonest => query.OrderByDescending(pb => pb.Expiration),
                    _ => query
                };

                foreach (var pb in query) {
                    banlist.Add(new Ban(pb.TicketNumber, pb.Identifier, pb.Reason, pb.BanningUser,
                        new DateTime(pb.Date, DateTimeKind.Utc),
                        new DateTime(pb.Expiration, DateTimeKind.Utc)));
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
                Console.WriteLine(ex.StackTrace);
            }
            return banlist;
        }

        public bool ClearBans() {
            try {
                int deleted = db.GetTable<PlayerBan>().Delete();
                if (deleted > 0) {
                    _bans.Clear();
                    return true;
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return false;
        }
    }
    /// <summary>
    /// Enum containing sort options for ban retrieval
    /// </summary>
    public enum BanSortMethod
    {
        /// <summary>
        /// Bans will be sorted on expiration date, from soonest to latest
        /// </summary>
        ExpirationSoonestToLatest,
        /// <summary>
        /// Bans will be sorted on expiration date, from latest to soonest
        /// </summary>
        ExpirationLatestToSoonest,
        /// <summary>
        /// Bans will be sorted by the date they were added, from newest to oldest
        /// </summary>
        AddedNewestToOldest,
        /// <summary>
        /// Bans will be sorted by the date they were added, from oldest to newest
        /// </summary>
        AddedOldestToNewest,
        /// <summary>
        /// Bans will be sorted by their ticket number
        /// </summary>
        TicketNumber
    }

    /// <summary>
    /// Result of an attempt to add a ban
    /// </summary>
    public class AddBanResult
    {
        /// <summary>
        /// Message generated from the attempt
        /// </summary>
        public string? Message { get; set; }
        /// <summary>
        /// Ban object generated from the attempt, or null if the attempt failed
        /// </summary>
        public Ban? Ban { get; set; }
    }

    /// <summary>
    /// Event args used for completed bans
    /// </summary>
    public class BanEventArgs : EventArgs
    {
        /// <summary>
        /// Complete ban object
        /// </summary>
        public required Ban Ban { get; set; }

        /// <summary>
        /// Player ban is being applied to
        /// </summary>
        public required TSPlayer? Player { get; set; }

        /// <summary>
        /// Whether or not the operation should be considered to be valid
        /// </summary>
        public bool Valid { get; set; } = true;
    }

    /// <summary>
    /// Event args used for ban data prior to a ban being formalized
    /// </summary>
    public class BanPreAddEventArgs : EventArgs
    {
        /// <summary>
        /// An identifiable piece of information to ban
        /// </summary>
        public required string Identifier { get; set; }

        /// <summary>
        /// Gets or sets the ban reason.
        /// </summary>
        /// <value>The ban reason.</value>
        public required string? Reason { get; set; }

        /// <summary>
        /// Gets or sets the name of the user who added this ban entry.
        /// </summary>
        /// <value>The banning user.</value>
        public required string? BanningUser { get; set; }

        /// <summary>
        /// DateTime from which the ban will take effect
        /// </summary>
        public required DateTime BanDateTime { get; set; }

        /// <summary>
        /// DateTime at which the ban will end
        /// </summary>
        public required DateTime ExpirationDateTime { get; set; }

        /// <summary>
        /// Whether or not the operation should be considered to be valid
        /// </summary>
        public bool Valid { get; set; } = true;

        /// <summary>
        /// Optional message to explain why the event was invalidated, if it was
        /// </summary>
        public string? Message { get; set; }
    }

    /// <summary>
    /// Describes an identifier used by the ban system
    /// </summary>
    public class Identifier
    {
        /// <summary>
        /// Identifiers currently registered
        /// </summary>
        public static List<Identifier> Available = new List<Identifier>();

        /// <summary>
        /// The prefix of the identifier. E.g, 'ip:'
        /// </summary>
        public string Prefix { get; }
        /// <summary>
        /// Short description of the identifier and its basic usage
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// IP identifier
        /// </summary>
        public static Identifier IP = Register("ip:", GetString($"An identifier for an IP Address in octet format. e.g., '{"127.0.0.1".Color(Utils.RedHighlight)}'."));
        /// <summary>
        /// UUID identifier
        /// </summary>
        public static Identifier UUID = Register("uuid:", GetString("An identifier for a UUID."));
        /// <summary>
        /// Player name identifier
        /// </summary>
        public static Identifier Name = Register("name:", GetString("An identifier for a character name."));
        /// <summary>
        /// User account identifier
        /// </summary>
        public static Identifier Account = Register("acc:", GetString("An identifier for a TShock User Account name."));

        private Identifier(string prefix, string description) {
            Prefix = prefix;
            Description = description;
        }

        /// <summary>
        /// Returns the identifier's prefix
        /// </summary>
        /// <returns></returns>
        public override string ToString() {
            return Prefix;
        }

        /// <summary>
        /// Registers a new identifier with the given prefix and description
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="description"></param>
        public static Identifier Register(string prefix, string description) {
            var ident = new Identifier(prefix, description);
            Available.Add(ident);

            return ident;
        }
    }
}
