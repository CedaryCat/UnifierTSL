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
    public class RememberedPosManager
    {
        [Table(Name = "RememberedPos")]
        class RememberedPos
        {
            [Column(IsPrimaryKey = true, DataType = DataType.VarChar, Length = 50)]
            public required string Name { get; set; }
            [Column(DataType = DataType.Text)]
            public required string IP { get; set; }
            public required int X { get; set; }
            public required int Y { get; set; }
            [Column(DataType = DataType.Text)]
            public required string WorldID { get; set; }
        }

        readonly DataConnection database;
        readonly ITable<RememberedPos> table;
        public RememberedPosManager(DataConnection db) {
            database = db;
            table = database.CreateTable<RememberedPos>(tableOptions: TableOptions.CreateIfNotExists);
        }
        public Vector2 CheckLeavePos(string name) {
            try {
                var pos = table.FirstOrDefault(p => p.Name == name);
                if (pos != null) {
                    int checkX = pos.X;
                    int checkY = pos.Y;
                    //fix leftover inconsistencies
                    if (checkX == 0)
                        checkX++;
                    if (checkY == 0)
                        checkY++;
                    return new Vector2(checkX, checkY);
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }

            return new Vector2();
        }

        public Vector2 GetLeavePos(string worldid, string name, string IP) {
            try {
                var pos = table.FirstOrDefault(p => p.Name == name && p.IP == IP && p.WorldID == worldid);
                if (pos != null) {
                    return new Vector2(pos.X, pos.Y);
                }
            }
            catch (Exception ex) {
                TShock.Log.Error(ex.ToString());
            }

            return new Vector2();
        }

        public void InsertLeavePos(string worldid, string name, string IP, int X, int Y) {
            if (CheckLeavePos(name) == Vector2.Zero) {
                try {
                    if ((X != 0) && (Y != 0)) //invalid pos!
                    {
                        table.Insert(() => new RememberedPos {
                            Name = name,
                            IP = IP,
                            X = X,
                            Y = Y,
                            WorldID = worldid
                        });
                    }
                }
                catch (Exception ex) {
                    TShock.Log.Error(ex.ToString());
                }
            }
            else {
                try {
                    if ((X != 0) && (Y != 0)) //invalid pos!
                    {
                        table.Where(p => p.Name == name)
                            .Set(p => p.X, X)
                            .Set(p => p.Y, Y)
                            .Set(p => p.IP, IP)
                            .Set(p => p.WorldID, worldid)
                            .Update();
                    }
                }
                catch (Exception ex) {
                    TShock.Log.Error(ex.ToString());
                }
            }
        }
    }
}
