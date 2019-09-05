using System;
using System.Collections.Generic;
using System.Linq;
using AsyncDbg.Causality;
using Codex.Utilities;

#nullable enable

namespace AsyncDbg.VisuaNodes
{
    public class AsyncGraph
    {
        // Roots of the current async graph.
        private readonly Dictionary<Guid, VisualNode> _roots = new Dictionary<Guid, VisualNode>();

        private readonly Lazy<Guid> _key;

        public Guid Key => _key.Value;

        public AsyncGraph()
        {
            _key = new Lazy<Guid>(() => ComputeKey());
        }

        public IEnumerable<CausalityNode> EnumerateCausalityNodes()
        {
            foreach (var root in _roots.Values.SelectMany(r => r.CausalityNodes))
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
            foreach (var root in _roots.Values)
            {
                foreach (var n in root.EnumerateAwaitsOnAndSelf())
                {
                    yield return n;
                }
            }
        }

        public VisualNode[] Roots => _roots.Values.ToArray();

        public void AddRoot(VisualNode visualRoot)
        {
            Guid key = visualRoot.Key.Value;
            if (!_roots.ContainsKey(key))
            {
                _roots.Add(key, visualRoot);
            }
        }

        public override string ToString()
        {
            // TODO: implement
            return base.ToString();
        }

        private Guid ComputeKey()
        {
            return Guid.NewGuid();
            //var murmur = new Murmur3();
            //return murmur.ComputeHash(_roots.Keys.Select(k => k.ToByteArray()).Select(ba => new ArraySegment<byte>(ba))).AsGuid();
        }
    }
}
