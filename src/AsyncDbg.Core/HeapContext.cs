using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncDbgCore.New;
using Microsoft.Diagnostics.Runtime;

namespace AsyncCausalityDebuggerNew
{
    public class HeapContext
    {
        private readonly Func<ClrHeap> _heapFactory;
        public ClrHeap DefaultHeap { get; }

        public HeapContext(Func<ClrHeap> heapFactory)
        {
            _heapFactory = heapFactory;
            DefaultHeap = heapFactory();
        }

        public ClrHeap CreateHeap()
        {
            lock (this)
            {
                return _heapFactory();
            }
        }

        public static implicit operator ClrHeap(HeapContext context)
        {
            return context.DefaultHeap;
        }
    }
}
