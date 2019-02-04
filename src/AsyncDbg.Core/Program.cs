using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using AsyncDbgCore;
using AsyncDbgCore.Core;
using AsyncDbgCore.New;
using Microsoft.Diagnostics.Runtime;
using AsyncDbg.Extensions;
using HeapExtensions = AsyncDbgCore.Core.HeapExtensions;
using AsyncCausalityDebuggerNew;

namespace AsyncCausalityDebugger
{
    public static class Program
    {
        static Dictionary<ClrType, int> _typeCounts = new Dictionary<ClrType, int>();

        static void DoCoreStuff()
        {
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\BasicDatastructures (3).DMP";
            EntryPoint.DoStuff(path);
        }

        static void Main(string[] args)
        {
            //DoCoreStuff();
            //return;
            var dumpPath = args.Length > 0 ? args[0] : //@"F:\Sources\DumpingGround\HangWithUnwrap\HangWithUnwrap.DMP";
                @"D:\Dumps\AnotherHang\Domino.DMP";//@"d:\Dumps\HangFromBuildMachine2\UnhandledFailure.dmp";

            //var testDumpPath = @"C:\Users\seteplia\AppData\Local\Temp\SemaphoreSlimLiveLock (3).DMP";
            //dumpPath = testDumpPath;

            //var dumpPath = args.Length > 0 ? args[0] : @"d:\Dumps\Domino.dmp";
            //CausalityContext context = CausalityContext.LoadCausalityContextFromDump(dumpPath);
            //context.SaveDgml(dumpPath + ".dgml");

            var context = CausalityContext.LoadCausalityContextFromDump(dumpPath);
            context.SaveDgml(dumpPath + ".dgml");
        }
    }

    public class TypeIndex
    {
        public bool CanCreateNode => Kind != NodeKind.Unknown;
        public NodeKind Kind { get; set; }
        public ClrType RootType { get; private set; }
        public readonly HashSet<ClrType> DerivedTypes = new HashSet<ClrType>(ClrTypeEqualityComparer.Instance);
        public readonly HashSet<ClrInstance> Instances = new HashSet<ClrInstance>(ClrInstanceAddressComparer.Instance);

        public TypeIndex(ClrType rootType)
        {
            SetRoot(rootType);
        }

        public void SetRoot(ClrType rootType)
        {
            RootType = rootType;
            DerivedTypes.Add(rootType);
        }

        public bool AddDerived(ClrType type)
        {
            return DerivedTypes.Add(type);
        }

        public bool AddIfDerived(ClrType type)
        {
            if (DerivedTypes.Contains(type.BaseType))
            {
                return DerivedTypes.Add(type);
            }

            return false;
        }

        public bool ContainsType(ClrType type)
        {
            return type != null && DerivedTypes.Contains(type);
        }

        public static implicit operator TypeIndex(ClrType type)
        {
            return new TypeIndex(type);
        }
    }

    public class CausalityContext
    {
        public ClrHeap Heap;

        public ConcurrentDictionary<ulong, CausalityNode> NodesByAddress = new ConcurrentDictionary<ulong, CausalityNode>();
        public ConcurrentDictionary<ClrInstance, CausalityNode> Nodes = new ConcurrentDictionary<ClrInstance, CausalityNode>(new ClrInstanceAddressComparer());

        //public TypeIndex UnwrapPromise;
        public TypeIndex TaskIndex;
        public TypeIndex ManualResetEventIndex;
        public TypeIndex ManualResetEventSlimIndex;
        public TypeIndex AwaitTaskContinuationIndex;
        public TypeIndex ThreadIndex;
        public TypeIndex TaskCompletionSourceIndex;
        public TypeIndex SemaphoreSlimIndex;
        public TypeIndex SemaphoreWrapperIndex;

        public List<TypeIndex> TypeIndices = new List<TypeIndex>();

        public HashSet<ClrType> WhenAllTypes = new HashSet<ClrType>(ClrTypeEqualityComparer.Instance);

        public List<ClrInstance> ContinuationTrace = new List<ClrInstance>();
        public Stack<ClrInstance> ContinuationStack = new Stack<ClrInstance>();

        public readonly HashSet<ClrInstance> TaskCompletionSentinels = new HashSet<ClrInstance>(ClrInstanceAddressComparer.Instance);

        public readonly ClrType ActionType;
        public readonly ClrType ContinuationWrapperType;
        public readonly ClrType AsyncTaskMethodBuilderType;
        public readonly ClrType ThreadType;

        public ClrType AsyncStateMachineInterface = null;
        public ClrType StandardTaskContinuationType = null;

        public IList<ClrThread> Threads;
        public Dictionary<int, ClrThread> ThreadsById = new Dictionary<int, ClrThread>();
        public readonly ClrRuntime Runtime;

