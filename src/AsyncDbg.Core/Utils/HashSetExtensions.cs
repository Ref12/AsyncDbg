using System.Collections.Generic;

#nullable enable

namespace AsyncDbg.Utils
{
    public static class HashSetExtensions
    {
        public static void AddRange<T>(this HashSet<T> hashSet, IEnumerable<T> values)
        {
            foreach (var v in values)
            {
                hashSet.Add(v);
            }
        }
    }
}
