using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using AsyncDbgCore;
using AsyncDbgCore.Core;
using AsyncDbgCore.New;
using Microsoft.Diagnostics.Runtime;
//using AsyncDbg.Extensions;
//using HeapExtensions = AsyncDbgCore.Core.HeapExtensions;
using AsyncCausalityDebuggerNew;

#nullable enable

namespace AsyncCausalityDebugger
{
    
    public static class Program
    {
        private const object notnull = null;
        //private static readonly object notnull = "";



        private static void Foo(object? o)
        {
            if (o is object)
            {

            }

            // Not null checks
            if (o is var _)
            {
                // 
            }

            if (o is (1, _))
            {
                // Match with the tuple!
            }

            if (o is object _)
            {

            }

            if (o is { })
            {
                // Not null
            }

            if (o switch { { } => true, null => false })
            {
                // This madness compiles but throws at runtime if o is null
            }

            if (o switch { { } => true })
            {
                // This madness compiles but throws at runtime if o is null
                // And No warning from the compiler!
            }

            if (o is null)
            {
                // null
            }
        }
        static void Main(string[] args)
        {
            



            var dumpPath = args.Length > 0 ? args[0] : @"E:\Dumps\UnhandledFailure.dmp";

            var context = CausalityContext.LoadCausalityContextFromDump(dumpPath);
            context.SaveDgml(dumpPath + ".dgml");
        }
    }
}
