using System;
using System.Collections.Generic;
using System.Linq;
using AsyncDbg.Causality;
using AsyncDbg.Utils;
using static System.Environment;

#nullable enable

namespace AsyncDbg.VisuaNodes
{
    public class VisualNode
    {
        // If visual node consists of multiple causality nodes,
        // then we need to track the last causality node that contains actual set of dependencies.
        private readonly CausalityNode _lastOrSingleCausalityNode;
        private readonly HashSet<VisualNode> _awaitsOn = new HashSet<VisualNode>();

        public string Id { get; }
        public string DisplayText { get; }

        public HashSet<CausalityNode> CausalityNodes { get; }

        public VisualNode(string id, string displayText, params CausalityNode[] nodes)
        {
            Contract.Requires(nodes.Length != 0, "nodes.Length != 0");

            Id = id;
            DisplayText = displayText;
            _lastOrSingleCausalityNode = nodes.Last();
            CausalityNodes = new HashSet<CausalityNode>(nodes);
        }

        public static VisualNode Create(params CausalityNode[] nodes)
        {
            Contract.Requires(nodes.Length != 0, "nodes.Length != 0");

            // Id of a visual node is Id of the first causality node,
            // because dependent nodes are linked to the first node in the combined chain.
            var id = nodes[0].Id;
            string displayText;

            if (nodes.Length > 1)
            {
                displayText = string.Join($"{NewLine}|{NewLine}", nodes.Reverse().Where(n => n.Visible).Select(n => n.ToString()));
            }
            else
            {
                displayText = nodes[0].ToString();
            }

            return new VisualNode(id, displayText, nodes);
        }

        public void MaterializeDependencies(Dictionary<CausalityNode, VisualNode> visualMap)
        {
            // TODO: add VisualMap type that will fail with more readable error message
            // when the indexer like visualMap[d] fails with key not found.
            _awaitsOn.AddRange(_lastOrSingleCausalityNode.Dependencies.Select(d => visualMap[d]));
        }

        public IEnumerable<VisualNode> AwaitsOn => _awaitsOn;

        public void Awaits(IEnumerable<VisualNode> awaitsOn)
        {
            _awaitsOn.AddRange(awaitsOn);
        }

        public override string ToString()
        {
            return DisplayText;
        }

        public static VisualNode Create(CausalityNode causalityNode)
        {
            return new VisualNode(causalityNode.Id, causalityNode.ToString(), causalityNode);
        }

        public IEnumerable<VisualNode> EnumerateAwaitsOnAndSelf()
        {
            var enumeratedSet = new HashSet<VisualNode>();

            // Using queue to get depth first left to right traversal. Stack would give right to left traversal.
            var queue = new Queue<VisualNode>();

            queue.Enqueue(this);

            while (queue.Count > 0)
            {
                var next = queue.Dequeue();

                if (enumeratedSet.Contains(next))
                {
                    continue;
                }

                enumeratedSet.Add(next);
                yield return next;

                foreach (var n in next._awaitsOn)
                {
                    queue.Enqueue(n);
                }
            }
        }
    }
}
