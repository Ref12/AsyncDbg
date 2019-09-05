using System;
using AsyncDbg.Causality;
using NUnit.Framework;

namespace Test.AsyncCausalityInspector
{
    [TestFixture]
    public class ManualTests
    {
        [Test]
        public void CheckTheDump()
        {
            string path = @"E:\Dumps\1_xunit.console.exe.dmp";
            //string path = @"C:\Users\seteplia\AppData\Local\Temp\xunit.console (2).DMP";

            var contextNew = CausalityContext.LoadCausalityContextFromDump(path);
            var newContent = contextNew.SaveDgml(path + ".dgml", whatIf: false);
            //var newContent = contextNew.OverallStats(path + ".dgml");
            Console.WriteLine(newContent);
        }
    }
}