        public CausalityContext(ClrHeap heap)
        {
            Heap = heap;
            ActionType = heap.GetTypeByName("System.Action");
            ContinuationWrapperType = heap.GetTypeByName("System.Runtime.CompilerServices.AsyncMethodBuilderCore+ContinuationWrapper");
            AsyncTaskMethodBuilderType = heap.GetTypeByName("System.Runtime.CompilerServices.AsyncTaskMethodBuilder");
            //if (AsyncTaskMethodBuilderType.Fields.Count == 1)
            //{
            //    //AsyncTaskMethodBuilderType = AsyncTaskMethodBuilderType.Fields[0].Type;
            //    ClrInstance.BypassFieldsByFieldName["<>t__builder"] = AsyncTaskMethodBuilderType.Fields.Where(f => f.Name == "m_builder").First();
            //}

            List<IList<ClrStackFrame>> stacks = new List<IList<ClrStackFrame>>();
            List<ClrThread> emptyThreads = new List<ClrThread>();

            ThreadType = heap.GetTypeByName("System.Threading.Thread");

            Runtime = heap.Runtime;
            Threads = Runtime.Threads;
            foreach (var thread in Threads)
            {
                Console.WriteLine(thread.ManagedThreadId);
                var stackTrace = thread.StackTrace;
                if (stackTrace.Count != 0)
                {
                    stacks.Add(thread.StackTrace);
                }
                else
                {
                    emptyThreads.Add(thread);
                }

                ThreadsById[thread.ManagedThreadId] = thread;
            }
        }

        public static void RunAsyncInspector(string dumpPath)
        {
            DataTarget target = DataTarget.LoadCrashDump(dumpPath ?? @"F:\shared\FromSergey\vstest.executionengine.DMP");

            var dacLocation = target.ClrVersions[0];
            ClrRuntime runtime = dacLocation.CreateRuntime();
            var heap = runtime.Heap;

            var objects = HeapExtensions.EnumerateClrObjects(heap).Where(o => o.Type.ToString().Contains("System.")).Take(100).ToList();
            //var fields = objects[15].Fields;
            //var value = objects[15].GetValue();
            //var proxy = new ClrInstanceDynamicProxy(objects[0]);

            Console.WriteLine(objects.Count);
        }

        public static CausalityContext LoadCausalityContextFromDump(string dumpPath)
        {
            DataTarget target = DataTarget.LoadCrashDump(dumpPath ?? @"F:\shared\FromSergey\vstest.executionengine.DMP");
            var dacLocation = target.ClrVersions[0];
            ClrRuntime runtime = dacLocation.CreateRuntime();
            var heap = runtime.Heap;

            CausalityContext context = new CausalityContext(heap);
            context.Initialize();

            context.Compute();
            return context;
        }

        public void Initialize()
        {
            TaskIndex = Heap.GetTypeByName("System.Threading.Tasks.Task");
            //UnwrapPromise = Heap.GetTypeByName("System.Threading.Tasks.UnwrapPromise");
            ManualResetEventSlimIndex = Heap.GetTypeByName("System.Threading.ManualResetEventSlim");
            ManualResetEventIndex = Heap.GetTypeByName("System.Threading.ManualResetEvent");
            StandardTaskContinuationType = Heap.GetTypeByName("System.Threading.Tasks.StandardTaskContinuation"); // This is ContinueWith continuation
            AwaitTaskContinuationIndex = Heap.GetTypeByName("System.Threading.Tasks.AwaitTaskContinuation");
            ThreadIndex = Heap.GetTypeByName("System.Threading.Thread");
            TaskCompletionSourceIndex = Heap.GetTypeByName("System.Threading.Tasks.TaskCompletionSource");
            SemaphoreSlimIndex = Heap.GetTypeByName("System.Threading.SemaphoreSlim");
            SemaphoreWrapperIndex = Heap.GetTypeByName("ContentStoreInterfaces.Synchronization.SemaphoreSlimToken");

            //UnwrapPromise.Kind = NodeKind.UnwrapPromise;
            TaskIndex.Kind = NodeKind.Task;
            ManualResetEventSlimIndex.Kind = NodeKind.ManualResetEventSlim;
            ManualResetEventIndex.Kind = NodeKind.ManualResetEvent;
            ThreadIndex.Kind = NodeKind.Thread;
            TaskCompletionSourceIndex.Kind = NodeKind.TaskCompletionSource;
            SemaphoreSlimIndex.Kind = NodeKind.SemaphoreSlim;
            SemaphoreWrapperIndex.Kind = NodeKind.SemaphoreWrapper;

            TypeIndices.Add(TaskIndex);
            //TypeIndices.Add(UnwrapPromise);
            TypeIndices.Add(ManualResetEventSlimIndex);
            TypeIndices.Add(ManualResetEventIndex);
            TypeIndices.Add(AwaitTaskContinuationIndex);
            TypeIndices.Add(ThreadIndex);
            TypeIndices.Add(TaskCompletionSourceIndex);
            //TypeIndices.Add(SemaphoreSlimIndex);

            if (TaskIndex.RootType.BaseType.Name.Equals("System.Threading.Tasks.Task"))
            {
                TaskIndex.SetRoot(TaskIndex.RootType.BaseType);
            }

            var taskCompletionSentinelField = TaskIndex.RootType.GetStaticFieldByName("s_taskCompletionSentinel");
            foreach (var appDomain in Runtime.AppDomains)
            {
                var address = taskCompletionSentinelField.GetValue(appDomain);
                if (address is ulong)
                {
                    var type = Heap.GetObjectType((ulong)address);
                    var instance = CreateClrInstance((ulong)address, type);
                    if (instance.Type != null)
                    {
                        TaskCompletionSentinels.Add(instance);
                    }
                }
            }

            bool hasMore = true;
            while (hasMore)
            {
                hasMore = false;
                foreach (var type in Heap.EnumerateTypes())
                {
                    if (type.IsInterface && AsyncStateMachineInterface == null)
                    {
                        if (type.Name == "System.Runtime.CompilerServices.IAsyncStateMachine")
                        {
                            AsyncStateMachineInterface = type;
                        }
                    }

                    if (type.BaseType != null)
                    {
                        foreach (var typeIndex in TypeIndices)
                        {
                            if (typeIndex.AddIfDerived(type))
                            {
                                hasMore = true;
                                break;
                            }
                        }
                    }
                }
            }

            foreach (var taskDerivedTypes in TaskIndex.DerivedTypes)
            {
                if (taskDerivedTypes.Name.StartsWith("System.Threading.Tasks.Task+WhenAllPromise"))
                {
                    WhenAllTypes.Add(taskDerivedTypes);
                }
            }

            foreach (var obj in Heap.EnumerateObjectAddresses())
            {
                ClrType type = Heap.GetObjectType(obj);

                foreach (var typeIndex in TypeIndices)
                {
                    if (typeIndex.CanCreateNode && typeIndex.ContainsType(type))
                    {
                        var instance = CreateClrInstance(obj, type);
                        typeIndex.Instances.Add(instance);

                        if (instance.Type.ToString().Contains("Task<System.Threading.Tasks.Void") && instance.ObjectAddress == 1927676154728)
                        {
                            var d = Heap.GetProxy(instance.ObjectAddress.Value);
                        }

                        GetNode(instance, typeIndex.Kind);
                        break;
                    }
                }
            }
        }

