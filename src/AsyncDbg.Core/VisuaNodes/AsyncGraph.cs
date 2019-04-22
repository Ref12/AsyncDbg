using System;
using System.Collections.Generic;
using System.Linq;
using AsyncCausalityDebuggerNew;

#nullable enable

namespace AsyncDbg.VisuaNodes
{
    public class AsyncGraph
    {
        // Roots of the current async graph.
        private readonly HashSet<VisualNode> _roots = new HashSet<VisualNode>();

        public IEnumerable<CausalityNode> EnumerateCausalityNodes()
        {
            foreach (var root in _roots.SelectMany(r => r.CuasalityNodes))
            {
                foreach (var n in root.EnumerateDependenciesAndSelfDepthFirst())
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
            foreach (var root in _roots)
            {
                foreach (var n in root.EnumerateAwaitsOnAndSelf())
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
