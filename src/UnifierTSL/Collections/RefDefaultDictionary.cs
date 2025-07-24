﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Collections
{

    public class RefDefaultDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue> where TKey : notnull where TValue : class
    {
        private readonly Dictionary<TKey, TValue> _dict;

        private readonly Func<TValue>? _getDefaultValue;

        private readonly Func<TKey, TValue> _getDefaultValue2;
        public RefDefaultDictionary(TValue defValue) {
            _dict = [];
            _getDefaultValue = () => defValue;
            _getDefaultValue2 = _ => defValue;
        }
        public RefDefaultDictionary(IDictionary<TKey, TValue> dictionary, TValue defValue) {
            _dict = new Dictionary<TKey, TValue>(dictionary);
            _getDefaultValue = () => defValue;
            _getDefaultValue2 = _ => defValue;
        }
        public RefDefaultDictionary(IDictionary<TKey, TValue> dictionary, Func<TValue> getDefaultValue) {
            _dict = new Dictionary<TKey, TValue>(dictionary);
            _getDefaultValue = getDefaultValue;
            _getDefaultValue2 = _ => getDefaultValue();
        }
        public RefDefaultDictionary(Func<TValue> getDefaultValue) {
            _dict = [];
            _getDefaultValue = getDefaultValue;
            _getDefaultValue2 = _ => getDefaultValue();
        }
        public RefDefaultDictionary(Func<TKey, TValue> getDefaultValue) {
            _dict = [];
            _getDefaultValue2 = getDefaultValue;
        }
        TValue IReadOnlyDictionary<TKey, TValue>.this[TKey key] => this[key];
        /// <summary>
        /// when get: if the key does not exist in the dictionary,the defaultVaule is returned.
        /// when set: if the key does not exist in the dictionary,the key-value pair is added
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public TValue this[TKey key] {
            get {
                if (_dict.TryGetValue(key, out var value)) {
                    return value;
                }
                if (_getDefaultValue is not null) {
                    value = _getDefaultValue.Invoke();
                }
                else {
                    value = _getDefaultValue2.Invoke(key);
                }
                _dict.Add(key, value);
                return value;
            }
            set {
                if (_dict.ContainsKey(key)) {
                    _dict[key] = value;
                }
                else {
                    _dict.Add(key, value);
                }
            }
        }

        public ICollection<TKey> Keys => _dict.Keys;
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => _dict.Keys;

        public ICollection<TValue> Values => _dict.Values;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => _dict.Values;

        public int Count => _dict.Count;

        public bool IsReadOnly => false;

        public void Add(TKey key, TValue value) {
            if (_dict.ContainsKey(key)) {
                _dict[key] = value;
            }
            else {
                _dict.Add(key, value);
            }
        }

        public void Add(KeyValuePair<TKey, TValue> item) {
            if (_dict.ContainsKey(item.Key)) {
                _dict[item.Key] = item.Value;
            }
            else {
                _dict.Add(item.Key, item.Value);
            }
        }

        public void Clear() {
            _dict.Clear();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item) {
            return _dict.Contains(item);
        }

        public bool ContainsKey(TKey key) {
            return _dict.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int index) {
            if (array == null) {
                throw new ArgumentNullException();
            }

            if (index < 0 || index > array.Length) {
                throw new ArgumentOutOfRangeException();
            }

            if (array.Length - index < Count) {
                throw new ArgumentException();
            }

            int num = Count;
            KeyValuePair<TKey, TValue>[] array2 = _dict.ToArray();
            for (int i = 0; i < num; i++) {
                array[index++] = new KeyValuePair<TKey, TValue>(array2[i].Key, array2[i].Value);
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() {
            return _dict.GetEnumerator();
        }

        public bool Remove(TKey key) {
            return _dict.Remove(key);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item) {
            return _dict.Remove(item.Key);
        }

        public bool TryGetValue(TKey key, [NotNullWhen(true)] out TValue? value) {
            return _dict.TryGetValue(key, out value);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
            return ((System.Collections.IEnumerable)_dict).GetEnumerator();
        }
    }
}
