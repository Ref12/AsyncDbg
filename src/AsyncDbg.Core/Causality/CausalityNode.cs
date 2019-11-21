using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncDbg.Core;
using AsyncDbg.InstanceWrappers;

#nullable enable

namespace AsyncDbg.Causality
{
    public class CausalityNode
    {
        protected readonly CausalityContext Context;

        protected CausalityNode? TryGetNodeFor(ClrInstance? instance)
        {
            if (instance == null)
            {
                return null;
            }

            if (Context.TryGetNodeFor(instance, out var result))
            {
                return result;
            }

            return null;
        }

        protected TypesRegistry Types => Context.Registry;

        /// <summary>
        /// A CLR object instance that backs the current causality node.
        /// </summary>
        public ClrInstance ClrInstance { get; }

        public CausalityNode? CompletionSourceTaskNode { get; private set; }

        public Lazy<Guid> Key { get; }

        public bool IsRoot
        {
            get
            {
                if (Dependents.Count == 0) return true;

                if (this is ThreadNode) return true;

                if (this is AsyncStateMachineNode stateMachine && stateMachine.Dependents.Count != 0) return true;
                //if (Dependents.Count == 0) return true;
                return false;
            }
        }

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

        protected CausalityNode(CausalityContext context, ClrInstance clrInstance, NodeKind kind)
        {
            Context = context;
            ClrInstance = clrInstance;

            Id = clrInstance.ValueOrDefault?.ToString() ?? string.Empty;
            Kind = kind;
            Key = new Lazy<Guid>(() => ComputeKey());
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
                NodeKind.SemaphoreSlim => new SemaphoreSlimNode(context, clrInstance),
                _ => new CausalityNode(context, clrInstance, kind),
            };
        }

        public string CreateDisplayText()
        {
            var prefix = $"({Dependencies.Count}, {Dependents.Count}) ";
            var suffix = $"({Id})";

            var mainText = ToString();

            return $"{prefix} {mainText} {suffix}";
        }

        public IEnumerable<CausalityNode> EnumerateDependenciesAndSelfDepthFirst()
        {
            return EnumerateNeighborsAndSelfDepthFirst(n => n.Dependencies);
        }

        public IEnumerable<CausalityNode> EnumerateNeighborsAndSelfDepthFirst()
        {
            return EnumerateNeighborsAndSelfDepthFirst(n => n.Dependencies.Concat(n.Dependents));
        }

        public Guid ComputeKey()
        {
            return Guid.NewGuid();
            //Murmur3 murmur = new Murmur3();
            //var bytes = Encoding.UTF8.GetBytes(ClrInstance.AddressRegex.Replace(ToString(), ""));

            //var dependencies = EnumerateDependenciesAndSelfDepthFirst();
            //var hash = dependencies.Select(t => Encoding.UTF8.GetBytes(ClrInstance.AddressRegex.Replace(t.ToString(), "")));
            //// Hash dependencies nodes and normalized display text for self
            //return murmur.ComputeHash(hash.Select(ba => new ArraySegment<byte>(ba))).AsGuid();
        }

        public IEnumerable<CausalityNode> EnumerateNeighborsAndSelfDepthFirst(Func<CausalityNode, IEnumerable<CausalityNode>> getNeighbors)
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

