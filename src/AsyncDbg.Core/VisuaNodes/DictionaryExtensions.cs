using System;
using System.Collections.Generic;

#nullable enable

namespace AsyncDbg.VisuaNodes
{
    public static class DictionaryExtensions
    {
        public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, Func<TValue> valueFactory)
        {
            if (dictionary.TryGetValue(key, out var result))
            {
                return result;
            }

            result = valueFactory();
            dictionary.Add(key, result);
            return result;
        }
    }
}
