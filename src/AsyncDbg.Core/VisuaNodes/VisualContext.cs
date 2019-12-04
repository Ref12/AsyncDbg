using System;
using System.Collections.Generic;
using System.Linq;
using AsyncDbg.Causality;
using AsyncDbg.Utils;

#nullable enable

namespace AsyncDbg.VisuaNodes
{
    public class VisualContext
    {
        private readonly Dictionary<Guid, AsyncGraph> _uniqueRelevantAsyncGraphs = new Dictionary<Guid, AsyncGraph>();
        //private readonly Dictionary<ICausalityNode, VisualNode> _visualMap = new Dictionary<ICausalityNode, VisualNode>();

        // Each causaility node belongs only to one async graph. This map tracks this information.
        //private readonly Dictionary<ICausalityNode, AsyncGraph> _nodeToAsyncGraphMap = new Dictionary<ICausalityNode, AsyncGraph>();

        // Each causaility node belongs only to one async graph. This map tracks this information.
        //private readonly Dictionary<VisualNode, AsyncGraph> _visualNodeToAsyncGraphMap = new Dictionary<VisualNode, AsyncGraph>();
        private readonly List<AsyncGraph> _asyncGraphs;

        public VisualContext(List<AsyncGraph> asyncGraphs)
        {
            _asyncGraphs = asyncGraphs;
            var relevantAsyncGraphs = asyncGraphs.Where(graph => IsRelevant(graph)).ToList();

            _uniqueRelevantAsyncGraphs = relevantAsyncGraphs.ToDictionary(graph => ComputeGraphKey(graph), graph => graph);
        }

        public static VisualContext Create(IEnumerable<ICausalityNode> causalityNodes, bool simplify = false)
        {
            var rawGraphs = SplitNodesIntoGraphs(causalityNodes);

            var asyncGraphs = rawGraphs.Select(nodes => CreateGraph(nodes, simplify)).ToList();

            return new VisualContext(asyncGraphs);
        }

        public IEnumerable<AsyncGraph> GetAsyncGraphs() => _uniqueRelevantAsyncGraphs.Values;

        public IEnumerable<VisualNode> EnumerateVisualNodes()
        {
            foreach (var graph in _uniqueRelevantAsyncGraphs.Values)
            {
                foreach (var n in graph.VisualNodes)
                {
                    yield return n;
                }
            }
        }

        private static List<List<ICausalityNode>> SplitNodesIntoGraphs(IEnumerable<ICausalityNode> nodes)
        {
            var processedNodes = new HashSet<ICausalityNode>();
            var rawGraphs = new List<List<ICausalityNode>>();

            foreach (var node in nodes)
            {
                if (!processedNodes.Contains(node))
                {
                    // The current not is not yet processed.
                    var rawGraph = new List<ICausalityNode>();
                    // Now we enumerate all the neighbor nodes of the current node
                    foreach (var n in node.EnumerateNeighborsAndSelfDepthFirst())
                    {
                        processedNodes.Add(n);
                        rawGraph.Add(n);
                    }

                    rawGraphs.Add(rawGraph);
                }
            }

            return rawGraphs;
        }

        private static AsyncGraph CreateGraph(List<ICausalityNode> nodes, bool simplify)
        {
            // A -> B -> C -> D => (A,B,C) -> D
            //           | -> F          | -> F

            // Prepopulating CausalityNode -> VisualNode map.
            var collapseable = new List<ICausalityNode>();
            var visualNodes = new List<VisualNode>();
            var allNodes = nodes;

            // If we have a root node, we'll start from it, because in this case the node simplification will produce
            // better result.
            var root = nodes.FirstOrDefault(n => n.IsRoot());

            if (root != null)
            {
                allNodes = root.EnumerateNeighborsAndSelfDepthFirst().ToList();
            }

            var map = new Dictionary<ICausalityNode, VisualNode>();
            ICausalityNode? previousNode = null;
            foreach (var node in allNodes)
            {
                if (collapseable.Count != 0 && shouldCollapse(previousNode, node))
                {
                    createCompositeVisualNode();
                }

                if (simplify && isCollapseable(previousNode, node))
                {
                    collapseable.Add(node);
                }
                else
                {
                    if (!map.ContainsKey(node))
                    {
                        var visualNode = VisualNode.Create(node);
                        map.Add(node, visualNode);
                        visualNodes.Add(visualNode);
                    }
                }

                previousNode = node;
            }

            // The last node in the dependency graph can be part of a collapsable subgraph.
            createCompositeVisualNode();

            foreach (var visualNode in visualNodes)
            {
                visualNode.MaterializeDependencies(map);
            }

            return new AsyncGraph(visualNodes);

            void createCompositeVisualNode()
            {
                if (collapseable.Count == 0)
                {
                    return;
                }

                var visual = VisualNode.Create(collapseable.ToArray());
                visualNodes.Add(visual);

                // associate all collapsed nodes with visualNode
                map.TryAddRange(collapseable, visual);
                collapseable.Clear();
            }

            static bool isCollapseable(ICausalityNode? previousNode, ICausalityNode node)
            {
                var isCollapsable = node.Dependencies.Count <= 1 && node.Kind != NodeKind.Thread;
                return isCollapsable;
            }

            static bool shouldCollapse(ICausalityNode? previousNode, ICausalityNode node)
            {
                var shouldCollapse = node.Dependencies.Count > 1 || node.Dependents.Count > 1
                    || previousNode != null && !previousNode.Dependencies.Contains(node);
                return shouldCollapse;
            }
        }

        private static Guid ComputeGraphKey(AsyncGraph graph)
        {
            return Guid.NewGuid();
            //var murmur = new Murmur3();
            //return murmur.ComputeHash(_roots.Keys.Select(k => k.ToByteArray()).Select(ba => new ArraySegment<byte>(ba))).AsGuid();
        }

        private static bool IsRelevant(AsyncGraph graph)
        {
            var causalityNodes = graph.EnumerateCausalityNodes().ToHashSet();

            // Filtering out empty graphs or fully completed ones.
            // One cases that falls into this bucket is async state machines allocated in debug builds.
            // In this case they're allocated, but useless becuase they're not even started.
            // (Just a reminder, in debug builds state machines are classes and in release mode they're structs).
            if (causalityNodes.All(n => n.IsComplete || n.IsRoot() && n.IsLeaf()))
            {
                return false;
            }

            if (graph.VisualNodes.Count == 1)
            {
                // Checking for a stand alone task completion source that is represented by two nodes: Task + TaskCompletionSource.
                var root = graph.VisualNodes[0].CausalityNodes.FirstOrDefault();

                if (root is TaskNode taskNode && taskNode.TaskKind == TaskKind.FromTaskCompletionSource)
                {
                    
                    return false;
                }

            }

            return true;
        }
    }
}
