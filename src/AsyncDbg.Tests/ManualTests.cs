using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AsyncCausalityDebugger;
using NUnit.Framework;

namespace Test.AsyncCausalityInspector
{
    [TestFixture]
    public class ManualTests
    {
        [Test]
        public void CheckTheDump()
        {
            string path = @"E:\Dumps\UnhandledFailure.dmp";
            //string path = @"C:\Users\seteplia\AppData\Local\Temp\xunit.console (2).DMP";

            var contextNew = AsyncCausalityDebuggerNew.CausalityContext.LoadCausalityContextFromDump(path);
            var newContent = contextNew.SaveDgml(path + ".dgml", whatIf: false);
            //var newContent = contextNew.OverallStats(path + ".dgml");
            Console.WriteLine(newContent);
        }
    }
}
