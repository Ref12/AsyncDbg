using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncDbg.Core;
using AsyncDbg.InstanceWrappers;
using AsyncDbgCore.Core;
using AsyncDbgCore.New;

#nullable enable

namespace AsyncDbg.Causality
{
    public class CausalityNode
    {
        private readonly CausalityContext _context;
        protected TypesRegistry Types => _context.Registry;

        /// <summary>
        /// Object instance that backes current causality node.
        /// </summary>
        public ClrInstance ClrInstance { get; }

        public CausalityNode? CompletionSourceTaskNode { get; private set; }

        public bool IsRoot => Dependents.Count == 0;
        public bool IsLeaf => Dependencies.Count == 0;

        /// <summary>
        /// Some nodes in the async graph are auxiliary and should not be visible.
        /// For instance, TaskCompletionSource instance and the underlying Task instance
        /// are tightly coupled together and only one of them should be printed out.
        /// </summary>
        public virtual bool Visible => true;

        public readonly HashSet<CausalityNode> Dependencies = new HashSet<CausalityNode>();
        public readonly HashSet<CausalityNode> Dependents = new HashSet<CausalityNode>();

        public HashSet<CausalityNode> WaitingOn => Dependencies;
        public HashSet<CausalityNode> Unblocks => Dependents;

        private string CreateDisplayText()
        {
            var prefix = $"({Dependencies.Count}, {Dependents.Count}) ";
            var suffix = $"({Id})";

            var mainText = ToString();

            return $"{prefix} {mainText} {suffix}";
        }

        public IEnumerable<CausalityNode> EnumerateDependenciesAndSelfDepthFirst()
        {
            var enumeratedSet = new HashSet<CausalityNode>();

            // Using queue to get depth first left to right traversal. Stack would give right to left traversal.
            // TODO: explain why depth first is so important!
            var queue = new Stack<CausalityNode>();

            queue.Push(this);

            while (queue.Count > 0)
            {
                var next = queue.Pop();

                if (enumeratedSet.Contains(next))
                {
                    continue;
                }

                enumeratedSet.Add(next);
                yield return next;

                foreach (var n in next.Dependencies)
                {
                    queue.Push(n);
                }
            }
        }

        public string Id { get; }

        public NodeKind Kind { get; }

        public bool ProcessingContinuations { get; set; }

        public virtual bool IsComplete => false; // Derive types should override the result.

        protected virtual string DisplayStatus
        {
            get
            {
                if (ProcessingContinuations)
                {
                    return nameof(ProcessingContinuations);
                }

                return Kind.ToString();
            }
        }

        protected CausalityNode(CausalityContext context, ClrInstance clrInstance, NodeKind kind)
        {
            _context = context;
            ClrInstance = clrInstance;

            Id = clrInstance.ValueOrDefault?.ToString() ?? string.Empty;
            Kind = kind;
        }

        public static CausalityNode Create(CausalityContext context, ClrInstance clrInstance, NodeKind kind)
        {
            return kind switch
            {
                NodeKind.Task => new TaskNode(context, clrInstance),
                NodeKind.TaskCompletionSource => new TaskCompletionSourceNode(context, clrInstance),
                NodeKind.AsyncStateMachine => new AsyncStateMachineNode(context, clrInstance),
                NodeKind.AwaitTaskContinuation => new AwaitTaskContinuationNode(context, clrInstance),
                NodeKind.Thread => new ThreadNode(context, clrInstance),
                NodeKind.ManualResetEventSlim => new ManualResetEventSlimNode(context, clrInstance),
                _ => new CausalityNode(context, clrInstance, kind),
            };
        }

