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
    public class TestSimpleStuff
    {
        [Test]
        public void InspectDumpWithSemaphore()
        {
            string path = @"C:\Users\seteplia\AppData\Local\Temp\SemaphoreSlimLiveLock (3).DMP";
            CausalityContext.RunAsyncInspector(path);
        }

        [Test]
        public void AwaitForTaskDelay()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\BasicDatastructures_await_for_TaskDelay.DMP";

            var contextNew = AsyncCausalityDebuggerNew.CausalityContext.LoadCausalityContextFromDump(path);
            var newContent = contextNew.SaveDgml(path + ".dgml", whatIf: false);

            var context = CausalityContext.LoadCausalityContextFromDump(path);
            var oldContent = context.SaveDgml(path + ".dgml", whatIf: true);

            Console.WriteLine("Old content: " + oldContent);
            Console.WriteLine("New content: " + newContent);

            Assert.That(newContent, Is.EqualTo(oldContent));
        }

        [Test]
        public void TaskSourceSlim()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\BasicDatastructures_TaskSourceSlim.DMP";

            var contextNew = AsyncCausalityDebuggerNew.CausalityContext.LoadCausalityContextFromDump(path);
            var newContent = contextNew.SaveDgml(path + ".dgml", whatIf: false);

            var context = CausalityContext.LoadCausalityContextFromDump(path);
            var oldContent = context.SaveDgml(path + ".dgml", whatIf: true);

            Console.WriteLine("Old content: " + oldContent);
            Console.WriteLine("New content: " + newContent);

            Assert.That(newContent, Is.EqualTo(oldContent));
        }

        [Test]
        public void TaskSourceSlimValueTasks()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\BasicDatastructures_TaskSourceSlimValueTask.DMP";

            var contextNew = AsyncCausalityDebuggerNew.CausalityContext.LoadCausalityContextFromDump(path);
            var newContent = contextNew.SaveDgml(path + ".dgml", whatIf: false);

            var context = CausalityContext.LoadCausalityContextFromDump(path);
            var oldContent = context.SaveDgml(path + ".dgml", whatIf: true);

            Console.WriteLine("Old content: " + oldContent);
            Console.WriteLine("New content: " + newContent);

            Assert.That(newContent, Is.EqualTo(oldContent));
        }

        [Test]
        public void CheckTheDump()
        {
            string path = @"C:\Users\seteplia\AppData\Local\Temp\xunit.console (8).DMP";

            var contextNew = AsyncCausalityDebuggerNew.CausalityContext.LoadCausalityContextFromDump(path);
            var newContent = contextNew.SaveDgml(path + ".dgml", whatIf: false);
        }

        [Test]
        public void AwaitForCompletedTask()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\AwaitForCompletedTask.dmp";

            //var context = CausalityContext.LoadCausalityContextFromDump(path);
            //var oldContent = context.SaveDgml(path + ".dgml", whatIf: false);
            var contextNew = AsyncCausalityDebuggerNew.CausalityContext.LoadCausalityContextFromDump(path);
            var newContent = contextNew.SaveDgml(path + ".dgml", whatIf: false);
        }

        [Test]
        public void CompareOldAndNew()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\BasicDatastructures (3).DMP";

            var context = CausalityContext.LoadCausalityContextFromDump(path);
            var oldContent = context.SaveDgml(path + ".dgml", whatIf: true);

            var contextNew = AsyncCausalityDebuggerNew.CausalityContext.LoadCausalityContextFromDump(path);
            var newContent = contextNew.SaveDgml(path + ".dgml", whatIf: true);

            Assert.That(newContent, Is.EqualTo(oldContent));
        }

        [Test]
        public void CompareOldAndNewLong()
        {
            string path = @"D:\Dumps\MetabuildHang\Domino118464.DMP";

            var contextNew = AsyncCausalityDebuggerNew.CausalityContext.LoadCausalityContextFromDump(path);
            var newContent = contextNew.SaveDgml(path + ".dgml", whatIf: true);

            var context = CausalityContext.LoadCausalityContextFromDump(path);
            var oldContent = context.SaveDgml(path + ".dgml", whatIf: false);

            Assert.That(newContent, Is.EqualTo(oldContent));
        }
    }
}
