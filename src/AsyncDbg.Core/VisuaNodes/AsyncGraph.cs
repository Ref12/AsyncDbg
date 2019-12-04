using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AsyncDbg.Causality;

#nullable enable

namespace AsyncDbg.VisuaNodes
{
    [DebuggerDisplay("{VisualNodes,nq}")]
    public class AsyncGraph
    {
        public AsyncGraph(List<VisualNode> visualNodes)
        {
            VisualNodes = visualNodes;
        }

        public IEnumerable<ICausalityNode> EnumerateCausalityNodes()
        {
            foreach (var node in VisualNodes.SelectMany(r => r.CausalityNodes))
            {
                yield return node;
            }
        }

        public List<VisualNode> VisualNodes { get; }

        public override string ToString()
        {
            // TODO: implement
            return base.ToString();
        }
    }
}
