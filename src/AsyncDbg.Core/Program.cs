﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        static void Main(string[] args)
        {
            //var dumpPath = args.Length > 0 ? args[0] : @"E:\Dumps\FromDmitry\BatmonService-MW1AAPE3AE9F979-27484-55d99747-c1fe-d35b-d6b0-ae122faa04a9.dmp";
            //var dumpPath = args.Length > 0 ? args[0] : @"C:\Users\seteplia\AppData\Local\Temp\BasicDatastructures.DMP";
            var dumpPath = args.Length > 0 ? args[0] : @"E:\Dumps\xunit.console (2).DMP";

            // "E:\Dumps\xunit.console (2).DMP"
            // C:\Users\seteplia\AppData\Local\Temp\BasicDatastructures.DMP
            var context = CausalityContext.LoadCausalityContextFromDump(dumpPath);
            context.SaveDgml(dumpPath + ".dgml");
            return;

            string? line = null;
            while ((line = Console.ReadLine()) != null)
            {
                try
                {
                    var id = ulong.Parse(line.Trim());
                    if (context.TryGetNodeAt(id, out var node))
                    {
                        Console.WriteLine($"Found node '{node.ToString()}' at for {id}");
                    }
                    else
                    {
                        Console.WriteLine($"Could not find node at for {id}");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }
        }
    }
}
