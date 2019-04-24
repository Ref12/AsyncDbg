using AsyncDbg.Core;
using AsyncDbgCore.Core;

#nullable enable

namespace AsyncDbg.Causality
{
    public class AsyncStateMachineNode : CausalityNode
    {
        private TaskNode? _underlyingTask;

        /// <nodoc />
        public AsyncStateMachineNode(CausalityContext context, ClrInstance task)
            : base(context, task, NodeKind.AsyncStateMachine)
        {
            
        }

        /// <inheritdoc />
        public override bool IsComplete => StateMachineState == -2;

        /// <inheritdoc />
        public override bool Visible => false;

        public int StateMachineState
        {
            get
            {
                return (int)ClrInstance["<>1__state"].Instance?.Value;
            }
        }

        public ClrInstance? AwaitedTask
        {
            get
            {
                if (StateMachineState < 0)
                {
                    return null;
                }

                // If an async method awaits for multiple task types, then the generated state machine
                // would have more then one task awaiter in a form <>u__num.
                // To determine which task the state machine awaits on computing the awaiter based on the current state.
                var awaitedTaskFieldName = $"<>u__{StateMachineState + 1}";

                return ClrInstance.TryGetFieldValue(awaitedTaskFieldName)?.Instance.TryGetFieldValue("m_task")?.Instance;
            }
        }

        /// <inheritdoc />
        protected override void AddEdge(CausalityNode dependency, CausalityNode dependent)
        {
            if (dependent is TaskNode taskNode && dependency == this)
            {
                Contract.Assert(_underlyingTask == null, "AddEdge method should be called only once");
                _underlyingTask = taskNode;
                taskNode.SetAsyncStateMachine(this);
            }
            //else
            {
                base.AddEdge(dependency, dependent);
            }
        }

        /// <inheritdoc />
        protected override string ToStringCore()
        {
            // Underlying task may be null when the async method is not started yet (possible in debug builds)
            // or when the async method is finished.

            var status = StateMachineState switch
            {
                -1 => "NotStarted",
                -2 => "Completed",
                _ => null,
            };

            var statusText = status != null ? $"[{status}] " : string.Empty;

            // For task completion source we should use current dependnetis, but the dependents from the underlying task instance.
            var insAndOuts = InsAndOuts(Dependencies.Count, ((CausalityNode?)_underlyingTask ?? this).Dependents.Count);
            
            var type = _underlyingTask?.ClrInstance.Type != null ? $"{_underlyingTask.ClrInstance.Type?.TypeToString(Types)} " : string.Empty;
            return $"{insAndOuts} {statusText}async {type}{ClrInstance.Type?.TypeToString(Types)}() ({ClrInstance.ObjectAddress})";
        }
    }
}
