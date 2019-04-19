//using System.Collections.Generic;
//using AsyncDbgCore;

//#nullable enable

//namespace AsyncCausalityDebuggerNew
//{
//    public class VisualNode
//    {
//        public string Id { get; }
//        public string DisplayText { get; }

//        public HashSet<CausalityNode> CuasalityNodes { get; }

//        public VisualNode(string id, string displayText, params CausalityNode[] nodes)
//        {
//            Contract.Requires(nodes.Length != 0, "nodes.Length != 0");

//            Id = id;
//            DisplayText = displayText;

//            CuasalityNodes = new HashSet<CausalityNode>(nodes);
//        }

//        public override string ToString()
//        {
//            return DisplayText;
//        }
//    }
//}
