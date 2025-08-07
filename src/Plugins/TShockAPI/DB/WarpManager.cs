using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TShockAPI.DB
{
    public class WarpManager
    {
        [Table(Name = "Warps")]
        public class WarpTable
        {
            [Column(DataType = DataType.Int32, IsPrimaryKey = true, IsIdentity = true)]
            public int Id { get; set; }

            [Column(Name = "WarpName", DataType = DataType.VarChar, Length = 50)]
            public required string WarpName { get; set; }

            [Column(DataType = DataType.Int32)]
            public int X { get; set; }

            [Column(DataType = DataType.Int32)]
            public int Y { get; set; }

            [Column(Name = "WorldID", DataType = DataType.VarChar, Length = 50)]
            public required string WorldID { get; set; }

            [Column(DataType = DataType.Text)]
            public required string Private { get; set; }
        }
        readonly DataConnection database;
        readonly ITable<WarpTable> table;
        internal readonly List<Warp> Warps = [];
        public WarpManager(DataConnection db) {
            database = db;
            table = database.CreateTable<WarpTable>(tableOptions: TableOptions.CreateIfNotExists);
        }

        /// <summary>
        /// Adds a warp.
        /// </summary>
        /// <param name="x">The X position.</param>
        /// <param name="y">The Y position.</param>
        /// <param name="name">The name.</param>
        /// <returns>Whether the operation succeeded.</returns>
        public bool Add(string worldId, int x, int y, string name) {
            try {
                if (table.Insert(() => new WarpTable {
                    X = x,
                    Y = y,
                    WarpName = name,
                    WorldID = worldId,
                    Private = "0"
                }) > 0) {
                    Warps.Add(new Warp(worldId, new Point(x, y), name));
                    return true;
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return false;
        }

        /// <summary>
        /// Reloads all warps.
        /// </summary>
        public void ReloadWarps() {
            Warps.Clear();

            var warps = table.ToList();
            foreach (var warp in warps) {
                Warps.Add(new Warp(
                    warp.WorldID,
                    new Point(warp.X, warp.Y),
                    warp.WarpName,
                    (warp.Private ?? "0") != "0"));
            }
        }

        /// <summary>
        /// Removes a warp.
        /// </summary>
        /// <param name="warpName">The warp name.</param>
        /// <returns>Whether the operation succeeded.</returns>
        public bool Remove(string worldId, string warpName) {
            try {
                if (table.Where(w => w.WarpName == warpName && w.WorldID == worldId)
                        .Delete() > 0) {
                    Warps.RemoveAll(w => string.Equals(w.Name, warpName, StringComparison.OrdinalIgnoreCase));
                    return true;
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return false;
        }

        /// <summary>
        /// Finds the warp with the given name.
        /// </summary>
        /// <param name="warpName">The name.</param>
        /// <returns>The warp, if it exists, or else null.</returns>
        public Warp? Find(string worldId, string warpName) {
            return Warps.Find(w => w.WorldID == worldId && string.Equals(w.Name, warpName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Sets the position of a warp.
        /// </summary>
        /// <param name="warpName">The warp name.</param>
        /// <param name="x">The X position.</param>
        /// <param name="y">The Y position.</param>
        /// <returns>Whether the operation succeeded.</returns>
        public bool Position(string worldId, string warpName, int x, int y) {
            try {
                if (table.Where(w => w.WarpName == warpName && w.WorldID == worldId)
                        .Set(w => w.X, x)
                        .Set(w => w.Y, y)
                        .Update() > 0) {
                    Warps.Find(w => string.Equals(w.Name, warpName, StringComparison.OrdinalIgnoreCase))!.Position = new Point(x, y);
                    return true;
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }
            return false;
        }

        /// <summary>
        /// Sets the hidden state of a warp.
        /// </summary>
        /// <param name="warpName">The warp name.</param>
        /// <param name="state">The state.</param>
        /// <returns>Whether the operation succeeded.</returns>
        public bool Hide(string worldId, string warpName, bool state) {
            try {
                if (table.Where(w => w.WarpName == warpName && w.WorldID == worldId)
                        .Set(w => w.Private, state ? "1" : "0")
                        .Update() > 0) {
                    Warps.Find(w => string.Equals(w.Name, warpName, StringComparison.OrdinalIgnoreCase))!.IsPrivate = state;
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
    /// Represents a warp.
    /// </summary>
    public class Warp
    {
        public string WorldID { get; set; }
        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Gets or sets the warp's privacy state.
        /// </summary>
        public bool IsPrivate { get; set; }
        /// <summary>
        /// Gets or sets the position.
        /// </summary>
        public Point Position { get; set; }

        public Warp(string worldId, Point position, string name, bool isPrivate = false) {
            WorldID = worldId;
            Name = name;
            Position = position;
            IsPrivate = isPrivate;
        }
    }
}
