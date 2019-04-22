using System;
using System.Collections.Generic;

#nullable enable

namespace AsyncDbg.Utils
{
    public static class DictionaryExtensions
    {
        public static T GetOrAdd<T, TKey>(this Dictionary<TKey, T> dictionary, TKey key, Func<TKey, T> func)
        {
            if (dictionary.TryGetValue(key, out var result))
            {
                return result;
            }

            result = func(key);
            dictionary.Add(key, result);
            return result;
        }

        public static void TryAddValue<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (!dictionary.ContainsKey(key))
            {
                dictionary.Add(key, value);
            }
        }

        public static void TryAddRange<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, IEnumerable<TKey> keys, TValue value)
        {
            foreach (var k in keys)
            {
                dictionary.TryAddValue(k, value);
            }
        }
    }
}
