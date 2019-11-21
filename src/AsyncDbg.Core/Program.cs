using System;
using System.Threading.Tasks;
using AsyncDbg.Causality;

#nullable enable

namespace AsyncCausalityDebugger
{

    public static class Program
    {
        static void Main(string[] args)
        {
            //var dumpPath = args.Length > 0 ? args[0] : @"E:\Dumps\FromDmitry\BatmonService-MW1AAPE3AE9F979-27484-55d99747-c1fe-d35b-d6b0-ae122faa04a9.dmp";
            //var dumpPath = args.Length > 0 ? args[0] : @"C:\Users\seteplia\AppData\Local\Temp\BasicDatastructures.DMP";
            //var dumpPath = args.Length > 0 ? args[0] : @"C:\Sources\GitHub\Dumps4AsyncDbg\AsyncReaderWriterLockDeadlock_64.DMP";
            var dumpPath = args.Length > 0 ? args[0] : @"C:\Sources\GitHub\Dumps4AsyncDbg\LongRunningTask_64.DMP";
            //var dumpPath = args.Length > 0 ? args[0] : @"C:\Sources\GitHub\Dumps4AsyncDbg\ManualResetEventSlimOnTheStack_64.DMP";
            //var dumpPath = args.Length > 0 ? args[0] : @"E:\Dumps\UnhandledFailure (2)\UnhandledFailure.dmp";
            //var dumpPath = args.Length > 0 ? args[0] : @"E:\Dumps\QCacheService.exe_190425_034613\QCacheService.exe_190425_034613.dmp";
            //var dumpPath = args.Length > 0 ? args[0] : @"E:\Dumps\QuickBuild(16056)\QuickBuild(16056).dmp";
            //var dumpPath = args.Length > 0 ? args[0] : @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\BasicDatastructures\DeepStackTraceInMainThread.DMP";
            //var dumpPath = args.Length > 0 ? args[0] : @"E:\Dumps\xunit.console (2).DMP";

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