        public CausalityNode GetNode(ClrInstance instance, NodeKind kind = NodeKind.Unknown)
        {
            var node = Nodes.GetOrAdd(instance, task => new CausalityNode(this, task, kind: kind));
            if (node.TaskInstance.ObjectAddress != null)
            {
                NodesByAddress.GetOrAdd(node.TaskInstance.ObjectAddress.Value, node);
            }

            return node;
        }

        private ClrInstance CreateClrInstance(ulong address, ClrType type)
        {
            return new ClrInstance(Heap, address, type);
        }

        public void Compute()
        {
            foreach (NodeKind kind in Enum.GetValues(typeof(NodeKind)))
            {
                foreach (var node in Nodes.Values)
                {
                    node.Link(kind);
                }
            }
        }

        public string SaveDgml(string filePath, bool whatIf = false)
        {
            DgmlWriter writer = new DgmlWriter();
            foreach (var node in Nodes.Values.OrderBy(n => n.Id))
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
            return string.Empty;
        }

        private bool RanToCompletion(CausalityNode node)
        {
            if (!node.IsComplete)
            {
                return false;
            }

            return node.Dependencies.All(n => n.IsComplete || n.Kind == NodeKind.TaskCompletionSource);
        }
    }

    public enum NodeKind
    {
        Unknown,
        Task,
        UnwrapPromise,
        ManualResetEventSlim,
        ManualResetEvent,
        AsyncStateMachine,
        SemaphoreSlim,
        SemaphoreWrapper,
        Thread,

        // Blocking objects must be processed after threads
        BlockingObject,
        TaskCompletionSource,
    }

    public class CausalityNode
    {
        public readonly CausalityContext Context;
        public readonly ClrInstance TaskInstance;
        public CausalityNode CompletionSourceTaskNode;
        public ClrThread Thread;
        public ClrInstance TargetInstance { get; private set; }
        public HashSet<CausalityNode> Dependencies = new HashSet<CausalityNode>();
        public HashSet<CausalityNode> Dependents = new HashSet<CausalityNode>();

        public string Id { get; }

        public bool IsTask => Kind == NodeKind.Task;
        public readonly NodeKind Kind;

        private string _displayString;

        public string DisplayString
        {
            get
            {
                if (_displayString == null)
                {
                    _displayString = ToString();
                }

                return _displayString;
            }
        }

        public bool ProcessingContinuations { get; set; }
        public bool IsComplete
        {
            get
            {
                if (IsTask)
                {
                    var status = Status;
                    return (status == TaskStatus.RanToCompletion || status == TaskStatus.Canceled || status == TaskStatus.Faulted) && !ProcessingContinuations;
                }
                else if (Kind == NodeKind.TaskCompletionSource)
                {
                    return CompletionSourceTaskNode.IsComplete;
                }

                return false;
            }
        }

