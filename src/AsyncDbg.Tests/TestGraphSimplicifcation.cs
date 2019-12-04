using AsyncDbg.Causality;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AsyncDbg.Tests
{
    //public class CompositeNode : ICausalityNode
    //{

    //}

    //public class TestCausalityNode : ICausalityNode
    //{

    //}

    [TestFixture]
    public class TestGraphSimplicifcation
    {
        [TestCase]
        public void TestSimpleCase()
        {
            // Node1 -> Node2 -> Node3
            //
            // CompositeNode (Node1 -> Node2 -> Node3)

            // Node1 -> Node2 -> Node3 -> Node6
            //   |----> Node4 -> Node5 |
            //
            // Node1 -> Composite (2->3) -> Node6
            //   |----> Composite (4->5) |
        }
    }
}
