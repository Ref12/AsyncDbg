using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using AsyncDbg.Core;
using Microsoft.Diagnostics.Runtime;
using System.IO;

#nullable enable

namespace AsyncDbg.Causality
{
    public class CausalityContext
    {
        public IEnumerable<CausalityNode> Nodes => _nodesByAddress.Values;

        private readonly ConcurrentDictionary<ulong, CausalityNode> _nodesByAddress = new ConcurrentDictionary<ulong, CausalityNode>();
        private readonly Dictionary<int, ClrThread> _threadsById;

        public TypesRegistry Registry { get; }

        public HeapContext Heap { get; }

        public ClrRuntime Runtime => Heap.DefaultHeap.Runtime;

        public CausalityContext(HeapContext heapContext)
        {
            Registry = TypesRegistry.Create(heapContext);
            Heap = heapContext;

            _threadsById = heapContext.DefaultHeap.Runtime.Threads.ToDictionary(t => t.ManagedThreadId, t => t);

            foreach (var (instance, kind) in Registry.EnumerateRegistry())
            {
                GetOrCreate(instance, kind);
            }

            Console.WriteLine("Successfully loaded clr instances.");
        }

        public static CausalityContext LoadCausalityContextFromDump(string dumpPath)
        {
            var heapContext = new HeapContext(() =>
            {
                var target = DataTarget.LoadCrashDump(dumpPath);
                var dacLocation = target.ClrVersions[0];
                var runtime = dacLocation.CreateRuntime();
                var heap = runtime.Heap;
                return heap;
            });

            var context = new CausalityContext(heapContext);

            Console.WriteLine("Linking the nodes together.");
            context.LinkNodes();
            return context;
        }

        public bool TryGetNodeFor(ClrInstance instance, [NotNullWhen(true)]out CausalityNode? result)
        {
            if (instance.ObjectAddress != null)
            {
                return _nodesByAddress.TryGetValue(instance.ObjectAddress.Value, out result);
            }

            result = null;
            return false;
        }

        public bool TryGetThreadById(int threadId, [NotNullWhen(true)]out ClrThread? thread) => _threadsById.TryGetValue(threadId, out thread);

        public CausalityNode GetNode(ClrInstance instance)
        {
            Contract.AssertNotNull(instance.ObjectAddress);

            return _nodesByAddress.GetOrAdd(instance.ObjectAddress.Value, task => CausalityNode.Create(this, instance, kind: NodeKind.Unknown));
        }

        public bool TryGetNodeAt(ulong address, [NotNullWhen(true)]out CausalityNode? result)
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
            var node = _nodesByAddress.GetOrAdd(instance.ObjectAddress.Value, _ => CausalityNode.Create(this, instance, kind: kind));

            return node;
        }

        public void LinkNodes()
        {
            foreach (var node in _nodesByAddress.Values)
            {
                node.Link();
            }
        }

        public string OverallStats(string filePath)
        {
            //var list = new List<AsyncStack>();

            var roots = _nodesByAddress.Values.Where(n => n.Dependencies.Count == 0).ToArray();
            //foreach (var node in _nodesByAddress.Values.OrderBy(v => v.Id))
            //foreach (var node in _nodesByAddress.Values.OrderBy(v => v.Id))
            //{
            //    if (node.Dependencies.Count == 0 && node.Dependents.Count == 0)
            //    {
            //        continue;
            //    }

            //    if (RanToCompletion(node))
            //    {
            //        continue;
            //    }

            //    writer.AddNode(new DgmlWriter.Node(id: node.Id, label: node.ToString()));


            //    foreach (var dependency in node.Dependencies.OrderBy(d => d.Id))
            //    {
            //        writer.AddLink(new DgmlWriter.Link(
            //            source: node.Id,
            //            target: dependency.Id,
            //            label: null));
            //    }

            //    //if (node.Kind == NodeKind.AwaitTaskContinuation)
            //    //{
            //    //    foreach (var dependency in node.Dependents.OrderBy(d => d.Id))
            //    //    {
            //    //        writer.AddLink(new DgmlWriter.Link(
            //    //            source: dependency.Id,
            //    //            target: node.Id,
            //    //            label: null));
            //    //    }
            //    //}
            //}

            var nodes = _nodesByAddress.Values.ToArray();
            var waitingOnSempahore = nodes.Where(t => t.ToString().Contains("SemaphoreSlimToken") && t.ToString().Contains("Wait")).ToList();
            var pinBatches = nodes.Where(t => t.ToString().Contains("Grpc") && t.ToString().Contains("PinBatchAsync"))
                .ToArray();

            var fetchPackages = nodes.Where(t => t.ToString().Contains("NugetCacheStorage") && t.ToString().Contains("FetchPackageAsync"))
                .ToArray();

            return string.Empty;
        }

        public string SaveDgml(string filePath, bool whatIf = false)
        {
            var writer = new DgmlWriter();

            Console.WriteLine("Analyzing the async graphs...");
            var visualContext = VisuaNodes.VisualContext.Create(Nodes, simplify: false);

            foreach (var node in visualContext.EnumerateVisualNodes())
            {
                writer.AddNode(new DgmlWriter.Node(id: node.Id, label: node.DisplayText));

                //foreach (var dependency in node.Dependencies.OrderBy(d => d.Id))
                foreach (var dependency in node.AwaitsOn)
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

            if (File.Exists(filePath))
            {
                var existingFile = File.ReadAllText(filePath);
                var newFile = writer.SerializeAsString();

                if (existingFile == newFile)
                {
                    Console.WriteLine("No changes were detected!");
                }
                else
                {
                    Console.WriteLine("The diagram has changaed!");
                    writer.Serialize(filePath);
                }
            }
            else
            {
                writer.Serialize(filePath);
            }

            return writer.SerializeAsString();
        }
    }
}
