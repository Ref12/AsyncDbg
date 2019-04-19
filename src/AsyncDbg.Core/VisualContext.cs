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

        private IEnumerable<VisualNode> Nodes => NodesByCausalityNode.Values.Distinct();

        public IEnumerable<VisualNode> ActiveNodes => NodesByCausalityNode.Values.Where(n => n.IsActive).Distinct();

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
                NodesByCausalityNode[node] = visualNode;
                visualNode.AssociatedNodes.Add(node);
            }
        }

        public void Preprocess()
        {
            foreach (var visualNode in Nodes)
            {
                visualNode.Unblocks.UnionWith(visualNode.CausalityNode.Unblocks.Select(c => NodesByCausalityNode[c]));
                visualNode.WaitingOn.UnionWith(visualNode.CausalityNode.WaitingOn.Select(c => NodesByCausalityNode[c]));
            }

            bool hasMore = true;
            while (hasMore)
            {
                hasMore = false;
                foreach (var visualNode in ActiveNodes)
                {
                    var node = visualNode.CausalityNode;

                    if (visualNode.WaitingOn.Count(n => n.IsActive) == 0 && visualNode.Unblocks.Count(n => n.IsActive) == 0)
                    {
                        hasMore |= visualNode.Deactivate();
                        continue;
                    }

                    if (CausalityContext.RanToCompletion(node))
                    {
                        hasMore |= visualNode.Deactivate();
                        continue;
                    }
                }
            }

            foreach (var visualNode in ActiveNodes)
            {
                var node = visualNode.CausalityNode;

                foreach (var awaitedNode in visualNode.WaitingOn)
                {
                    awaitedNode.Activate();
                }
            }

            foreach (var visualNode in ActiveNodes.ToList())
            {
                var node = visualNode.CausalityNode;
                if (node.Kind == NodeKind.AsyncStateMachine)
                {
                    if (node.Unblocks.Count == 1 && visualNode.Unblocks.Count == 1)
                    {
                        var unblockedNode = visualNode.Unblocks.First();
                        if (unblockedNode.CausalityNode.Kind != NodeKind.Task)
                        {
                            continue;
                        }

                        visualNode.Collapse(unblockedNode, string.Join(Environment.NewLine,
                            node.ToString(),
                            unblockedNode.ToString()));
                    }
                }
            }

            // Stack merge
            hasMore = true;
            while (hasMore)
            {
                hasMore = false;

                foreach (var visualNode in ActiveNodes.ToList())
                {
                    if (visualNode.WaitingOn.Count(n => n.IsActive) == 1 && visualNode.IsActive)
                    {
                        var singleBlocker = visualNode.WaitingOn.First(n => n.IsActive);
                        if (singleBlocker.Unblocks.Count(n => n.IsActive) == 1)
                        {
                            visualNode.Collapse(singleBlocker, string.Join(STACK_SEPARATOR,
                                    singleBlocker.ToString(),
                                    visualNode.ToString()));
                            hasMore = true;
                        }
                    }
                }
            }
        }

        private static readonly string NL = Environment.NewLine;
        private static readonly string STACK_SEPARATOR = $"{NL}|{NL}";

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