        public string DisplayStatus
        {
            get
            {
                if (ProcessingContinuations)
                {
                    return nameof(ProcessingContinuations);
                }
                else
                {
                    return Status.ToString();
                }
            }
        }

        public TaskStatus Status => TaskInstanceHelpers.GetStatus(GetFlags());

        private int GetFlags()
        {
            return IsTask ? (int)(TaskInstance["m_stateFlags"].Instance.Value) : 0;
        }

        public CausalityNode(CausalityContext context, ClrInstance task, NodeKind kind)
        {
            Context = context;
            TaskInstance = task;
            Id = task.Value.ToString();
            Kind = kind;
        }

        public void Link(NodeKind kind)
        {
            if (Kind != kind)
            {
                return;
            }

            if (Kind == NodeKind.SemaphoreSlim)
            {
                var asyncHead = TaskInstance["m_asyncHead"].Instance;
                while (asyncHead != null && !asyncHead.IsNull)
                {
                    AddDependent(asyncHead);

                    asyncHead = asyncHead["Next"].Instance;
                }
            }
            else if (Kind == NodeKind.Thread)
            {
                var threadId = TaskInstance["m_ManagedThreadId"]?.Instance?.Value as int?;
                if (threadId != null && Context.ThreadsById.TryGetValue(threadId.Value, out var clrThread))
                {
                    Thread = clrThread;
                    HashSet<ulong> tcsSetResultFrames = new HashSet<ulong>();
                    List<ulong> stackTraceAddresses = new List<ulong>();
                    HashSet<CausalityNode> dependencies = new HashSet<CausalityNode>();
                    //foreach (var blockingObject in Thread.BlockingObjects ?? Enumerable.Empty<BlockingObject>())
                    //{
                    //    var obj = blockingObject.Object;
                    //    var dependency = Context.GetNode(new ClrInstance(Context.Heap, obj, Context.Heap.GetObjectType(obj)), NodeKind.BlockingObject);
                    //    AddEdge(dependency: dependency, dependent: this);

                    //    // Todo: link to owners
                    //    //if (blockingObject.Owner != null)
                    //    //{

                    //    //}

                    //    //if (blockingObject.Owners != null)
                    //    //{
                    //    //    foreach (var owner in blockingObject.Owners)
                    //    //    {

                    //    //    }
                    //    //}
                    //}

                    foreach (var stackFrame in clrThread.StackTrace)
                    {
                        stackTraceAddresses.Add(stackFrame.StackPointer);
                        if (Context.TaskCompletionSourceIndex.ContainsType(stackFrame.Method?.Type) &&
                            stackFrame.Method.Name == "TrySetResult")
                        {
                            tcsSetResultFrames.Add(stackFrame.StackPointer);
                        }
                    }

                    //var stackObjects = clrThread.EnumerateStackObjects().Select(so => new ClrInstance(Context.Heap, so.Object, Context.Heap.GetObjectType(so.Object))).ToList();
                    foreach (var stackObject in clrThread.EnumerateStackObjects())
                    {
                        var so = stackObject;
                        if (Context.NodesByAddress.TryGetValue(stackObject.Object, out var node))
                        {
                            if (node != this)
                            {
                                switch (node.Kind)
                                {
                                    case NodeKind.ManualResetEventSlim:
                                    case NodeKind.ManualResetEvent:
                                        AddEdge(dependency: node, dependent: this);
                                        break;
                                    case NodeKind.TaskCompletionSource:
                                        var stackFrame = GetStackFrame(so, stackTraceAddresses, clrThread);
                                        if (tcsSetResultFrames.Contains(stackFrame?.StackPointer ?? ulong.MaxValue))
                                        {
                                            // TaskCompletion source is waiting for this thread to complete the TrySetResult
                                            //AddEdge(dependency: this, dependent: node);
                                            node.ProcessingContinuations = true;
                                            //AddEdge(dependency: this, dependent: node);
                                        }

                                        AddEdge(dependency: this, dependent: node);
                                        break;
                                    case NodeKind.Thread:
                                    case NodeKind.AsyncStateMachine:
                                    default:
                                        break;
                                }
                            }
                        }
                    }

                    //dependencies.ExceptWith(Dependents);
                    //foreach (var node in dependencies)
                    //{
                    //    AddEdge(dependency: node, dependent: this);
                    //}
                }
            }

            if (kind == NodeKind.TaskCompletionSource)
            {
                ProcessContinuation(TaskInstance, isCurrentNode: true);
            }

            if (!IsTask)
            {
                return;
            }

            Context.ContinuationTrace.Clear();

            if (Context.WhenAllTypes.Contains(TaskInstance.Type))
            {
                foreach (var item in TaskInstance["m_tasks"].Instance.Items)
                {
                    if (!item.IsNull)
                    {
                        AddDependency(item);
                    }
                }
            }

            var nextContinuation = TaskInstance["m_continuationObject"].Instance;

            ProcessContinuation(nextContinuation);
        }

