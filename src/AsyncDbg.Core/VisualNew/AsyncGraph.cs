using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AsyncDbgCore;

#nullable enable

namespace AsyncCausalityDebuggerNew.VisualNew
{
    public static class DictionaryExtensions
    {
        public static T GetOrAdd<T, TKey>(this Dictionary<TKey, T> dictionary, TKey key, Func<TKey, T> func)
        {
            if (dictionary.TryGetValue(key, out var result))
            {
                return result;
            }

            result = func(key);
            dictionary.Add(key, result);
            return result;
        }

        public static void TryAddValue<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (!dictionary.ContainsKey(key))
            {
                dictionary.Add(key, value);
            }
        }

        public static void TryAddRange<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, IEnumerable<TKey> keys, TValue value)
        {
            foreach (var k in keys)
            {
                dictionary.TryAddValue(k, value);
            }
        }
    }

    public static class HashSetExtensions
    {
        public static void AddRange<T>(this HashSet<T> hashSet, IEnumerable<T> values)
        {
            foreach(var v in values)
            {
                hashSet.Add(v);
            }
        }

        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> sequence)
        {
            return new HashSet<T>(sequence);
        }
    }

    public class VisualContext
    {
        private readonly Dictionary<CausalityNode, VisualNode> _visualMap = new Dictionary<CausalityNode, VisualNode>();
        // Each causaility node belongs only to one async graph. This map tracks this information.
        private readonly Dictionary<CausalityNode, AsyncGraph> _nodeToAsyncGraphMap = new Dictionary<CausalityNode, AsyncGraph>();

        private bool TryFindExistingAsyncGraph(IEnumerable<CausalityNode> nodes, [NotNullWhenTrue] out AsyncGraph? graph)
        {
            foreach(var n in nodes)
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
            //_ = root
            //    .EnumerateDependenciesAndSelf()
            //    .Select(n => map.GetOrAdd(n, node => VisualNode.Create(node)))
            //    .ToList();

            var collapseable = new List<CausalityNode>();
            var allNodes = root.EnumerateDependenciesAndSelfDepthFirst().ToList();

            CausalityNode? previousNode = null;
            foreach (var node in root.EnumerateDependenciesAndSelfDepthFirst())
            {
                if (collapseable.Count != 0 && ShouldCollapse(previousNode, node))
                {
                    CreateCompositeVisualNode();
                }

                if (IsCollapseable(previousNode, node))
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
            CreateCompositeVisualNode();

            foreach(var visualNode in map.Values)
            {
                visualNode.MaterializeDependencies(map);
            }

            var result = map[root];
            return result;

            void CreateCompositeVisualNode()
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

            bool IsCollapseable(CausalityNode? previousNode, CausalityNode node)
            {
                bool isCollapsable = node.Dependencies.Count <= 1 && node.Kind != NodeKind.Thread;
                return isCollapsable;
                //return node.Dependencies.Count <= 1 &&
                //    (node.Dependents.Count == 0 || node.Dependents.First().Dependencies.Count <= 1);
            }

            bool ShouldCollapse(CausalityNode? previousNode, CausalityNode node)
            {
                bool shouldCollapse = node.Dependencies.Count > 1 || node.Dependents.Count > 1
                    || (previousNode != null && !previousNode.Dependencies.Contains(node));
            return shouldCollapse;
                //return node.Dependencies.Count <= 1 &&
                //    (node.Dependents.Count == 0 || node.Dependents.First().Dependencies.Count <= 1);
            }
        }

        private VisualNode GetOrCreateVisualNode(CausalityNode causalityNode)
        {
            if (_visualMap.TryGetValue(causalityNode, out var result))
            {
                return result;
            }

            result = VisualNode.Create(causalityNode);
            _visualMap.Add(causalityNode, result);
            return result;
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
            if (causalityNodes.All(n => n.IsComplete))
            {
                return false;
            }

            return true;
        }
    }

    public class AsyncGraph
    {
        // Roots of the current async graph.
        private readonly HashSet<VisualNode> _roots = new HashSet<VisualNode>();

        public IEnumerable<CausalityNode> EnumerateCausalityNodes()
        {
            foreach(var root in _roots.SelectMany(r => r.CuasalityNodes))
            {
                foreach(var n in root.EnumerateDependenciesAndSelfDepthFirst())
                {
                    yield return n;
                }
            }
        }

        public bool HasVisualNodeWith(Func<VisualNode, bool> predicate)
        {
            return EnumerateVisualNodes().Any(predicate);
        }

        public IEnumerable<VisualNode> EnumerateVisualNodes()
        {
            foreach(var root in _roots)
            {
                foreach(var n in root.EnumerateAwaitsOnAndSelf())
                {
                    yield return n;
                }
            }
        }

        public AsyncGraph(VisualNode node)
        {
            _roots.Add(node);
        }

        public VisualNode[] Roots => _roots.ToArray();

        public void AddRoot(VisualNode visualRoot)
        {
            _roots.Add(visualRoot);
        }

        public override string ToString()
        {
            // TODO: implement
            return base.ToString();
        }
    }
}
