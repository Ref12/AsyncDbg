// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AsyncCausalityDebugger;
using AsyncDbgCore.Core;
using Microsoft.Diagnostics.Runtime;
using AsyncDbg.Extensions;
using ClrInstance = AsyncDbgCore.Core.ClrInstance;

namespace AsyncDbgCore
{
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

    public class CausalityContext2
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

        public CausalityContext2(ClrHeap heap)
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

            var objects = heap.EnumerateClrObjects().Where(o => o.Type.ToString().Contains("System.")).Take(100).ToList();
            //var fields = objects[15].Fields;
            //var value = objects[15].GetValue();
            //var proxy = new ClrInstanceDynamicProxy(objects[0]);

            //Console.WriteLine(objects.Count);
        }

        public static CausalityContext2 LoadCausalityContextFromDump(string dumpPath)
        {
            DataTarget target = DataTarget.LoadCrashDump(dumpPath ?? @"F:\shared\FromSergey\vstest.executionengine.DMP");
            var dacLocation = target.ClrVersions[0];
            ClrRuntime runtime = dacLocation.CreateRuntime();
            var heap = runtime.Heap;

            var context = new CausalityContext2(heap);
            context.Initialize();

            context.Compute();
            return context;
        }

        public void Initialize()
        {
            StandardTaskContinuationType = Heap.GetTypeByName("System.Threading.Tasks.StandardTaskContinuation"); // This is ContinueWith continuation

            TaskIndex = Heap.GetTypeByName("System.Threading.Tasks.Task");
            ManualResetEventSlimIndex = Heap.GetTypeByName("System.Threading.ManualResetEventSlim");
            ManualResetEventIndex = Heap.GetTypeByName("System.Threading.ManualResetEvent");
            AwaitTaskContinuationIndex = Heap.GetTypeByName("System.Threading.Tasks.AwaitTaskContinuation");

            ThreadIndex = Heap.GetTypeByName("System.Threading.Thread");
            TaskCompletionSourceIndex = Heap.GetTypeByName("System.Threading.Tasks.TaskCompletionSource");
            SemaphoreSlimIndex = Heap.GetTypeByName("System.Threading.SemaphoreSlim");
            SemaphoreWrapperIndex = Heap.GetTypeByName("ContentStoreInterfaces.Synchronization.SemaphoreSlimToken");

            TaskIndex.Kind = NodeKind.Task;
            ManualResetEventSlimIndex.Kind = NodeKind.ManualResetEventSlim;
            ManualResetEventIndex.Kind = NodeKind.ManualResetEvent;
            ThreadIndex.Kind = NodeKind.Thread;

            TaskCompletionSourceIndex.Kind = NodeKind.TaskCompletionSource;
            SemaphoreSlimIndex.Kind = NodeKind.SemaphoreSlim;
            SemaphoreWrapperIndex.Kind = NodeKind.SemaphoreWrapper;

            TypeIndices.Add(TaskIndex);
            TypeIndices.Add(ManualResetEventSlimIndex);
            TypeIndices.Add(ManualResetEventIndex);
            TypeIndices.Add(AwaitTaskContinuationIndex);
            TypeIndices.Add(ThreadIndex);
            TypeIndices.Add(TaskCompletionSourceIndex);

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

                        GetNode(instance, typeIndex.Kind);
                        break;
                    }
                }
            }
        }

        public CausalityNode GetNode(ClrInstance instance, NodeKind kind = NodeKind.Unknown)
        {
            var node = Nodes.GetOrAdd(instance, task => new CausalityNode(this, task, kind: kind));
            if (node.TaskInstance.ObjectAddress != 0)
            {
                NodesByAddress.GetOrAdd(node.TaskInstance.ObjectAddress, node);
            }

            return node;
        }

        private ClrInstance CreateClrInstance(ulong address, ClrType type)
        {
            return new ClrInstance(address, Heap, type);
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
            foreach (var node in Nodes.Values)
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
                foreach (var dependency in node.Dependencies)
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
        public readonly CausalityContext2 Context;
        public readonly ClrInstance TaskInstance;
        public CausalityNode CompletionSourceTaskNode;
        public ClrThread Thread;
        public ClrInstance TargetInstance { get; private set; }
        public HashSet<CausalityNode> Dependencies = new HashSet<CausalityNode>();
        public HashSet<CausalityNode> Dependents = new HashSet<CausalityNode>();

        public string Id { get; }

        // TODO: should support ValueTasks as well!
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
                    return CompletionSourceTaskNode.IsComplete == true;
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
            return IsTask ? (int)(TaskInstance["m_stateFlags"].Value) : 0;
        }

        public CausalityNode(CausalityContext2 context, ClrInstance task, NodeKind kind)
        {
            Context = context;
            TaskInstance = task;
            Id = task.GetValue().ToString();
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
                var asyncHead = TaskInstance["m_asyncHead"];
                while (asyncHead != null && !asyncHead.IsNull)
                {
                    AddDependent(asyncHead);

                    asyncHead = asyncHead["Next"];
                }
            }
            else if (Kind == NodeKind.Thread)
            {
                var threadId = TaskInstance["m_ManagedThreadId"]?.Value as int?;
                if (threadId != null && Context.ThreadsById.TryGetValue(threadId.Value, out var clrThread))
                {
                    Thread = clrThread;
                    HashSet<ulong> tcsSetResultFrames = new HashSet<ulong>();
                    List<ulong> stackTraceAddresses = new List<ulong>();
                    HashSet<CausalityNode> dependencies = new HashSet<CausalityNode>();
                    //foreach (var blockingObject in Thread.BlockingObjects ?? Enumerable.Empty<BlockingObject>())
                    //{
                    //    var obj = blockingObject.Object;
                    //    var dependency = Context.GetNode(ClrInstance.FromAddress(obj, Context.Heap), NodeKind.BlockingObject);
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
                foreach (var item in TaskInstance["m_tasks"].Items)
                {
                    if (!item.IsNull)
                    {
                        AddDependency(item);
                    }
                }
            }

            var nextContinuation = TaskInstance["m_continuationObject"];

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
                        var actionTarget = continuation["_target"];
                        if (actionTarget.IsOfType(Context.ContinuationWrapperType))
                        {
                            nextContinuation = actionTarget["m_continuation"];
                            continue;
                        }

                        // This code makes no sense! actionTarget is System.Action, there is no m_stateMachine there!
                        TargetInstance = actionTarget.TryGetField("m_stateMachine");
                        if (TargetInstance == null || TargetInstance.IsNull)
                        {

                        }
                        else
                        {
                            FindSemaphores(TargetInstance);

                            if (TargetInstance.TryGetFieldValue("<>t__builder", out var asyncMethodBuilderField))
                            {
                                var asyncMethodBuild = asyncMethodBuilderField;
                                if (asyncMethodBuild.TryGetFieldValue("m_builder",
                                    out var innerAsyncMethodBuilderField))
                                {
                                    asyncMethodBuild = innerAsyncMethodBuilderField;
                                }

                                if (asyncMethodBuild.TryGetFieldValue("m_task", out var asyncMethodBuilderTaskField))
                                {
                                    nextContinuation = asyncMethodBuilderTaskField;
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
                        nextContinuation = continuation["m_task"];
                    }
                    else if (Context.TaskCompletionSentinels.Contains(continuation))
                    {
                        //COMPLETED
                    }
                    // Need to compare by name since GetTypeByName does not work for the generic type during initialization
                    else if (continuation.Type.Name == "System.Collections.Generic.List<System.Object>")
                    {
                        var size = (int)continuation["_size"].Value;
                        var items = continuation["_items"].Items;
                        for (int i = 0; i < size; i++)
                        {
                            var continuationItem = items[i];
                            ProcessContinuation(continuationItem);
                        }
                    }
                    else if (Context.AwaitTaskContinuationIndex.ContainsType(continuation.Type))
                    {
                        nextContinuation = continuation["m_action"];
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
                    var semaphore = field.Instance["_semaphore"];
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
                result += Environment.NewLine + TargetInstance;
            }

            return result;
        }
    }

    class TaskInstanceHelpers
    {
        // State constants for m_stateFlags;
        // The bits of m_stateFlags are allocated as follows:
        //   0x40000000 - TaskBase state flag
        //   0x3FFF0000 - Task state flags
        //   0x0000FF00 - internal TaskCreationOptions flags
        //   0x000000FF - publicly exposed TaskCreationOptions flags
        //
        // See TaskCreationOptions for bit values associated with TaskCreationOptions
        //
        private const int OptionsMask = 0xFFFF; // signifies the Options portion of m_stateFlags bin: 0000 0000 0000 0000 1111 1111 1111 1111
        internal const int TASK_STATE_STARTED = 0x10000;                                       //bin: 0000 0000 0000 0001 0000 0000 0000 0000
        internal const int TASK_STATE_DELEGATE_INVOKED = 0x20000;                              //bin: 0000 0000 0000 0010 0000 0000 0000 0000
        internal const int TASK_STATE_DISPOSED = 0x40000;                                      //bin: 0000 0000 0000 0100 0000 0000 0000 0000
        internal const int TASK_STATE_EXCEPTIONOBSERVEDBYPARENT = 0x80000;                     //bin: 0000 0000 0000 1000 0000 0000 0000 0000
        internal const int TASK_STATE_CANCELLATIONACKNOWLEDGED = 0x100000;                     //bin: 0000 0000 0001 0000 0000 0000 0000 0000
        internal const int TASK_STATE_FAULTED = 0x200000;                                      //bin: 0000 0000 0010 0000 0000 0000 0000 0000
        internal const int TASK_STATE_CANCELED = 0x400000;                                     //bin: 0000 0000 0100 0000 0000 0000 0000 0000
        internal const int TASK_STATE_WAITING_ON_CHILDREN = 0x800000;                          //bin: 0000 0000 1000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_RAN_TO_COMPLETION = 0x1000000;                           //bin: 0000 0001 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_WAITINGFORACTIVATION = 0x2000000;                        //bin: 0000 0010 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_COMPLETION_RESERVED = 0x4000000;                         //bin: 0000 0100 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_THREAD_WAS_ABORTED = 0x8000000;                          //bin: 0000 1000 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_WAIT_COMPLETION_NOTIFICATION = 0x10000000;               //bin: 0001 0000 0000 0000 0000 0000 0000 0000
        //This could be moved to InternalTaskOptions enum
        internal const int TASK_STATE_EXECUTIONCONTEXT_IS_NULL = 0x20000000;                   //bin: 0010 0000 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_TASKSCHEDULED_WAS_FIRED = 0x40000000;                    //bin: 0100 0000 0000 0000 0000 0000 0000 0000

        // A mask for all of the final states a task may be in
        private const int TASK_STATE_COMPLETED_MASK = TASK_STATE_CANCELED | TASK_STATE_FAULTED | TASK_STATE_RAN_TO_COMPLETION;

        // Values for ContingentProperties.m_internalCancellationRequested.
        private const int CANCELLATION_REQUESTED = 0x1;

        public static TaskStatus GetStatus(int sf)
        {
            TaskStatus rval;

            // get a cached copy of the state flags.  This should help us
            // to get a consistent view of the flags if they are changing during the
            // execution of this method.

            if ((sf & TASK_STATE_FAULTED) != 0)
                rval = TaskStatus.Faulted;
            else if ((sf & TASK_STATE_CANCELED) != 0)
                rval = TaskStatus.Canceled;
            else if ((sf & TASK_STATE_RAN_TO_COMPLETION) != 0)
                rval = TaskStatus.RanToCompletion;
            else if ((sf & TASK_STATE_WAITING_ON_CHILDREN) != 0)
                rval = TaskStatus.WaitingForChildrenToComplete;
            else if ((sf & TASK_STATE_DELEGATE_INVOKED) != 0)
                rval = TaskStatus.Running;
            else if ((sf & TASK_STATE_STARTED) != 0)
                rval = TaskStatus.WaitingToRun;
            else if ((sf & TASK_STATE_WAITINGFORACTIVATION) != 0)
                rval = TaskStatus.WaitingForActivation;
            else
                rval = TaskStatus.Created;

            return rval;
        }
    }

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
            return obj?.ObjectAddress.GetHashCode() ?? 0;
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
}