        private ClrStackFrame GetStackFrame(ClrRoot so, List<ulong> stackTraceAddresses, ClrThread clrThread)
        {
            if (so.StackFrame != null)
            {
                return so.StackFrame;
            }

            var result = stackTraceAddresses.BinarySearch(so.Address);
            if (result < 0)
            {
                result = ~result;
            }

            if (result < clrThread.StackTrace.Count)
            {
                return clrThread.StackTrace[result];
            }

            return null;
        }

        private void ProcessContinuation(ClrInstance nextContinuation, bool isCurrentNode = false)
        {
            bool nextIsCurrentNode = isCurrentNode;

            while (nextContinuation != null)
            {
                Context.ContinuationTrace.Add(nextContinuation);
                var continuation = nextContinuation;
                isCurrentNode = nextIsCurrentNode;
                nextIsCurrentNode = false;

                nextContinuation = null;

                if (!continuation.IsNull)
                {
                    if (!isCurrentNode && Context.Nodes.TryGetValue(continuation, out var dependentNode))
                    {
                        AddEdge(dependency: this, dependent: dependentNode);
                    }
                    else if (continuation.IsOfType(Context.ActionType))
                    {
                        var actionTarget = continuation["_target"].Instance;
                        if (actionTarget.IsOfType(Context.ContinuationWrapperType))
                        {
                            nextContinuation = actionTarget["m_continuation"].Instance;
                            continue;
                        }

                        TargetInstance = actionTarget["m_stateMachine"].Instance;
                        if (TargetInstance == null || TargetInstance.IsNull)
                        {

                        }
                        else
                        {
                            FindSemaphores(TargetInstance);

                            if (TargetInstance.TryGetFieldValue("<>t__builder", out var asyncMethodBuilderField))
                            {
                                var asyncMethodBuild = asyncMethodBuilderField.Instance;
                                if (asyncMethodBuild.TryGetFieldValue("m_builder",
                                    out var innerAsyncMethodBuilderField))
                                {
                                    asyncMethodBuild = innerAsyncMethodBuilderField.Instance;
                                }

                                if (asyncMethodBuild.TryGetFieldValue("m_task", out var asyncMethodBuilderTaskField))
                                {
                                    nextContinuation = asyncMethodBuilderTaskField.Instance;
                                }
                                else if (Context.TaskIndex.ContainsType(asyncMethodBuild.Type))
                                {
                                    nextContinuation = asyncMethodBuild;
                                }
                                else
                                {

                                }
                            }
                            else
                            {

                            }
                        }
                    }
                    else if (continuation.IsOfType(Context.StandardTaskContinuationType) || Context.TaskCompletionSourceIndex.ContainsType(continuation.Type))
                    {
                        nextContinuation = continuation["m_task"].Instance;
                    }
                    else if (Context.TaskCompletionSentinels.Contains(continuation))
                    {
                        //COMPLETED
                    }
                    // Need to compare by name since GetTypeByName does not work for the generic type during initialization
                    else if (continuation.Type.Name == "System.Collections.Generic.List<System.Object>")
                    {
                        var size = (int)continuation["_size"].Instance.Value;
                        var items = continuation["_items"].Instance.Items;
                        for (int i = 0; i < size; i++)
                        {
                            var continuationItem = items[i];
                            ProcessContinuation(continuationItem);
                        }
                    }
                    else if (Context.AwaitTaskContinuationIndex.ContainsType(continuation.Type))
                    {
                        nextContinuation = continuation["m_action"].Instance;
                    }
                    else
                    {
                        //continuation.ComputeInfo();
                    }
                }
            }
        }

        private void FindSemaphores(ClrInstance targetInstance)
        {
            foreach (var field in targetInstance.Fields)
            {
                if (Context.SemaphoreWrapperIndex.ContainsType(field.Field.Type))
                {
                    var semaphore = field.Instance["_semaphore"].Instance;
                    if (semaphore != null && !semaphore.IsNull)
                    {
                        AddDependent(semaphore);
                    }
                }
            }
        }

        public void AddDependency(ClrInstance dependency)
        {
            AddEdge(dependency: Context.GetNode(dependency), dependent: this);
        }

        public void AddDependent(ClrInstance dependent)
        {
            AddEdge(dependency: this, dependent: Context.GetNode(dependent));
        }

        public void AddEdge(CausalityNode dependency, CausalityNode dependent)
        {
            if (dependent.ToString().Contains("SemaphoreSlim"))
            {

            }
            if (dependency.TaskInstance.ObjectAddress == 1596100128648L)
            {

            }

            if (dependency.Kind == NodeKind.TaskCompletionSource && dependent.Kind == NodeKind.Task)
            {
                dependency.CompletionSourceTaskNode = dependent;
                dependent.ProcessingContinuations = dependency.ProcessingContinuations;
            }

            if (dependency.Kind == NodeKind.UnwrapPromise)
            {

            }

            dependency.Dependents.Add(dependent);
            dependent.Dependencies.Add(dependency);
        }

