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