        public void Link()
        {
            if (this is AwaitTaskContinuationNode)
            {
                ProcessUnblockedInstance(ClrInstance);
            }
            else if (Kind == NodeKind.SemaphoreSlim)
            {
                var asyncHead = ClrInstance["m_asyncHead"].Instance;
                while (asyncHead.IsNotNull())
                {
                    AddDependent(asyncHead);

                    asyncHead = asyncHead["Next"].Instance;
                }
            }
            else if (this is ThreadNode threadNode)
            {
                foreach (var stackObject in threadNode.ClrThread.EnumerateStackObjects())
                {
                    var so = stackObject;

                    // Handle the state machine from the stack.
                    if (_context.Registry.IsAsyncStateMachine(so.Type))
                    {
                        // Thread could have a state machine on the stack because it was responsible for running a task, but now it yielded the control away.
                        var clrInstance = ClrInstance.CreateInstance(_context.Heap, so.Object, so.Type);
                        var asyncStateMachineInstance = new AsyncStateMachineInstance(clrInstance, _context.Registry);

                        if (asyncStateMachineInstance.Continuation != null && _context.TryGetNodeFor(asyncStateMachineInstance.Continuation, out var dependentNode) && !dependentNode.IsComplete)
                        {
                            // This feels very hacky but we need to separate the case when this thread is related to a state machine and when it's not.
                            if (threadNode.HasAsyncStateMachineMoveNextCall(so))
                            {
                                // This check makes sure that this thread indeed is trying to move the state machine forward.
                                AddEdge(dependency: this, dependent: dependentNode);
                            }
                        }
                    }
                    else if (_context.Registry.IsTask(so.Type))
                    {
                        if (threadNode.StackTrace.Count != 0)
                        {
                            var instance = ClrInstance.CreateInstance(_context.Heap, so.Object, so.Type);
                            var taskInstance = new TaskInstance(instance);
                            if (taskInstance.Status == TaskStatus.Running)
                            {
                                if (_context.TryGetNodeFor(instance, out var dependentNode))
                                {
                                    AddEdge(dependency: this, dependent: dependentNode);
                                }
                            }
                        }
                    }

                    if (_context.TryGetNodeAt(stackObject.Object, out var node))
                    {
                        switch (node.Kind)
                        {
                            case NodeKind.ManualResetEventSlim:
                            case NodeKind.ManualResetEvent:
                                AddEdge(dependency: node, dependent: this);
                                break;
                            case NodeKind.TaskCompletionSource:
                                if (threadNode.InsideTrySetResultMethodCall(so))
                                {
                                    // TaskCompletion source is waiting for this thread to complete the TrySetResult
                                    node.ProcessingContinuations = true;
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

            if (this is AsyncStateMachineNode asyncStateMachine && asyncStateMachine.StateMachineState >= 0)
            {
                // Need to process continuations only when the state machine awaits using task awaiter.

                // TODO: should we look for semaphores for other cases as well?
                FindSemaphores(ClrInstance);

                var awaitedTask = asyncStateMachine.AwaitedTask;
                if (awaitedTask != null)
                {
                    AddDependency(awaitedTask);
                }

                ProcessUnblockedInstance(ClrInstance);
            }
            else if (Kind == NodeKind.TaskCompletionSource)
            {
                ProcessUnblockedInstance(ClrInstance);
            }
            else if (this is TaskNode taskNode)
            {
                if (taskNode.TaskKind == TaskKind.WhenAll)
                {
                    foreach (var item in taskNode.WhenAllContinuations)
                    {
                        AddDependency(item);
                    }
                }

                ProcessUnblockedInstance(taskNode.ContinuationObject);
            }
        }

        private bool TryAddEdge(ClrInstance? continuation, bool asDependent = true)
        {
            if (continuation != null && _context.TryGetNodeFor(continuation, out var dependentNode))
            {
                AddEdge(dependency: this, dependent: dependentNode);
                return true;
            }

            return false;
        }

        private void ProcessUnblockedInstance(ClrInstance? nextContinuation)
        {
            var isCurrentNode = nextContinuation == ClrInstance;
            var nextIsCurrentNode = isCurrentNode;
            while (nextContinuation != null)
            {
                var continuation = nextContinuation;
                isCurrentNode = nextIsCurrentNode;
                nextIsCurrentNode = false;

                nextContinuation = null;

                if (!continuation.IsNull)
                {

                    if (!isCurrentNode && _context.TryGetNodeFor(continuation, out var dependentNode))
                    {
                        AddEdge(dependency: this, dependent: dependentNode);
                    }
                    else if (Kind == NodeKind.AsyncStateMachine)
                    {
                        if (!isCurrentNode)
                        {
                            // Only link to the task created by the async state machine. After that, no further continuations to process
                            return;
                        }

                        var stateMachine = continuation;

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
                            else if (_context.Registry.IsTask(asyncMethodBuild.Type))
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
                    }
                    else if (continuation.IsOfType(typeof(Action), _context))
                    {
                        var actionTarget = continuation["_target"].Instance;
                        if (actionTarget.IsOfType(_context.ContinuationWrapperType))
                        {
                            // Do we need to look at the m_innerTask field as well here?
                            nextContinuation = actionTarget["m_continuation"].Instance;
                            continue;
                        }

                        // m_stateMachine field is defined in AsyncMethodBuilderCore and in MoveNextRunner.
                        var stateMachine = actionTarget.TryGetFieldValue("m_stateMachine")?.Instance;
                        if (stateMachine.IsNull())
                        {
                            continue;
                        }

                        //TargetInstance = stateMachine;


                    }
                    else if (continuation.IsOfType(_context.StandardTaskContinuationType) || _context.TaskCompletionSourceIndex.ContainsType(continuation.Type))
                    {
                        nextContinuation = continuation["m_task"].Instance;
                    }
                    else if (continuation.IsCompletedTaskContinuation(_context))
                    {
                        // Continuation is a special sentinel instance that indicates that the task is completed.
                        break;
                    }
                    // Need to compare by name since GetTypeByName does not work for the generic type during initialization
                    else if (continuation.Type?.Name == "System.Collections.Generic.List<System.Object>")
                    {
                        var size = (int)continuation["_size"].Instance.ValueOrDefault;
                        var items = continuation["_items"].Instance.Items;
                        for (var i = 0; i < size; i++)
                        {
                            var continuationItem = items[i];
                            ProcessUnblockedInstance(continuationItem);
                        }
                    }
                    else if (_context.AwaitTaskContinuationIndex.ContainsType(continuation.Type))
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

        public class AsyncStateMachineInstance
        {
            private readonly ClrInstance _instance;
            public AsyncStateMachineInstance(ClrInstance instance, TypesRegistry registry)
            {
                _instance = instance;

                Continuation = TryGetAsyncBuildersContinuation(instance, registry);
            }

            public ClrInstance? Continuation { get; }
        }

        private static ClrInstance? TryGetAsyncBuildersContinuation(ClrInstance asyncBuilderInstance, TypesRegistry registry)
        {
            if (asyncBuilderInstance.TryGetFieldValue("<>t__builder", out var asyncMethodBuilderField))
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
                    return asyncMethodBuilderTaskField.Instance;
                }
                //else if (asyncMethodBuild.Type.IsOfType(typeof(Task)))
                else if (registry.IsTask(asyncMethodBuild.Type))
                {
                    return asyncMethodBuild;
                }
            }

            return null;
        }

        private void FindSemaphores(ClrInstance targetInstance)
        {
            var registry = _context.Registry;

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

        protected void AddDependency(ClrInstance dependency)
        {
            AddEdge(dependency: _context.GetNode(dependency), dependent: this);
        }

        protected void AddDependent(ClrInstance dependent)
        {
            AddEdge(dependency: this, dependent: _context.GetNode(dependent));
        }

        protected virtual void AddEdge(CausalityNode dependency, CausalityNode dependent)
        {
            if (dependency == null || dependency.ClrInstance?.IsNull == true || dependent == null || dependency.ClrInstance?.IsNull == true)
            {
                // Can't add edge to nothing
                return;
            }

            if (dependency == dependent)
            {
                // Avoiding self-references.
                return;
            }

            dependency.Dependents.Add(dependent);
            dependent.Dependencies.Add(dependency);
        }

        /// <inheritdoc />
        public sealed override string ToString()
        {
            return ToStringCore();
        }

        protected virtual string ToStringCore()
        {
            var result = $"{InsAndOuts()} [{DisplayStatus.ToString()}] {ClrInstance?.ToString(Types) ?? ""}";

            return result;
        }

        protected static string InsAndOuts(int dependencies, int dependents)
        {
            var up = '\x2191';
            var down = '\x2193';
            return $"({up}:{dependencies}, {down}:{dependents})";
        }

        protected string InsAndOuts() => InsAndOuts(Dependencies.Count, Dependents.Count);
    }
}