        public override string ToString()
        {
            var result = $"({Dependencies.Count}) [{(IsTask ? DisplayStatus.ToString() : Kind.ToString())}] {TaskInstance?.ToString() ?? ""}";
            if (Thread != null && (Dependencies.Count != 0 || Dependents.Count != 0))
            {
                result += Environment.NewLine + string.Join(Environment.NewLine, Thread.StackTrace);
            }

            if (TargetInstance != null)
            {
                result += Environment.NewLine + TargetInstance.ToString();
            }

            if (result.Contains("1596100158496"))
            {

            }


            if (result.Contains("TaskNode"))
            {

            }

            return result;
        }
    }

    //class TaskInstanceHelpers
    //{
    //    // State constants for m_stateFlags;
    //    // The bits of m_stateFlags are allocated as follows:
    //    //   0x40000000 - TaskBase state flag
    //    //   0x3FFF0000 - Task state flags
    //    //   0x0000FF00 - internal TaskCreationOptions flags
    //    //   0x000000FF - publicly exposed TaskCreationOptions flags
    //    //
    //    // See TaskCreationOptions for bit values associated with TaskCreationOptions
    //    //
    //    private const int OptionsMask = 0xFFFF; // signifies the Options portion of m_stateFlags bin: 0000 0000 0000 0000 1111 1111 1111 1111
    //    internal const int TASK_STATE_STARTED = 0x10000;                                       //bin: 0000 0000 0000 0001 0000 0000 0000 0000
    //    internal const int TASK_STATE_DELEGATE_INVOKED = 0x20000;                              //bin: 0000 0000 0000 0010 0000 0000 0000 0000
    //    internal const int TASK_STATE_DISPOSED = 0x40000;                                      //bin: 0000 0000 0000 0100 0000 0000 0000 0000
    //    internal const int TASK_STATE_EXCEPTIONOBSERVEDBYPARENT = 0x80000;                     //bin: 0000 0000 0000 1000 0000 0000 0000 0000
    //    internal const int TASK_STATE_CANCELLATIONACKNOWLEDGED = 0x100000;                     //bin: 0000 0000 0001 0000 0000 0000 0000 0000
    //    internal const int TASK_STATE_FAULTED = 0x200000;                                      //bin: 0000 0000 0010 0000 0000 0000 0000 0000
    //    internal const int TASK_STATE_CANCELED = 0x400000;                                     //bin: 0000 0000 0100 0000 0000 0000 0000 0000
    //    internal const int TASK_STATE_WAITING_ON_CHILDREN = 0x800000;                          //bin: 0000 0000 1000 0000 0000 0000 0000 0000
    //    internal const int TASK_STATE_RAN_TO_COMPLETION = 0x1000000;                           //bin: 0000 0001 0000 0000 0000 0000 0000 0000
    //    internal const int TASK_STATE_WAITINGFORACTIVATION = 0x2000000;                        //bin: 0000 0010 0000 0000 0000 0000 0000 0000
    //    internal const int TASK_STATE_COMPLETION_RESERVED = 0x4000000;                         //bin: 0000 0100 0000 0000 0000 0000 0000 0000
    //    internal const int TASK_STATE_THREAD_WAS_ABORTED = 0x8000000;                          //bin: 0000 1000 0000 0000 0000 0000 0000 0000
    //    internal const int TASK_STATE_WAIT_COMPLETION_NOTIFICATION = 0x10000000;               //bin: 0001 0000 0000 0000 0000 0000 0000 0000
    //    //This could be moved to InternalTaskOptions enum
    //    internal const int TASK_STATE_EXECUTIONCONTEXT_IS_NULL = 0x20000000;                   //bin: 0010 0000 0000 0000 0000 0000 0000 0000
    //    internal const int TASK_STATE_TASKSCHEDULED_WAS_FIRED = 0x40000000;                    //bin: 0100 0000 0000 0000 0000 0000 0000 0000

    //    // A mask for all of the final states a task may be in
    //    private const int TASK_STATE_COMPLETED_MASK = TASK_STATE_CANCELED | TASK_STATE_FAULTED | TASK_STATE_RAN_TO_COMPLETION;

    //    // Values for ContingentProperties.m_internalCancellationRequested.
    //    private const int CANCELLATION_REQUESTED = 0x1;

    //    public static TaskStatus GetStatus(int sf)
    //    {
    //        TaskStatus rval;

    //        // get a cached copy of the state flags.  This should help us
    //        // to get a consistent view of the flags if they are changing during the
    //        // execution of this method.

