using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AsyncDbg.Causality;
using AsyncDbg.Utils;

#nullable enable

namespace AsyncDbg.VisuaNodes
{
    public class VisualContext
    {
        private readonly Dictionary<CausalityNode, VisualNode> _visualMap = new Dictionary<CausalityNode, VisualNode>();

        // Each causaility node belongs only to one async graph. This map tracks this information.
        private readonly Dictionary<CausalityNode, AsyncGraph> _nodeToAsyncGraphMap = new Dictionary<CausalityNode, AsyncGraph>();

        private bool TryFindExistingAsyncGraph(IEnumerable<CausalityNode> nodes, [NotNullWhenTrue] out AsyncGraph? graph)
        {
            foreach (var n in nodes)
            {
                if (_nodeToAsyncGraphMap.TryGetValue(n, out graph))
                {
                    return true;
                }
            }

            graph = null;
            return false;
        }

        /// <summary>
        /// Creates a new async graph from the given root.
        /// </summary>
        /// <remarks>
        /// If a given node represents a part of the graph that joins an existing async graph, then this method
        /// will update an existing graph instead of creating a new one.
        /// For instance, if two threads are waiting on a single task, we'll have two root nodes for a single async graph:
        /// <code>
        /// T1           T2
        /// |-> TSC(0) <-|
        /// </code>
        /// </remarks>
        public AsyncGraph CreateFromRoot(CausalityNode root)
        {
            Contract.Requires(root.IsRoot, "AsyncGraph can be created from a root node only.");

            var visualRoot = CreateVisualNode(root, _visualMap);

            if (TryFindExistingAsyncGraph(root.EnumerateDependenciesAndSelfDepthFirst(), out var existingGraph))
            {
                existingGraph.AddRoot(visualRoot);
                return existingGraph;
            }

            return new AsyncGraph(visualRoot);
        }

        private static VisualNode CreateVisualNode(CausalityNode root, Dictionary<CausalityNode, VisualNode> map)
        {
            // A -> B -> C -> D => (A,B,C) -> D
            //           | -> F          | -> F

            // Prepopulating CausalityNode -> VisualNode map.
            var collapseable = new List<CausalityNode>();
            var allNodes = root.EnumerateDependenciesAndSelfDepthFirst().ToList();

            CausalityNode? previousNode = null;
            foreach (var node in root.EnumerateDependenciesAndSelfDepthFirst())
            {
                if (collapseable.Count != 0 && shouldCollapse(previousNode, node))
                {
                    createCompositeVisualNode();
                }

                if (isCollapseable(previousNode, node))
                {
                    collapseable.Add(node);
                }
                else
                {
                    _ = map.GetOrAdd(node, n => VisualNode.Create(n));
                    var visual = VisualNode.Create(node);
                }

                previousNode = node;
            }

            // The last node in the dependency graph can be part of a collapsable subgraph.
            createCompositeVisualNode();

            foreach (var visualNode in map.Values)
            {
                visualNode.MaterializeDependencies(map);
            }

            var result = map[root];
            return result;

            void createCompositeVisualNode()
            {
                if (collapseable.Count == 0)
                {
                    return;
                }

                var visual = VisualNode.Create(collapseable.ToArray());

                // associate all collapsed nodes with visualNode
                map.TryAddRange(collapseable, visual);
                collapseable.Clear();
            }

            bool isCollapseable(CausalityNode? previousNode, CausalityNode node)
            {
                var isCollapsable = node.Dependencies.Count <= 1 && node.Kind != NodeKind.Thread;
                return isCollapsable;
            }

            bool shouldCollapse(CausalityNode? previousNode, CausalityNode node)
            {
                var shouldCollapse = node.Dependencies.Count > 1 || node.Dependents.Count > 1
                    || previousNode != null && !previousNode.Dependencies.Contains(node);
                return shouldCollapse;
            }
        }

        public static AsyncGraph[] Create(CausalityContext causalityContext)
        {
            var roots = causalityContext.Nodes.Where(n => n.IsRoot).ToList();

            var visualContext = new VisualContext();

            var asyncGraphs = roots.Select(root => visualContext.CreateFromRoot(root)).Distinct().ToArray();

            asyncGraphs = asyncGraphs.Where(ag => IsRelevant(ag)).ToArray();
            return asyncGraphs;
        }

        private static bool IsRelevant(AsyncGraph graph)
        {
            var causalityNodes = graph.EnumerateCausalityNodes().ToHashSet();
            // Filtering out empty graphs or fully completed ones.
            // One cases that falls into this bucket is async state machines allocated in debug builds.
            // In this case they're allocated, but useless becuase they're not even started.
            // (Just a reminder, in debug builds state machines are classes and in release mode they're structs).
            if (causalityNodes.All(n => n.IsComplete || n.IsRoot && n.IsLeaf))
            {
                return false;
            }

            var root = graph.Roots.FirstOrDefault()?.CausalityNodes.FirstOrDefault();
            Contract.AssertNotNull(root);

            if (root is TaskNode taskNode && taskNode.TaskKind == TaskKind.FromTaskCompletionSource)
            {
                // This is just a stand alone task completion source that is represented by two nodes: Task + TaskCompletionSource.
                return false;
            }

            return true;
        }
    }
}
