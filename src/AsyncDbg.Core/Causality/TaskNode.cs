using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AsyncDbg.Core;
using AsyncDbg.InstanceWrappers;

#nullable enable

namespace AsyncDbg.Causality
{
    public class TaskNode : CausalityNode
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private readonly TaskInstance _taskInstance;

        // Not null if the task originates from TaskCompletionSource<T>
        private TaskCompletionSourceNode? _taskCompletionSource;

        private SemaphoreSlimNode? _semaphoreSlimNode;

        // Not null if the task originates from async state machine.
        private AsyncStateMachineNode? _asyncStateMachine;

        public TaskNode(CausalityContext context, ClrInstance task)
            : base(context, task, NodeKind.Task)
        {
            _taskInstance = new TaskInstance(task);
        }

        /// <nodoc />
        public TaskStatus Status => _taskInstance.Status;

        /// <inheritdoc />
        protected override string DisplayStatus => $"Status={_taskInstance.Status}, Kind={TaskKind}";

        public TaskKind TaskKind
        {
            get
            {
                if (_taskCompletionSource != null)
                    return TaskKind.FromTaskCompletionSource;

                if (_asyncStateMachine != null)
                    return TaskKind.AsyncMethodTask;

                if (Types.IsTaskWhenAll(ClrInstance))
                    return TaskKind.WhenAll;

                if (_semaphoreSlimNode != null)
                    return TaskKind.SemaphoreSlimTaskNode;

                if (Types.IsUnwrapPromise(ClrInstance))
                {
                    // There are two unwrap promises:
                    // One is used in Task.Unwrap() extension method
                    // and another one is returned by Task.Run method.
                    // Here is the comment from Task.cs
                    // "Should we check for OperationCanceledExceptions on the outer task and interpret them as proxy cancellation?"
                    // Unwrap() sets this to false, Run() sets it to true.
                    // private readonly bool _lookForOce;
                    return (bool)ClrInstance["_lookForOce"].Instance.Value ? TaskKind.TaskRun : TaskKind.UnwrapPromise;
                }

                if (Status == TaskStatus.Running)
                {
                    return TaskKind.TaskRun;
                }

                return TaskKind.Unknown;
            }
        }

        public List<ClrInstance> WhenAllContinuations
        {
            get
            {
                Contract.Requires(TaskKind == TaskKind.WhenAll, "Only applicable to WhenAll task kind.");
                return ClrInstance["m_tasks"].Instance.Items.Where(i => i.IsNotNull()).ToList();
            }
        }

        public ClrInstance ContinuationObject => GetContinuationObject();

        private ClrInstance GetContinuationObject()
        {
            var continuationObject = ClrInstance["m_continuationObject"].Instance;
            if (continuationObject.IsNotNull())
            {
                return continuationObject;
            }

            return ClrInstance["m_action"].Instance;
        }

        ///// <inheritdoc />
        //public override bool Visible => TaskKind <= TaskKind.VisibleTaskKind;

        /// <inheritdoc />
        public override bool IsComplete => _taskInstance.IsCompleted;

        public void SetTaskCompletionSource(TaskCompletionSourceNode tcs)
        {
            Contract.Assert(TaskKind == TaskKind.Unknown, $"Can not change task origin because it was already set to '{TaskKind}'");

            _taskCompletionSource = tcs;
        }

        public void SetAsyncStateMachine(AsyncStateMachineNode asyncStateMachine)
        {
            Contract.Assert(TaskKind == TaskKind.Unknown || TaskKind == TaskKind.AsyncMethodTask, $"Can not change task origin because it was already set to '{TaskKind}'");

            _asyncStateMachine = asyncStateMachine;
        }

        public void SetSemaphoreSlim(SemaphoreSlimNode semaphoreSlimNode)
        {
            Contract.Assert(TaskKind == TaskKind.Unknown, $"Can not change task origin because it was already set to '{TaskKind}'");
            _semaphoreSlimNode = semaphoreSlimNode;
        }

        /// <inheritdoc />
        public override void Link()
        {
            var taskNode = this;

            if (taskNode.TaskKind == TaskKind.WhenAll)
            {
                foreach (var item in taskNode.WhenAllContinuations)
                {
                    AddDependency(item);
                }
            }

            var parent = taskNode.ClrInstance.TryGetFieldValue("m_parent")?.Instance;
            if (parent.IsNotNull())
            {
                // m_parent is not null for Parallel.ForEach, for instance.
                AddDependent(parent);
            }

            // The continuation instance is a special (causality) node, then just adding the edge
            // without extra processing.

            if (Context.TryGetNodeFor(taskNode.ContinuationObject, out var dependentNode))
            {
                AddDependent(dependentNode);
            }
            else
            {
                var continuations = ContinuationResolver.TryResolveContinuations(taskNode.ContinuationObject, Context);
                foreach (var c in continuations)
                {
                    if (Context.TryGetNodeFor(c, out dependentNode))
                    {
                        AddDependent(dependentNode);
                    }
                }
            }
            
        }

        /// <inhertidoc />
        protected override string ToStringCore()
        {
            var result = TaskKind switch
            {
                TaskKind.FromTaskCompletionSource => Contract.AssertNotNull(_taskCompletionSource).ToString(),
                //TaskKind.AsyncMethodTask => Contract.AssertNotNull(_asyncStateMachine).ToString(),
                // var result = $"{InsAndOuts()} [{DisplayStatus.ToString()}] {ClrInstance?.ToString(Types) ?? ""}";
                //TaskKind.WhenAll => $"{InsAndOuts()} [{DisplayStatus.ToString()}] {ClrInstance.ToString(Types)}",
                TaskKind.TaskRun => $"{InsAndOuts()} Task.Run ({ClrInstance.ObjectAddress})",
                _ => base.ToStringCore(),
            };

            return result;
        }
    }

    /// <summary>
    /// Describes kind of a task.
    /// </summary>
    /// <remarks>
    /// Task type can be used for different purposes: it may be created by <see cref="TaskCompletionSource{TResult}"/>, or as part of async method, or represents a wrapper returned by <see cref="Task.Run(System.Func{Task})"/>.
    /// </remarks>
    public enum TaskKind
    {
        Unknown,
        UnwrapPromise,
        TaskRun,
        WhenAll,
        FromTaskCompletionSource,
        AsyncMethodTask,
        SemaphoreSlimTaskNode,
        VisibleTaskKind = WhenAll
    }
}