    //        if ((sf & TASK_STATE_FAULTED) != 0)
    //            rval = TaskStatus.Faulted;
    //        else if ((sf & TASK_STATE_CANCELED) != 0)
    //            rval = TaskStatus.Canceled;
    //        else if ((sf & TASK_STATE_RAN_TO_COMPLETION) != 0)
    //            rval = TaskStatus.RanToCompletion;
    //        else if ((sf & TASK_STATE_WAITING_ON_CHILDREN) != 0)
    //            rval = TaskStatus.WaitingForChildrenToComplete;
    //        else if ((sf & TASK_STATE_DELEGATE_INVOKED) != 0)
    //            rval = TaskStatus.Running;
    //        else if ((sf & TASK_STATE_STARTED) != 0)
    //            rval = TaskStatus.WaitingToRun;
    //        else if ((sf & TASK_STATE_WAITINGFORACTIVATION) != 0)
    //            rval = TaskStatus.WaitingForActivation;
    //        else
    //            rval = TaskStatus.Created;

    //        return rval;
    //    }
    //}

    class ClrInstanceAddressComparer : EqualityComparer<ClrInstance>
    {
        public static readonly ClrInstanceAddressComparer Instance = new ClrInstanceAddressComparer();

        public override bool Equals(ClrInstance x, ClrInstance y)
        {
            if (x?.ObjectAddress == null || y?.ObjectAddress == null)
            {
                return false;
            }

            return x?.ObjectAddress == y?.ObjectAddress;
        }

        public override int GetHashCode(ClrInstance obj)
        {
            return obj?.ObjectAddress?.GetHashCode() ?? 0;
        }
    }

    class ClrTypeEqualityComparer : EqualityComparer<ClrType>
    {
        public static readonly ClrTypeEqualityComparer Instance = new ClrTypeEqualityComparer();
        public override bool Equals(ClrType x, ClrType y)
        {

            return (
                (x?.MetadataToken == y?.MetadataToken) ||
                (x?.Name == y?.Name)) &&
                x?.Module.FileName == y?.Module.FileName;
        }

        public override int GetHashCode(ClrType obj)
        {
            return (obj?.MetadataToken.GetHashCode() ?? 0) ^ (obj?.Module.FileName?.GetHashCode() ?? 1);
        }
    }

    public class EnumTypeInfo
    {
        public Dictionary<string, EnumValue> Values = new Dictionary<string, EnumValue>();
        public Dictionary<object, EnumValue> ValuesByValue = new Dictionary<object, EnumValue>();
    }

    public class EnumValue
    {
        public string Name;
        public long Value;

        public EnumValue()
        {
        }

        public EnumValue(long value, List<EnumValue> values)
        {
            Name = string.Join(" | ", values.Select(v => v.Name));
            Value = value;
        }

        public override string ToString()
        {
            return Name;
        }

        public override bool Equals(object obj)
        {
            if (obj is string)
            {
                return string.Equals(Name, (string)obj, StringComparison.OrdinalIgnoreCase);
            }

            return base.Equals(obj);
        }
    }


    public class ClrInstance
    {
        private static readonly ClrFieldValue[] EmptyFields = new ClrFieldValue[0];
        private static readonly ClrInstance[] EmptyItems = new ClrInstance[0];

        public static Dictionary<string, ClrInstanceField> BypassFieldsByFieldName = new Dictionary<string, ClrInstanceField>();

        public ClrHeap Heap;
        public object Value;
        public ClrType OriginalType;
        private ClrType _primitiveTypeOptional;

        private static readonly ConditionalWeakTable<ClrType, EnumTypeInfo> _enumTypeInfos = new ConditionalWeakTable<ClrType, EnumTypeInfo>();

        public ulong? ObjectAddress => IsObject ? Value as ulong? : null;

        public bool IsObject;

        public ClrInstance(ClrHeap heap, object value, ClrType type)
        {
            Heap = heap;
            Value = value;
            OriginalType = type;
            ComputeInfo();
        }

        public void ComputeInfo()
        {
            var value = Value;
            var type = OriginalType;

            if (type != null && value != null)
            {
                IsObject = !type.IsIntrinsic();

                if (type.IsEnum)
                {
                    var info = GetOrCreateEnumTypeInfo(type);
                    if (info.ValuesByValue.TryGetValue(Value, out var e))
                    {
                        Value = e;
                    }
                    else
                    {
                        List<EnumValue> values = new List<EnumValue>();
                        long bits = Convert.ToInt64(Value);
                        foreach (var enumValue in info.ValuesByValue.Values)
                        {
                            if ((enumValue.Value & bits) == enumValue.Value)
                            {
                                values.Add(enumValue);
                            }
                        }

                        Value = new EnumValue(bits, values);
                    }
                }
            }

            if (!IsObject || type.IsValueClass)
            {
                _primitiveTypeOptional = type;
            }
        }

        public bool IsNull => IsObject && ObjectAddress == 0;

        public bool IsOfType(ClrType type)
        {
            return ClrTypeEqualityComparer.Instance.Equals(Type, type);
        }

        private EnumTypeInfo GetOrCreateEnumTypeInfo(ClrType type)
        {
            return _enumTypeInfos.GetValue(type, GetOrAddEnumTypeInfo);
        }

