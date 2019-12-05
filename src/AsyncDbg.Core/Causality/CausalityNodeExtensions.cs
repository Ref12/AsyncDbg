using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace AsyncDbg.Causality
{
    public static class CausalityNodeExtensions
    {
        public static bool IsRoot(this ICausalityNode node)
        {
            if (node.Dependents.Count == 0) { return true; }

            //if (node is ThreadNode) { return true; }

            //if (node is AsyncStateMachineNode stateMachine && stateMachine.Dependents.Count != 0) { return true; }

            //if (Dependents.Count == 0) return true;
            return false;
        }

        public static bool IsLeaf(this ICausalityNode node) => node.Dependencies.Count == 0;

        public static IEnumerable<ICausalityNode> EnumerateDependenciesAndSelfDepthFirst(this ICausalityNode current)
        {
            return EnumerateNeighborsAndSelfDepthFirst(current, n => n.Dependencies);
        }

        public static IEnumerable<ICausalityNode> EnumerateNeighborsAndSelfDepthFirst(this ICausalityNode current)
        {
            return EnumerateNeighborsAndSelfDepthFirst(current, n => n.Dependencies.Concat(n.Dependents));
        }

        public static IEnumerable<ICausalityNode> EnumerateNodesInCollapseableOrder(IEnumerable<ICausalityNode> nodes)
        {
            var existingSet = new HashSet<ICausalityNode>(nodes);

            // We have two cases here:
            // 1. We have potentially multiple root nodes in one graph or
            // 2. We have 0 root nodes, because we have a cycle.
            foreach (var n in enumerate(nodes))
            {
                var removed = existingSet.Remove(n);
                if (removed)
                {
                    // We can visit the same node more then once because two nodes can point to the same one.
                    yield return n;
                }
            }

            Contract.Assert(existingSet.Count == 0, "All the nodes must be enumerated.");

            static IEnumerable<ICausalityNode> enumerate(IEnumerable<ICausalityNode> nodes)
            {
                var roots = nodes.Where(n => n.IsRoot()).ToList();
                if (roots.Count != 0)
                {
                    // Case 1
                    foreach (var r in roots)
                    {
                        foreach (var d in r.EnumerateDependenciesAndSelfDepthFirst())
                        {
                            yield return d;
                        }
                    }
                }
                else
                {
                    // This is the second case: we have a cycle.
                    // In this case we'll grab the first node and enumerate all the dependencies.
                    var first = nodes.FirstOrDefault();
                    if (first != null)
                    {
                        foreach (var d in first.EnumerateDependenciesAndSelfDepthFirst())
                        {
                            yield return d;
                        }
                    }
                }
            }
        }

        public static IEnumerable<ICausalityNode> EnumerateNeighborsAndSelfDepthFirst(this ICausalityNode current, Func<ICausalityNode, IEnumerable<ICausalityNode>> getNeighbors)
        {
            var enumeratedSet = new HashSet<ICausalityNode>();

            // Using queue to get depth first left to right traversal. Stack would give right to left traversal.
            // TODO: explain why depth first is so important!
            var queue = new Stack<ICausalityNode>();

            queue.Push(current);

            while (queue.Count > 0)
            {
                var next = queue.Pop();

                if (enumeratedSet.Contains(next))
                {
                    continue;
                }

                enumeratedSet.Add(next);
                yield return next;

                foreach (var n in getNeighbors(next))
                {
                    queue.Push(n);
                }
            }
        }
    }
}
