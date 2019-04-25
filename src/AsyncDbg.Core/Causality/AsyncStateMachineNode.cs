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
