// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System.Collections.Generic;

#nullable enable

namespace AsyncDbgCore.New
{
    internal static class EnumerableExtensions
    {
        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> sequence, IEqualityComparer<T>? comparer = null)
        {
            return new HashSet<T>(sequence, comparer ?? EqualityComparer<T>.Default);
        }
    }
}
