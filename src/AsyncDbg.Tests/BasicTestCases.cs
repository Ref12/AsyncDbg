using System;
using System.Collections.Generic;
using System.IO;
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
        private bool TryGetBaseLine(string dumpFilePath, out string dgml)
        {
            string dgmlFilePath = $"{dumpFilePath}.dgml";
            if (File.Exists(dgmlFilePath))
            {
                dgml = File.ReadAllText(dgmlFilePath);
                return true;
            }

            dgml = null;
            return false;
        }

        private void CheckDgmlGeneration(string dumpFilePath)
        {
            var contextNew = AsyncCausalityDebuggerNew.CausalityContext.LoadCausalityContextFromDump(dumpFilePath);
            var dgmlPath = dumpFilePath + ".dgml";
            var newContent = contextNew.SaveDgml(dgmlPath, whatIf: true);

            if (TryGetBaseLine(dumpFilePath, out var oldContent))
            {
                Assert.AreEqual(oldContent, newContent);
            }
            else
            {
                File.WriteAllText(dgmlPath, newContent);
            }
        }

        [Test]
        public void InspectDumpWithSemaphore()
        {
            //string path = @"C:\Users\seteplia\AppData\Local\Temp\SemaphoreSlimLiveLock (3).DMP";
            //CheckDgmlGeneration(path);
        }

        [Test]
        public void AwaitForTaskDelay()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\BasicDatastructures_await_for_TaskDelay.DMP";
            CheckDgmlGeneration(path);
        }

        [Test]
        public void TaskSourceSlim()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\BasicDatastructures_TaskSourceSlim.DMP";
            CheckDgmlGeneration(path);
        }

        [Test]
        public void TaskSourceSlimValueTasks()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\BasicDatastructures_TaskSourceSlimValueTask.DMP";
            CheckDgmlGeneration(path);
        }

        [Test]
        public void AwaitForCompletedTask()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\AwaitForCompletedTask.dmp";
            CheckDgmlGeneration(path);
        }

        [Test]
        public void TaskCompletionSourceDeadlock()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\TaskCompletionSourceDeadlock.dmp";
            CheckDgmlGeneration(path);
        }

        [Test]
        public void AwaitForTaskCompletionSource()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\WaitingOnTcs.dmp";
            CheckDgmlGeneration(path);
        }

        [Test]
        public void AwaitForUnfinishedTask()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\AwaitForUnfinishedTask.dmp";
            CheckDgmlGeneration(path);
        }

        [Test]
        public void CompareOldAndNew()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\BasicDatastructures (3).DMP";
            CheckDgmlGeneration(path);
        }
    }
}
