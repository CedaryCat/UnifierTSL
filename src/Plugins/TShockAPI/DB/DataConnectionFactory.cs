using System;
using LinqToDB.Data;

namespace TShockAPI.DB
{
    internal static class DataConnectionFactory
    {
        public static Func<DataConnection> FromPrototype(DataConnection prototype) {
            ArgumentNullException.ThrowIfNull(prototype);

            var options = prototype.Options;
            return () => new DataConnection(options);
        }
    }
}
