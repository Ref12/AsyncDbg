using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AsyncDbg.InstanceWrappers;
using NUnit.Framework;

namespace AsyncDbg.Tests
{
    [TestFixture]
    public class TaskExtensionsTests
    {
        [Test]
        public void Test()
        {
            var status1 = 33554944;

            //Console.WriteLine(TaskInstasnceHelpers.GetStatus(status1));
            Console.WriteLine(TaskInstanceHelpers.GetStatus(33555456));

            /*
             * Name	Value	Type
m_stateFlags	33555456	int
             */
        }
    }
}