        private EnumTypeInfo GetOrAddEnumTypeInfo(ClrType type)
        {
            EnumTypeInfo info = new EnumTypeInfo();
            foreach (var enumName in type.GetEnumNames())
            {
                type.TryGetEnumValue(enumName, out object value);
                var intValue = ((IConvertible)value).ToInt32(null);
                var enumValue = new EnumValue()
                {
                    Name = enumName,
                    Value = intValue
                };

                // It is possible to have duplicates
                info.Values[enumName] = enumValue;
                info.ValuesByValue[value] = enumValue;
            }

            return info;
        }


        public ClrType Type
        {
            get
            {
                if (_primitiveTypeOptional != null)
                {
                    return _primitiveTypeOptional;
                }

                if (IsObject)
                {
                    return Heap.GetObjectType((ulong)Value);
                }

                return _primitiveTypeOptional;
            }
        }

        public ClrFieldValue this[string fieldName]
        {
            get
            {
                var propertyName = $"<{fieldName}>k__BackingField";
                foreach (var field in Fields)
                {
                    if (field.Field.Name == fieldName || field.Field.Name == propertyName)
                    {
                        return field;
                    }
                }

                return null;
            }
        }

        public bool TryGetFieldValue(string fieldName, out ClrFieldValue fieldValue)
        {
            fieldValue = this[fieldName];
            return fieldValue != null;
        }

        private ClrFieldValue[] _fields;
        public ClrFieldValue[] Fields
        {
            get
            {
                if (_fields == null)
                {
                    _fields = IsObject ? ComputeFields() : EmptyFields;
                }

                return _fields;
            }
        }

        private ClrInstance[] _items;
        public ClrInstance[] Items
        {
            get
            {
                if (_items == null)
                {
                    _items = IsObject && Type.IsArray ? ComputeItems() : EmptyItems;
                }

                return _items;
            }
        }

        private ClrInstance[] ComputeItems()
        {
            var address = (ulong)Value;
            var length = Type.GetArrayLength(address);
            var items = new ClrInstance[length];
            var elementType = Type.ComponentType;
            for (int i = 0; i < length; i++)
            {
                var value = Type.GetArrayElementValue(address, i);
                items[i] = CreateItemInstance(value, elementType, i);
            }

            return items;
        }

        private ClrInstance CreateItemInstance(object value, ClrType type, int index)
        {
            if (!type.IsIntrinsic() && value != null)
            {
                type = Heap.GetObjectType((ulong)value) ?? type;
            }

            if (value == null)
            {
                value = Type.GetArrayElementAddress(ObjectAddress.Value, index);
            }

            return new ClrInstance(Heap, value, type);
        }

        private ClrFieldValue[] ComputeFields()
        {
            HashSet<int> offsets = new HashSet<int>();
            List<ClrFieldValue> fields = new List<ClrFieldValue>();

            var type = Type;
            while (type != null)
            {
                foreach (var typeField in type.Fields)
                {
                    if (typeField.Name == "<>u__1")
                    {
                        continue;
                    }

                    if (offsets.Add(typeField.Offset))
                    {
                        fields.Add(ClrFieldValue.Create(typeField, this));
                    }
                }

                type = type.BaseType;
            }

            return fields.ToArray();
        }

        public override string ToString()
        {
            // For some weird reason String is not an object!
            if (Type?.IsString == true)
            {
                return $"\"{Value}\"";
            }

            if (IsObject)
            {
                return $"{Type?.Name} ({Value})";
            }

            if (Value == null)
            {
                return "<NO VALUE>";
            }

            string suffix = string.Empty;
            if (Type.IsOfTypes(typeof(long), typeof(ulong)))
            {
                suffix = "L";
            }
            else if (Type.IsOfTypes(typeof(double), typeof(float)))
            {
                suffix = "f";
            }

            return $"{Value}{suffix}";
        }
    }

    public class ClrFieldValue
    {
        public ClrInstanceField Field;
        public ClrInstance Instance;

        internal static ClrFieldValue Create(ClrInstanceField typeField, ClrInstance instance)
        {
            var getValueTypeField = typeField;
            //if (typeField.Type.MetadataToken == 0 && typeField.Type.Name == "ERROR")
            //{
            //    if (ClrInstance.BypassFieldsByFieldName.TryGetValue(typeField.Name, out var bypassField))
            //    {
            //        getValueTypeField = bypassField;
            //    }
            //}

            var value = getValueTypeField.GetValue(instance.ObjectAddress.Value);

            var type = getValueTypeField.Type;

            if (!type.IsIntrinsic() && value != null)
            {
                type = instance.Heap.GetObjectType((ulong)value) ?? type;
            }

            if (value == null)
            {
                value = instance.ObjectAddress.Value + (ulong)typeField.Offset;
            }

            return new ClrFieldValue()
            {
                Instance = new ClrInstance(instance.Heap, value, type),
                Field = typeField
            };
        }

        public override string ToString()
        {
            return $"{Field.Name} = {Instance}";
        }
    }
}
