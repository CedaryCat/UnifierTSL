using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TShockAPI
{
    public static class LinqExt
    {
        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action) {
            if (source == null) throw new ArgumentNullException("source");
            if (action == null) throw new ArgumentNullException("action");

            foreach (T item in source)
                action(item);
        }

        /// <summary>
        /// Attempts to retrieve the value at the given index from the enumerable
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <param name="index"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static bool TryGetValue<T>(this IEnumerable<T> enumerable, int index, [NotNullWhen(true)] out T? value) where T : notnull {
            if (index < enumerable.Count()) {
                value = enumerable.ElementAt(index);
                return true;
            }

            value = default;
            return false;
        }
    }
}
