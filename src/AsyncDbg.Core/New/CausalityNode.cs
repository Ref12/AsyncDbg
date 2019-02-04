using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncDbgCore.New;
using Microsoft.Diagnostics.Runtime;
#nullable enable

namespace AsyncCausalityDebuggerNew
{

    public class CausalityNode
    {
        public CausalityContext Context { get; }
        public ClrInstance TaskInstance { get; }

        public CausalityNode? CompletionSourceTaskNode;
        public ClrThread? Thread;
        public ClrInstance? TargetInstance { get; private set; }
        public readonly HashSet<CausalityNode> Dependencies = new HashSet<CausalityNode>();
        public readonly HashSet<CausalityNode> Dependents = new HashSet<CausalityNode>();

        public string Id { get; }

        private bool IsTask => Kind == NodeKind.Task;
        public NodeKind Kind { get; }

        private bool ProcessingContinuations { get; set; }

        public bool IsComplete
        {
            get
            {
                switch (Kind)
                {
                    case NodeKind.Task:
                        var taskInstance = new TaskInstance(TaskInstance);
                        return taskInstance.IsCompleted && !ProcessingContinuations;
                    case NodeKind.TaskCompletionSource:
                        return CompletionSourceTaskNode?.IsComplete == true;
                    case NodeKind.AwaitTaskContinuation:
                        if (TargetInstance != null && TargetInstance.TryGetFieldValue("<>1__state")?.Instance?.Value.Equals((object)-2) == true)
                        {
                            return true;
                        }

                        return false;
                    case NodeKind.Thread:
                        return Dependencies.All(d => d.IsComplete);
                    default:
                        return false;
                }
            }
        }

        private string DisplayStatus
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

        private TaskStatus Status => TaskInstanceHelpers.GetStatus(GetFlags());

        private int GetFlags()
        {
            return IsTask ? (int)(TaskInstance["m_stateFlags"].Instance.ValueOrDefault) : 0;
        }

        public CausalityNode(CausalityContext context, ClrInstance task, NodeKind kind)
        {
            Context = context;
            TaskInstance = task;
            Id = task.ValueOrDefault?.ToString() ?? string.Empty;
            Kind = kind;

            if (TaskInstance.ObjectAddress == 2557664314544L || TaskInstance.ObjectAddress == 2557664286008L)
            {

            }
        }

