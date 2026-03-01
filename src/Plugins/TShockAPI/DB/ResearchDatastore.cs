using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TShockAPI.DB
{
    public class ResearchDatastore
    {
        [Table(Name = "Research")]
        class Research { 
            public required int WorldId { get; set; }
            public int PlayerId { get; set; }
            public int ItemId { get; set; }
            public int AmountSacrificed { get; set; }
            [Column(DataType = DataType.DateTime)]
            public DateTime TimeSacrificed { get; set; }
        }

        readonly DataConnection database;
        readonly ITable<Research> table;
        public ResearchDatastore(DataConnection db) { 
            database = db;
            table = database.CreateTable<Research>(tableOptions: TableOptions.CreateIfNotExists);
            _itemsSacrificed = [];
        }

        /// <summary>
        /// In-memory cache of what items have been sacrificed.
        /// The first call to GetSacrificedItems will load this with data from the database.
        /// </summary>
        private Dictionary<int, int> _itemsSacrificed;
        /// <summary>
        /// This call will return the memory-cached list of items sacrificed.
        /// If the cache is not initialized, it will be initialized from the database.
        /// </summary>
        /// <returns></returns>
        public Dictionary<int, int> GetSacrificedItems(int worldid) {
            if (_itemsSacrificed == null) {
                _itemsSacrificed = ReadFromDatabase(worldid);
            }

            return _itemsSacrificed;
        }

        /// <summary>
        /// This function will return a Dictionary&lt;ItemId, AmountSacrificed&gt; representing
        /// what the progress of research on items is for this world.
        /// </summary>
        /// <returns>A dictionary of ItemID keys and Amount Sacrificed values.</returns>
        private Dictionary<int, int> ReadFromDatabase(int worldid) {
            Dictionary<int, int> sacrificedItems = new Dictionary<int, int>();

            try {
                var query = table
                    .Where(r => r.WorldId == worldid)
                    .GroupBy(r => r.ItemId)
                    .Select(g => new {
                        ItemId = g.Key,
                        TotalSacrificed = g.Sum(r => r.AmountSacrificed)
                    });

                foreach (var item in query) {
                    sacrificedItems[item.ItemId] = item.TotalSacrificed;
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }

            return sacrificedItems;
        }

        /// <summary>
        /// This method will sacrifice an amount of an item for research.
        /// </summary>
        /// <param name="itemId">The net ItemId that is being researched.</param>
        /// <param name="amount">The amount of items being sacrificed.</param>
        /// <param name="player">The player who sacrificed the item for research.</param>
        /// <returns>The cumulative total sacrifices for this item.</returns>
        public int SacrificeItem(int worldid, int itemId, int amount, TSPlayer player) {
            var itemsSacrificed = GetSacrificedItems(worldid);
            if (!(itemsSacrificed.ContainsKey(itemId)))
                itemsSacrificed[itemId] = 0;

            var result = 0;
            try {
                result = table
                    .Value(r => r.WorldId, worldid)
                    .Value(r => r.PlayerId, player.Account.ID)
                    .Value(r => r.ItemId, itemId)
                    .Value(r => r.AmountSacrificed, amount)
                    .Value(r => r.TimeSacrificed, DateTime.Now)
                    .Insert();
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }

            if (result == 1) {
                itemsSacrificed[itemId] += amount;
            }

            return itemsSacrificed[itemId];
        }
    }
}
