using AsyncDbg.Core;
using System;
using System.Collections;

#nullable enable

namespace AsyncDbg.Causality
{
    public enum StateMachineStatus
    {
        Created,
        RunningMoveNext,
        Awaiting,
        Completed,
    }

    /// <summary>
    /// Represents an async state machine.
    /// </summary>
    /// <remarks>
    /// An async state machine is an instance of a generated state machine class that has the following important
    /// aspects/properties:
    /// Status: Created|Awaiting|RunningMoveNext|Completed
    /// The transition is happening the following way:
    /// Created (init state) -> RunningMoveNext (A thread runs MoveNext method) -> Awaiting (awaiting on a non completed task) -> Completed
    ///                         (Thread field is not null)                         (CurrentAwaiter is not null)
    /// </remarks>
    public class AsyncStateMachineNode : CausalityNode
    {
        public const int InitialState = -1;
        public const int CompletedState = -2;

        // A thread that runs MoveNext method for the current instance.
        private ThreadNode? _moveNextRunnerThread;
        private ClrInstance? _syncContext;

        // A task that the current state machine instance awaits on
        private readonly ClrInstance? _awaitedTask;

        private readonly StateMachineStatus _status;

        // Resulting task instance is null for async void methods (i.e. _resultingTask != null, but _resultingTask.IsNull() == true).
        private readonly ClrInstance _resultingTask;

        /// <nodoc />
        public AsyncStateMachineNode(CausalityContext context, ClrInstance clrInstance)
            : base(context, clrInstance, NodeKind.AsyncStateMachine)
        {
            _status = StateMachineState switch
            {
                InitialState => StateMachineStatus.Created,
                CompletedState => StateMachineStatus.Completed,
                _ => StateMachineStatus.Awaiting,
            };

            if (_status == StateMachineStatus.Awaiting)
            {
                _awaitedTask = GetAwaitedTask();
                Contract.AssertNotNull(_awaitedTask, $"For state machine state '{StateMachineState}' an awaited task should be available");
            }

            _resultingTask = GetResultingTask(clrInstance, Context.Registry);
        }

        public void SetSyncContext(ClrInstance syncContext)
        {
            _syncContext = syncContext;
        }

        /// <inheritdoc />
        public override bool IsComplete => Status == StateMachineStatus.Completed;

        /// <inheritdoc />
        public override bool Visible => true;

        public void RunningMoveNext(ThreadNode threadNode)
        {
            // MoveNext can be executed by a thread when the status is either Created or Awaiting.
            Contract.Assert(StateMachineState != CompletedState, "State machine can not be completed");
            _moveNextRunnerThread = threadNode;
        }

        public ThreadNode? MoveNextRunnerThread => _moveNextRunnerThread;

        /// <summary>
        /// Returns a task that the current state machines awaits on.
        /// </summary>
        public ClrInstance? AwaitedTask => _awaitedTask;

        public TaskNode? AwaitedTaskNode => (TaskNode?)TryGetNodeFor(AwaitedTask);

        public ClrInstance ResultingTask => _resultingTask;

        // The result actually can be null, when not all the nodes are registered.
        // For instance, accessing this property from constructor may return null.
        // Maybe assert that Context.Linked is true or something similar?
        public TaskNode ResultingTaskNode => (TaskNode)TryGetNodeFor(_resultingTask)!;

        public StateMachineStatus Status =>
            StateMachineState switch
            {
                CompletedState => StateMachineStatus.Completed,
                // MoveNext can be executed from initial or awaiting states
                _ when _moveNextRunnerThread != null => StateMachineStatus.RunningMoveNext,
                InitialState => StateMachineStatus.Created,
                _ => StateMachineStatus.Awaiting,
            };

        public int StateMachineState => (int)ClrInstance["<>1__state"].Instance?.Value!;

        /// <summary>
        /// Gets a resulting task for a given state machine instance.
        /// </summary>
        public static ClrInstance GetResultingTask(ClrInstance stateMachine, TypesRegistry registry)
        {
            Contract.Requires(registry.IsAsyncStateMachine(stateMachine.Type), $"A given instance should be a compiler generated state machine. Actual type: '{stateMachine.Type!.Name}'.");

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
                    return asyncMethodBuilderTaskField.Instance;
                }

                 if (registry.IsTask(asyncMethodBuild.Type))
                {
                    return asyncMethodBuild;
                }
            }

            // CoreCLR case
            if (stateMachine.TryGetFieldValue("m_continuationObject", out var continuation))
            {
                return continuation.Instance;
            }

            Contract.Assert(false, "Can't find a resulting task for the instance.");
            throw new System.Exception("Not reachable");
        }

        /// <summary>
        /// Returns a task instance that the current state machine awaits on.
        /// </summary>
        private ClrInstance? GetAwaitedTask()
        {
            // Async state machine could have more then one task awaiter, and in order to find the right one
            // we just look for all <>u__ fields that have non-default value of task awaiter.
            foreach (var field in ClrInstance.Fields)
            {
                if (field.Field.Name.StartsWith("<>u__"))
                {
                    var taskInstance = field.Instance.TryGetFieldValue("m_task")?.Instance;
                    if (taskInstance?.IsNull == false)
                    {
                        return taskInstance;
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public override void Link()
        {
            if (StateMachineState >= 0)
            {
                // Need to process continuations only when the state machine awaits another task..
                if (_awaitedTask != null)
                {
                    AddDependency(_awaitedTask);
                }
            }

            // ResultingTaskNode is null for async void methods.
            if (ResultingTaskNode != null)
            {
                ResultingTaskNode.SetAsyncStateMachine(this);
                base.AddEdge(this, ResultingTaskNode);
            }
        }

        /// <inheritdoc />
        protected override string ToStringCore()
        {
            // We can print: (running after second await)

            // Underlying task may be null when the async method is not started yet (possible in debug builds)
            // or when the async method is finished.

            var status = Status.ToString();
            // The result type of async void is incorrect here!

            var statusText = status != null ? $"[{status}] " : string.Empty;

            // For task completion source we should use current dependnetis, but the dependents from the underlying task instance.
            //var insAndOuts = InsAndOuts(Dependencies.Count, ((CausalityNode?)ResultingTaskNode ?? this).Dependents.Count);
            var insAndOuts = InsAndOuts(Dependencies.Count, Dependents.Count);

            //var type = _underlyingTask?.ClrInstance.Type != null ? $"{_underlyingTask.ClrInstance.Type?.TypeToString(Types)} " : string.Empty;
            string type = _resultingTask.Type != null && !_resultingTask.IsNull ? $"{_resultingTask.Type?.TypeToString(Types)}" : "void";
            var result = $"{insAndOuts} {statusText}async {type} {ClrInstance.Type?.TypeToString(Types)}() ({ClrInstance.ObjectAddress})";

            if (_syncContext != null)
            {
                result += $"{Environment.NewLine}(with sync context: {_syncContext.Type?.TypeToString(Types)})";
            }

            return result;
        }
    }
}