                foreach (var n in getNeighbors(next))
                {
                    queue.Push(n);
                }
            }
        }

        public string Id { get; }

        public NodeKind Kind { get; }

        public virtual bool IsComplete => false; // Derive types should override the result.

        protected virtual string DisplayStatus
        {
            get
            {
                return Kind.ToString();
            }
        }

        public virtual void Link()
        {
            if (this is ThreadNode threadNode)
            {
                CausalityNode? lastMoveNext = null;

                foreach (var stackObject in threadNode.EnumerateStackObjects())
                {
                    var so = stackObject;

                    if (Context.Registry.IsTask(so.Type))
                    {
                        if (threadNode.StackTrace.Count != 0)
                        {
                            var instance = ClrInstance.CreateInstance(Context.Heap, so.Object, so.Type);
                            var taskInstance = new TaskInstance(instance);
                            if (taskInstance.Status == TaskStatus.Running)
                            {
                                if (Context.TryGetNodeFor(instance, out var dependentNode))
                                {
                                    AddEdge(dependency: this, dependent: dependentNode);
                                }
                            }
                        }
                    }

                    if (Context.TryGetNodeAt(stackObject.Object, out var node))
                    {
                        switch (node.Kind)
                        {
                            case NodeKind.ManualResetEventSlim:
                            case NodeKind.ManualResetEvent:
                                AddEdge(dependency: node, dependent: this);
                                break;
                            case NodeKind.TaskCompletionSource:
                                AddEdge(dependency: this, dependent: node);
                                break;
                            case NodeKind.Thread:
                            //case NodeKind.AsyncStateMachine:
                            default:
                                break;
                        }
                    }
                }

                //if (asyncStateMachineOnTheStack != null)
                //{
                //    asyncStateMachineOnTheStack.RunningMoveNext(threadNode);
                //}

                if (lastMoveNext != null)
                {
                    //AddEdge(dependency: this, dependent: lastMoveNext);
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
        }

        private bool TryAddEdge(ClrInstance? continuation, bool asDependent = true)
        {
            if (continuation != null && Context.TryGetNodeFor(continuation, out var dependentNode))
            {
                AddEdge(dependency: this, dependent: dependentNode);
                return true;
            }

            return false;
        }

        protected void ProcessUnblockedInstance(ClrInstance? nextContinuation)
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
                    if (!isCurrentNode && Context.TryGetNodeFor(continuation, out var dependentNode))
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

                        nextContinuation = AsyncStateMachineNode.GetResultingTask(continuation);

                        //var stateMachine = continuation;

                        //if (stateMachine.TryGetFieldValue("<>t__builder", out var asyncMethodBuilderField))
                        //{
                        //    var asyncMethodBuild = asyncMethodBuilderField.Instance;
                        //    // Looking for a builder instance for async state machine generated for async Task and async ValueTask methods.
                        //    if (asyncMethodBuild.TryGetFieldValue("m_builder", out var innerAsyncMethodBuilderField) ||
                        //        asyncMethodBuild.TryGetFieldValue("_methodBuilder", out innerAsyncMethodBuilderField))
                        //    {
                        //        asyncMethodBuild = innerAsyncMethodBuilderField.Instance;
                        //    }

                        //    if (asyncMethodBuild.TryGetFieldValue("m_task", out var asyncMethodBuilderTaskField))
                        //    {
                        //        nextContinuation = asyncMethodBuilderTaskField.Instance;
                        //    }
                        //    //else if (asyncMethodBuild.Type.IsOfType(typeof(Task)))
                        //    else if (_context.Registry.IsTask(asyncMethodBuild.Type))
                        //    {
                        //        nextContinuation = asyncMethodBuild;
                        //    }
                        //    else
                        //    {
                        //    }

                        // This is an await continuation and the next continuation may be already finished.
                        // In this case we mark the current instance as completed as well.
                        //if (Context.)
                        //}
                    }
                    else if (continuation.IsOfType(typeof(Action), Context))
                    {
                        var actionTarget = continuation["_target"].Instance;
                        if (actionTarget.IsOfType(Context.ContinuationWrapperType))
                        {
                            // Do we need to look at the m_innerTask field as well here?
                            nextContinuation = actionTarget["m_continuation"].Instance;
                            continue;
                        }

                        // If the action points to a closure, it is possible that the closure
                        // is responsible for setting the result of a task completion source.
                        // There is no simple way to detect whether this is the case or not, so we will add the "edge" unconditionally.
                        if (actionTarget.Type.IsClosure())
                        {
                            foreach (var field in actionTarget.Fields)
                            {
                                if (Context.TaskCompletionSourceIndex.ContainsType(field.Instance.Type))
                                //if (field.Instance.IsOfType(_context.TaskCompletionSourceIndex))
                                {
                                    if (Context.TryGetNodeFor(field.Instance, out var dependentNode2))
                                    {
                                        dependentNode2.AddEdge(dependency: dependentNode2, dependent: this);
                                        //AddEdge(dependentNode2, this);
                                    }

                                    //dependentNode.AddEdge(dependency: dependentNode, dependent);
                                    //AddDependency(field.Instance["m_task"].Instance);
                                    //AddDependent(field.Instance);
                                }
                            }

                            continue;
                        }

                        // m_stateMachine field is defined in AsyncMethodBuilderCore and in MoveNextRunner.
                        var stateMachine = actionTarget.TryGetFieldValue("m_stateMachine")?.Instance;
                        if (stateMachine.IsNull())
                        {
                            continue;
                        }

                        if (Context.TryGetNodeFor(stateMachine, out var stateMachineNode))
                        {
                            AddEdge(dependency: this, dependent: stateMachineNode);

                            //TargetInstance = stateMachine;
                        }
                    }
                    else if (continuation.IsOfType(Context.StandardTaskContinuationType))
                    {
                        nextContinuation = continuation["m_task"].Instance;
                    }
                    else if (Context.TaskCompletionSourceIndex.ContainsType(continuation.Type))
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
                        for (var i = 0; i < size; i++)
                        {
                            var continuationItem = items[i];
                            ProcessUnblockedInstance(continuationItem);
                        }
                    }
                    else if (Context.AwaitTaskContinuationIndex.ContainsType(continuation.Type) || continuation.IsOfType(Context.Registry.TaskIndex))
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

            // CoreCLR case
            if (asyncBuilderInstance.TryGetFieldValue("m_continuationObject", out var continuation))
            {
                return continuation.Instance;
            }

            return null;
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

        protected void AddDependency(ClrInstance dependency)
        {
            AddEdge(dependency: Context.GetNode(dependency), dependent: this);
        }

        protected void AddDependent(ClrInstance dependent)
        {
            AddEdge(dependency: this, dependent: Context.GetNode(dependent));
        }

        protected void AddDependent(CausalityNode dependent)
        {
            AddEdge(dependency: this, dependent: dependent);
        }

        protected virtual void AddEdge(CausalityNode? dependency, CausalityNode? dependent)
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

        //protected virtual string 
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
