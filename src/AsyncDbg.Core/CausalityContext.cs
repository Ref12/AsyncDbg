﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncCausalityDebugger;
using AsyncDbgCore.New;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace AsyncCausalityDebuggerNew
{
    public class CausalityContext
    {

        public static void FooBar<T>(T t) where T : object
        {

        }

        public static void FooBar2()
        {
            string? s = null;
            FooBar(s);
            FooBar(42);

            string s2 = "";
            FooBar(s2);
        }

        public IEnumerable<CausalityNode> Nodes => _nodesByAddress.Values;

        private readonly ConcurrentDictionary<ulong, CausalityNode> _nodesByAddress = new ConcurrentDictionary<ulong, CausalityNode>();
        private readonly Dictionary<int, ClrThread> _threadsById;

        public TypeIndex AwaitTaskContinuationIndex => Registry.AwaitTaskContinuationIndex;
        public TypeIndex TaskCompletionSourceIndex => Registry.TaskCompletionSourceIndex;

        public ClrType ContinuationWrapperType => Registry.ContinuationWrapperType;

        public ClrType StandardTaskContinuationType => Registry.StandardTaskContinuationType;

        public TypesRegistry Registry { get; }

        public HeapContext Heap { get; }

        public CausalityContext(HeapContext heapContext)
        {
            Registry = TypesRegistry.Create(heapContext);
            Heap = heapContext;

            _threadsById = heapContext.DefaultHeap.Runtime.Threads.ToDictionary(t => t.ManagedThreadId, t => t);

            var runtime = heapContext.DefaultHeap.Runtime;
            foreach (var thread in runtime.Threads.Where(t => t.EnumerateStackTrace().Any()))
            {
                Console.WriteLine("### Thread {0}", thread.OSThreadId);
                Console.WriteLine("Thread type: {0}",
                    thread.IsBackground ? "Background"
                    : thread.IsGC ? "GC"
                    : "Foreground");
                Console.WriteLine("");
                Console.WriteLine("Stack trace:");
                foreach (var stackFrame in thread.EnumerateStackTrace())
                {
                    Console.WriteLine("* {0}", stackFrame.DisplayString);
                }
            }

            foreach (var (instance, kind) in Registry.EnumerateRegistry())
            {
                GetOrCreate(instance, kind);
            }

            var items = runtime.ThreadPool.EnumerateManagedWorkItems().ToList();
            Console.WriteLine("Done");
        }

        

        public static CausalityContext LoadCausalityContextFromDump(string dumpPath)
        {
            HeapContext heapContext = new HeapContext(() =>
            {
                var target = DataTarget.LoadCrashDump(dumpPath);
                var dacLocation = target.ClrVersions[0];
                var runtime = dacLocation.CreateRuntime();
                var heap = runtime.Heap;
                return heap;
            });

            var context = new CausalityContext(heapContext);

            context.Compute();
            return context;
        }

        public bool TryGetNodeFor(ClrInstance instance, out CausalityNode result)
        {
            if (instance.ObjectAddress != null)
            {
                return _nodesByAddress.TryGetValue(instance.ObjectAddress.Value, out result);
            }

            result = null;
            return false;
        }

        public bool TryGetThreadById(int threadId, out ClrThread thread) => _threadsById.TryGetValue(threadId, out thread);

        public CausalityNode GetNode(ClrInstance instance)
        {
            if (instance.IsNull)
            {
                return null;
            }

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

        public string OverallStats(string filePath)
        {
            var list = new List<AsyncStack>();

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
            //var roots = _nodesByAddress.Values.Where(n => n.Dependencies.Count != 0).ToArray();
            var roots = _nodesByAddress.Values.Where(r => r.IsRoot).OrderBy(v => v.Id).ToArray();

            var visualContext = VisualContext.Create(this);
            foreach (var node in visualContext.ActiveNodes)
            //foreach (var node in roots.OrderBy(v => v.Id))
            {
                //if (node.IsRoot && node.IsLeaf)
                //{
                //    continue;
                //}
                //if (node.Dependencies.Count == 0 && node.Dependents.Count == 0)
                //{
                //    continue;
                //}

                //if (node.ToString().Contains("DBS.Tools.HashedPackage") || node.ToString().Contains("FetchHashedPackageDefinitionAsync"))
                //{
                //    continue;
                //}

                //if (node.ToString().Contains("SemaphoreSlimToken") && node.ToString().Contains("Wait"))
                //{
                //    continue;
                //}

                //if (node.ToString().Contains("Grpc.GrpcClient") && node.ToString().Contains("PinBatchAsync"))
                //{
                //    continue;
                //}

                //if (RanToCompletion(node))
                //{
                //    continue;
                //}

                //var visualNode = node.CreateVisualNode();
                writer.AddNode(new DgmlWriter.Node(id: node.CausalityNode.Id, label: node.ToString()));
                
                //foreach (var dependency in node.Dependencies.OrderBy(d => d.Id))
                foreach (var dependency in node.WaitingOn.OrderBy(d => d.Id))
                {
                    writer.AddLink(new DgmlWriter.Link(
                        source: node.Id,
                        target: dependency.Id,
                        label: null));
                }

                //if (node.Kind == NodeKind.AwaitTaskContinuation)
                //{
                //    foreach (var dependency in node.Dependents.OrderBy(d => d.Id))
                //    {
                //        writer.AddLink(new DgmlWriter.Link(
                //            source: dependency.Id,
                //            target: node.Id,
                //            label: null));
                //    }
                //}
            }


            if (whatIf)
            {
                return writer.SerializeAsString();
            }

            writer.Serialize(filePath);

            return writer.SerializeAsString();
        }

        // True if the node represents a completed task/sequence of tasks. Needed because we don't want to show them.
        public bool RanToCompletion(CausalityNode node)
        {
            if (!node.IsComplete)
            {
                return false;
            }

            return node.Dependencies.All(n => n.IsComplete || n.Kind == NodeKind.TaskCompletionSource || n.Kind == NodeKind.AwaitTaskContinuation || node.Kind == NodeKind.AsyncStateMachine);
        }
    }
}
