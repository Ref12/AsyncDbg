using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncDbgCore.New;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace AsyncCausalityDebuggerNew
{
    public class VisualContext
    {
        public readonly CausalityContext CausalityContext;
        public Dictionary<CausalityNode, VisualNode> NodesByCausalityNode { get; } = new Dictionary<CausalityNode, VisualNode>();

        public IEnumerable<VisualNode> Nodes => NodesByCausalityNode.Values.Distinct();

        public VisualContext(CausalityContext causalityContext)
        {
            this.CausalityContext = causalityContext;
        }

        public VisualNode AddNode(CausalityNode node)
        {
            var visualNode = new VisualNode(this, node);
            Associate(visualNode, node);
            return visualNode;
        }

        public void Associate(VisualNode visualNode, CausalityNode node)
        {
            if (node != null)
            {
                NodesByCausalityNode.Add(node, visualNode);
                visualNode.AssociatedNodes.Add(node);
            }
        }

        public void Preprocess()
        {
            foreach (var node in Nodes)
            {
                if (node.CausalityNode.Kind == NodeKind.AsyncStateMachine)
                {

                }
            }
        }

        public static VisualContext Create(CausalityContext causalityContext)
        {
            var context = new VisualContext(causalityContext);

            foreach (var node in causalityContext.Nodes)
            {
                context.AddNode(node);
            }

            context.Preprocess();

            return context;
        }
    }
}