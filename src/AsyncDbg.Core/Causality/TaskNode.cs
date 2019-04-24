using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AsyncCausalityDebuggerNew;
using AsyncDbg.Core;
using AsyncDbgCore.Core;
using AsyncDbgCore.New;

#nullable enable

namespace AsyncDbg.Causality
{
    public class TaskNode : CausalityNode
    {
        /// <summary>
        /// Optional synchronization context attached to a current task node.
        /// The field is not null if the task represents an async operation that runs within a sync context.
        /// </summary>
        private ClrInstance? _syncContext;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private readonly TaskInstance _taskInstance;

        // Not null if the task originates from TaskCompletionSource<T>
        private CausalityNode? _taskCompletionSource;

        // Not null if the task originates from async state machine.
        private CausalityNode? _asyncStateMachine;

        public TaskNode(CausalityContext context, ClrInstance task)
            : base(context, task, NodeKind.Task)
        {
            _taskInstance = new TaskInstance(task);
        }

        /// <inheritdoc />
        protected override string DisplayStatus => _taskInstance.Status.ToString();

        public TaskKind TaskKind
        {
            get
            {
                if (_taskCompletionSource != null)
                    return TaskKind.FromTaskCompletionSource;

                if (_asyncStateMachine != null)
                    return TaskKind.AsyncTask;

                if (Types.IsTaskWhenAll(ClrInstance))
                    return TaskKind.WhenAll;

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

        public ClrInstance ContinuationObject => ClrInstance["m_continuationObject"].Instance;

        ///// <inheritdoc />
        //public override bool Visible => TaskKind <= TaskKind.VisibleTaskKind;

        /// <inheritdoc />
        public override bool IsComplete => _taskInstance.IsCompleted && !ProcessingContinuations;

        public void SetTaskCompletionSource(CausalityNode tcs)
        {
            Contract.Assert(TaskKind == TaskKind.Unknown, $"Can not change task origin because it was already set to '{TaskKind}'");

            _taskCompletionSource = tcs;
        }

        public void SetAsyncStateMachine(CausalityNode asyncStateMachine)
        {
            Contract.Assert(TaskKind == TaskKind.Unknown, $"Can not change task origin because it was already set to '{TaskKind}'");

            _asyncStateMachine = asyncStateMachine;
        }

        protected override void AddEdge(CausalityNode dependency, CausalityNode dependent)
        {
            if (dependency == this && dependent is AwaitTaskContinuationNode await && await.SyncContext != null)
            {
                // Saving it and not adding as an edge to simplify visualization.
                _syncContext = await.SyncContext;
            }
            else
            {
                base.AddEdge(dependency, dependent);
            }
        }

        /// <inhertidoc />
        protected override string ToStringCore()
        {
            var result = TaskKind switch
            {
                TaskKind.FromTaskCompletionSource => Contract.AssertNotNull(_taskCompletionSource).ToString(),
                TaskKind.AsyncTask => Contract.AssertNotNull(_asyncStateMachine).ToString(),
                TaskKind.WhenAll => $"{InsAndOuts()} {ClrInstance.ToString(Types)}",
                TaskKind.TaskRun => $"{InsAndOuts()} Task.Run ({ClrInstance.ObjectAddress})",
                _ => base.ToStringCore(),
            };

            if (_syncContext != null)
            {
                result += $"{Environment.NewLine}(with sync context: {_syncContext.Type?.TypeToString(Types)})";
            }

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
        TaskWrapper,
        AsyncTask,
        VisibleTaskKind = WhenAll
    }
}
