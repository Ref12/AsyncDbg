﻿using System;
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

        public CausalityNode? CompletionSourceTaskNode { get; private set; }
        public ThreadInstance? Thread { get; private set; }
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
                        return Dependencies.All(d => d.IsComplete) && Thread?.StackTraceLength == 0;
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

        private TaskStatus Status => IsTask ? new TaskInstance(TaskInstance).Status : 0;

        public CausalityNode(CausalityContext context, ClrInstance task, NodeKind kind)
        {
            Context = context;
            TaskInstance = task;
            Id = task.ValueOrDefault?.ToString() ?? string.Empty;
            Kind = kind;
        }

        public void Link()
        {
            if (Kind == NodeKind.AwaitTaskContinuation)
            {
                ProcessContinuation(TaskInstance, isCurrentNode: true);
            }
            else if (Kind == NodeKind.SemaphoreSlim)
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
                    Thread = new ThreadInstance(clrThread, Context.Registry);

                    foreach (var stackObject in clrThread.EnumerateStackObjects())
                    {
                        var so = stackObject;

                        // Handle the state machine from the stack.
                        if (Context.Registry.IsAsyncStateMachine(so.Type))
                        {
                            // Thread could have a state machine on the stack because it was responsible for running a task, but now it yielded the control away.
                            var clrInstance = ClrInstance.CreateInstance(Context.Heap, so.Object, so.Type);
                            var asyncStateMachineInstance = new AsyncStateMachineInstance(clrInstance, Context.Registry);

                            if (asyncStateMachineInstance.Continuation != null && Context.TryGetNodeFor(asyncStateMachineInstance.Continuation, out var dependentNode) && !dependentNode.IsComplete)
                            {
                                // This feels very hacky but we need to separate the case when this thread is related to a state machine and when it's not.
                                if (Thread.HasAsyncStateMachineMoveNextCall(so))
                                {
                                    // This check makes sure that this thread indeed is trying to move the state machine forward.
                                    AddEdge(dependency: this, dependent: dependentNode);
                                }
                            }
                        }
                        else if (Context.Registry.IsTask(so.Type))
                        {
                            if (clrThread.StackTrace.Count != 0)
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
                                    if (Thread.InsideTrySetResultMethodCall(so))
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
        }

        public void AddDependent(ClrInstance dependent)
        {
            AddEdge(dependency: this, dependent: Context.GetNode(dependent));
        }

        public void AddEdge(CausalityNode dependency, CausalityNode dependent)
        {
            if (dependency == dependent)
            {
                // Avoiding self-references.
                return;
            }

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