        public void Link()
        {
            if (TaskInstance.ObjectAddress == 2585904186960L) // FileSystemContentStoreInternal.ShutdownCore
            {

            }

            if (TaskInstance.ObjectAddress == 2557664314544L || TaskInstance.ObjectAddress == 2557664286008L)
            {

            }
            if (Kind == NodeKind.AwaitTaskContinuation)
            {
                ProcessContinuation(TaskInstance, isCurrentNode: true);
            }

            // Kind == kind
            if (Kind == NodeKind.SemaphoreSlim)
            {
                var asyncHead = TaskInstance["m_asyncHead"].Instance;
                while (asyncHead.IsNotNull())
                {
                    AddDependent(asyncHead);

                    asyncHead = asyncHead["Next"].Instance;
                }
            }
            else if (Kind == NodeKind.Thread)
            {
                var threadId = TaskInstance["m_ManagedThreadId"]?.Instance?.ValueOrDefault as int?;
                if (threadId != null && Context.TryGetThreadById(threadId.Value, out var clrThread))
                {
                    Thread = clrThread;
                    HashSet<ulong> tcsSetResultFrames = new HashSet<ulong>();
                    List<ulong> stackTraceAddresses = new List<ulong>();
                    //HashSet<CausalityNode> dependencies = new HashSet<CausalityNode>();
                    //foreach (var blockingObject in Thread.BlockingObjects ?? Enumerable.Empty<BlockingObject>())
                    //{
                    //    var obj = blockingObject.Object;
                    //    var dependency = Context.GetOrCreate(ClrInstance.CreateInstance(Context.Heap, obj), NodeKind.BlockingObject);
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
                        if (Context.Registry.IsTaskCompletionSource(stackFrame.Method?.Type) &&
                            stackFrame.Method?.Name == "TrySetResult")
                        {
                            tcsSetResultFrames.Add(stackFrame.StackPointer);
                        }
                    }

                    //var stackObjects = clrThread.EnumerateStackObjects().Select(so => new ClrInstance(Context.Heap, so.Object, Context.Heap.GetObjectType(so.Object))).ToList();
                    foreach (var stackObject in clrThread.EnumerateStackObjects())
                    {
                        var so = stackObject;

                        // Handle the state machine from the stack.
                        if (Context.Registry.IsAsyncStateMachine(so.Type))
                        {
                            var asyncStateMachineInstance = ClrInstance.CreateInstance(Context.Heap, so.Object, so.Type);
                            ProcessStateMachineContinuation(asyncStateMachineInstance);
                        }

                        if (Context.TryGetNodeAt(stackObject.Object, out var node))
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
                                    //case NodeKind.AsyncStateMachine:
                                    default:
                                        break;
                                }
                            }
                        }
                    }
                }
            }

            if (Kind == NodeKind.TaskCompletionSource)
            {
                ProcessContinuation(TaskInstance, isCurrentNode: true);
            }

            if (Kind == NodeKind.Task)
            {
                if (TaskInstance.IsTaskWhenAll(Context))
                {
                    foreach (var item in TaskInstance["m_tasks"].Instance.Items.Where(i => i.IsNotNull()))
                    {
                        AddDependency(item);
                    }
                }

                var nextContinuation = TaskInstance["m_continuationObject"].Instance;

                ProcessContinuation(nextContinuation);
            }
        }

        private ClrStackFrame? GetStackFrame(ClrRoot so, List<ulong> stackTraceAddresses, ClrThread clrThread)
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

        private void ProcessContinuation(ClrInstance? nextContinuation, bool isCurrentNode = false)
        {
            bool nextIsCurrentNode = isCurrentNode;
            while (nextContinuation != null)
            {
                var continuation = nextContinuation;
                isCurrentNode = nextIsCurrentNode;
                nextIsCurrentNode = false;

                nextContinuation = null;

                if (!continuation.IsNull)
                {
                    if (!isCurrentNode && Context.TryGetNodeFor(continuation, out var dependentNode))
                    {
                        AddEdge(dependency: this, dependent: dependentNode);
                    }
                    else if (continuation.IsOfType(typeof(System.Action), Context))
                    {
                        var actionTarget = continuation["_target"].Instance;
                        if (actionTarget.IsOfType(Context.ContinuationWrapperType))
                        {
                            // Do we need to look at the m_innerTask field as well here?
                            nextContinuation = actionTarget["m_continuation"].Instance;
                            continue;
                        }

                        // m_stateMachine field is defined in AsyncMethodBuilderCore and in MoveNextRunner.
                        var stateMachine = actionTarget["m_stateMachine"]?.Instance;
                        if (stateMachine.IsNull())
                        {
                            continue;
                        }

                        TargetInstance = stateMachine;
                        FindSemaphores(stateMachine);

                        if (stateMachine.TryGetFieldValue("<>t__builder", out var asyncMethodBuilderField))
                        {
                            var asyncMethodBuild = asyncMethodBuilderField.Instance;
                            // Looking for a builder instance for async state machine generated for async Task and async ValueTask methods.
                            if (asyncMethodBuild.TryGetFieldValue("m_builder", out var innerAsyncMethodBuilderField) ||
                                asyncMethodBuild.TryGetFieldValue("_methodBuilder", out innerAsyncMethodBuilderField))
                            {
                                asyncMethodBuild = innerAsyncMethodBuilderField.Instance;
                            }

                            if (asyncMethodBuild.TryGetFieldValue("m_task", out var asyncMethodBuilderTaskField))
                            {
                                nextContinuation = asyncMethodBuilderTaskField.Instance;
                            }
                            //else if (asyncMethodBuild.Type.IsOfType(typeof(Task)))
                            else if (Context.Registry.IsTask(asyncMethodBuild.Type))
                            {
                                nextContinuation = asyncMethodBuild;
                            }
                            else
                            {
                            }

                            // This is an await continuation and the next continuation may be already finished.
                            // In this case we mark the current instance as completed as well.
                            //if (Context.)
                        }
                        else
                        {
                        }
                    }
                    else if (continuation.IsOfType(Context.StandardTaskContinuationType) || Context.TaskCompletionSourceIndex.ContainsType(continuation.Type))
                    {
                        nextContinuation = continuation["m_task"].Instance;
                    }
                    else if (continuation.IsCompletedTaskContinuation(Context))
                    {
                        // Continuation is a special sentinel instance that indicates that the task is completed.
                        break;
                    }
                    // Need to compare by name since GetTypeByName does not work for the generic type during initialization
                    else if (continuation.Type?.Name == "System.Collections.Generic.List<System.Object>")
                    {
                        var size = (int)continuation["_size"].Instance.ValueOrDefault;
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

        private void ProcessStateMachineContinuation(ClrInstance? nextContinuation)
        {
            while (nextContinuation != null)
            {
                var continuation = nextContinuation;

                nextContinuation = null;

                if (!continuation.IsNull)
                {
                    if (Context.TryGetNodeFor(continuation, out var dependentNode))
                    {
                        AddEdge(dependency: this, dependent: dependentNode);
                    }
                    else if (continuation.TryGetFieldValue("<>t__builder", out var asyncMethodBuilderField))
                    {
                        var asyncMethodBuild = asyncMethodBuilderField.Instance;
                        // Looking for a builder instance for async state machine generated for async Task and async ValueTask methods.
                        if (asyncMethodBuild.TryGetFieldValue("m_builder", out var innerAsyncMethodBuilderField) ||
                            asyncMethodBuild.TryGetFieldValue("_methodBuilder", out innerAsyncMethodBuilderField))
                        {
                            asyncMethodBuild = innerAsyncMethodBuilderField.Instance;
                        }

                        if (asyncMethodBuild.TryGetFieldValue("m_task", out var asyncMethodBuilderTaskField))
                        {
                            nextContinuation = asyncMethodBuilderTaskField.Instance;
                        }
                        //else if (asyncMethodBuild.Type.IsOfType(typeof(Task)))
                        else if (Context.Registry.IsTask(asyncMethodBuild.Type))
                        {
                            nextContinuation = asyncMethodBuild;
                        }
                        else
                        {
                        }
                    }
                    else if (continuation.IsOfType(Context.StandardTaskContinuationType) || Context.TaskCompletionSourceIndex.ContainsType(continuation.Type))
                    {
                        nextContinuation = continuation["m_task"].Instance;
                    }
                    else if (continuation.IsCompletedTaskContinuation(Context))
                    {
                        // Continuation is a special sentinel instance that indicates that the task is completed.
                        break;
                    }
                    // Need to compare by name since GetTypeByName does not work for the generic type during initialization
                    else if (continuation.Type?.Name == "System.Collections.Generic.List<System.Object>")
                    {
                        var size = (int)continuation["_size"].Instance.ValueOrDefault;
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
            var registry = Context.Registry;
            
            foreach (var field in targetInstance.Fields)
            {
                if (field == null)
                {
                    continue;
                }

                if (registry.IsSemaphoreWrapper(field.Field.Type))
                {
                    var semaphore = field.Instance["_semaphore"].Instance;
                    if (semaphore.IsNotNull())
                    {
                        AddDependent(semaphore);
                    }
                }
            }
        }

        public void AddDependency(ClrInstance dependency)
        {
            AddEdge(dependency: Context.GetNode(dependency), dependent: this);
            //if (Context.TryGetNodeFor(dependency, out var node))
            //{
            //    AddEdge(dependency: node, dependent: this);
            //}
            //else
            //{
            //    Console.WriteLine($"Cannot find causality node for dependency '{dependency}'");
            //}
        }

        public void AddDependent(ClrInstance dependent)
        {
            AddEdge(dependency: this, dependent: Context.GetNode(dependent));
            //if (Context.TryGetNodeFor(dependent, out var node))
            //{
            //    AddEdge(dependency: this, dependent: node);
            //}
            //else
            //{
            //    Console.WriteLine($"Cannot find causality node for dependent '{dependent}'");
            //}
        }

        public void AddEdge(CausalityNode dependency, CausalityNode dependent)
        {
            if (dependency.Kind == NodeKind.TaskCompletionSource && dependent.Kind == NodeKind.Task)
            {
                dependency.CompletionSourceTaskNode = dependent;
                dependent.ProcessingContinuations = dependency.ProcessingContinuations;
            }

            dependency.Dependents.Add(dependent);
            dependent.Dependencies.Add(dependency);
        }

        public override string ToString()
        {
            var result = $"({Dependencies.Count}, {Dependents.Count}) [{(IsTask ? DisplayStatus.ToString() : Kind.ToString())}] {TaskInstance?.ToString() ?? ""}";
            if (Thread != null && (Dependencies.Count != 0 || Dependents.Count != 0))
            {
                result += Environment.NewLine + string.Join(Environment.NewLine, Thread.StackTrace);
            }

            if (TargetInstance != null)
            {
                result += Environment.NewLine + TargetInstance.ToString();
            }

            return result;
        }
    }
}
