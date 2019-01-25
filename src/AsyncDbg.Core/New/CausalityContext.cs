using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AsyncCausalityDebugger;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace AsyncCausalityDebuggerNew
{
    public class CausalityContext
    {
        private readonly ConcurrentDictionary<ulong, CausalityNode> _nodesByAddress = new ConcurrentDictionary<ulong, CausalityNode>();
        //private readonly ConcurrentDictionary<ClrInstance, CausalityNode> _nodes = new ConcurrentDictionary<ClrInstance, CausalityNode>(new ClrInstanceAddressComparer());
        private readonly Dictionary<int, ClrThread> _threadsById;

        public TypeIndex AwaitTaskContinuationIndex => Registry.AwaitTaskContinuationIndex;
        public TypeIndex TaskCompletionSourceIndex => Registry.TaskCompletionSourceIndex;

        public ClrType ContinuationWrapperType => Registry.ContinuationWrapperType;

        public ClrType StandardTaskContinuationType => Registry.StandardTaskContinuationType;

        public TypesRegistry Registry { get; }

        public ClrHeap Heap { get; }

        public CausalityContext(ClrHeap heap)
        {
            Registry = TypesRegistry.Create(heap);
            Heap = heap;

            _threadsById = heap.Runtime.Threads.ToDictionary(t => t.ManagedThreadId, t => t);

            foreach (var (instance, kind) in Registry.EnumerateRegistry())
            {
                GetOrCreate(instance, kind);
            }
        }

        public static CausalityContext LoadCausalityContextFromDump(string dumpPath)
        {
            var target = DataTarget.LoadCrashDump(dumpPath);
            var dacLocation = target.ClrVersions[0];
            var runtime = dacLocation.CreateRuntime();
            var heap = runtime.Heap;

            var context = new CausalityContext(heap);

            context.Compute();
            return context;
        }

        public bool TryGetNodeFor(ClrInstance instance, out CausalityNode result)
        {
            return _nodesByAddress.TryGetValue(instance.ObjectAddress.Value, out result);
        }

        public bool TryGetThreadById(int threadId, out ClrThread thread) => _threadsById.TryGetValue(threadId, out thread);

        public CausalityNode GetNode(ClrInstance instance)
        {
            return _nodesByAddress.GetOrAdd(instance.ObjectAddress.Value, task => new CausalityNode(this, instance, kind: NodeKind.Unknown));
        }

        public bool TryGetNodeAt(ulong address, out CausalityNode result)
        {
            return _nodesByAddress.TryGetValue(address, out result);
        }

        public CausalityNode GetOrCreate(ClrInstance instance, NodeKind kind)
        {
            if (instance.ObjectAddress == null)
            {
                throw new InvalidOperationException($"instance.ObjectAddress should not be null. Instance: {instance}.");
            }

            //var node = _nodes.GetOrAdd(instance, task => new CausalityNode(this, task, kind: kind));
            //_nodesByAddress.GetOrAdd(node.TaskInstance.ObjectAddress.Value, node);
            var node = _nodesByAddress.GetOrAdd(instance.ObjectAddress.Value, _ => new CausalityNode(this, instance, kind: kind));

            return node;
        }

        public void Compute()
        {
            foreach (var node in _nodesByAddress.Values)
            {
                node.Link();
            }
        }

        public string SaveDgml(string filePath, bool whatIf = false)
        {
            var writer = new DgmlWriter();
            foreach (var node in _nodesByAddress.Values.OrderBy(v => v.Id))
            {
                if (node.Dependencies.Count == 0 && node.Dependents.Count == 0)
                {
                    continue;
                }

                if (RanToCompletion(node))
                {
                    continue;
                }

                writer.AddNode(new DgmlWriter.Node(id: node.Id, label: node.ToString()));
                foreach (var dependency in node.Dependencies.OrderBy(d => d.Id))
                {
                    writer.AddLink(new DgmlWriter.Link(
                        source: node.Id,
                        target: dependency.Id,
                        label: null));
                }
            }

            if (whatIf)
            {
                return writer.SerializeAsString();
            }

            writer.Serialize(filePath);
            return writer.SerializeAsString();
        }

        // True if the node represents a completed task/sequence of tasks. Needed because we don't want to show them.
        private bool RanToCompletion(CausalityNode node)
        {
            if (!node.IsComplete)
            {
                return false;
            }

            return node.Dependencies.All(n => n.IsComplete || n.Kind == NodeKind.TaskCompletionSource || n.Kind == NodeKind.AwaitTaskContinuation);
        }
    }
}
