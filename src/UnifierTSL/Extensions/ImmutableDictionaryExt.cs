using System.Collections.Immutable;

namespace UnifierTSL.Extensions
{
    public static class ImmutableDictionaryExt
    {
        /// <summary>
        /// Fluent method for adding an item to an ImmutableDictionary
        /// </summary>
        public static ImmutableDictionary<TKey, TValue>.Builder AddItem<TKey, TValue>(
            this ImmutableDictionary<TKey, TValue>.Builder builder,
            TKey key,
            TValue value)
            where TKey : notnull {
            builder.Add(key, value);
            return builder;
        }
        /// <summary>
        /// Fluent method for adding an items to an ImmutableDictionary
        /// </summary>
        public static ImmutableDictionary<TKey, TValue>.Builder AddItems<TKey, TValue>(
            this ImmutableDictionary<TKey, TValue>.Builder builder,
            IEnumerable<(TKey key, TValue value)> items)
            where TKey : notnull {
            foreach ((TKey key, TValue value) in items) {
                builder.Add(key, value);
            }
            return builder;
        }
        /// <summary>
        /// Fluent method for adding an items to an ImmutableDictionary
        /// </summary>
        public static ImmutableDictionary<TKey, TValue>.Builder AddItems<TKey, TValue>(
            this ImmutableDictionary<TKey, TValue>.Builder builder,
            IEnumerable<TValue> items) where TValue : notnull, IKeySelector<TKey>
            where TKey : notnull {
            foreach (TValue item in items) {
                builder.Add(item.Key, item);
            }
            return builder;
        }
        /// <summary>
        /// Fluent method for setting an item to an ImmutableDictionary
        /// </summary>
        public static ImmutableDictionary<TKey, TValue>.Builder SetItem<TKey, TValue>(
            this ImmutableDictionary<TKey, TValue>.Builder builder,
            TKey key,
            TValue value)
            where TKey : notnull {
            builder[key] = value;
            return builder;
        }
        /// <summary>
        /// Fluent method for setting an items to an ImmutableDictionary
        /// </summary>
        public static ImmutableDictionary<TKey, TValue>.Builder SetItems<TKey, TValue>(
            this ImmutableDictionary<TKey, TValue>.Builder builder,
            IEnumerable<(TKey key, TValue value)> items)
            where TKey : notnull {
            foreach ((TKey key, TValue value) in items) {
                builder[key] = value;
            }
            return builder;
        }
        /// <summary>
        /// Fluent method for setting an items to an ImmutableDictionary
        /// </summary>
        public static ImmutableDictionary<TKey, TValue>.Builder SetItems<TKey, TValue>(
            this ImmutableDictionary<TKey, TValue>.Builder builder,
            IEnumerable<TValue> items) where TValue : notnull, IKeySelector<TKey>
            where TKey : notnull {
            foreach (TValue item in items) {
                builder[item.Key] = item;
            }
            return builder;
        }
    }
}
