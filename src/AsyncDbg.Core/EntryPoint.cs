using System;
using System.Linq;
using AsyncDbg.Core;
using Microsoft.Diagnostics.Runtime;

namespace AsyncDbgCore.New
{
    public class EntryPoint
    {
        public static void DoStuff(string path)
        {
            DataTarget target = DataTarget.LoadCrashDump(path);

            var dacLocation = target.ClrVersions[0];
            ClrRuntime runtime = dacLocation.CreateRuntime();
            var heap = runtime.Heap;

            var objects = heap.EnumerateClrObjects().ToList();
            Console.WriteLine(objects.Count);

            var arrays = objects.Where(o => o.IsArray).ToList();
            
            var targetArray = arrays[11];
            var array = PrintArray(targetArray);
            Console.WriteLine($"Required array: {array}");

            var myClassesArray = arrays[14];
            var myClass = myClassesArray.Items[0];

            var targetArray2 = arrays[13];
            var s1 = targetArray2.Items[0];
            var value = s1["I1"];
            Console.WriteLine($"Array with structs: {PrintArray(targetArray2)}");

            // Currently 12-th array is what is created in the app.
            foreach (var a in arrays)
            {
                Console.WriteLine(PrintArray(a));
            }

            Console.WriteLine("Done!");
        }

        private static string PrintArray(ClrInstance instance)
        {
            return $"[{string.Join(", ", instance.Items.Select(i => i.ToString()))}]";
        }
    }
}
