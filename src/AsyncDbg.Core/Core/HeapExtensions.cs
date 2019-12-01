// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace AsyncDbg.Core
{
    public static class HeapExtensions
    {
        public static IEnumerable<ClrInstance> EnumerateClrObjects(this ClrHeap heap)
        {
            foreach (var address in heap.EnumerateObjectAddresses())
            {
                var result = ClrInstance.CreateInstance(heap, address);
                yield return result;
            }
        }
    }
}
