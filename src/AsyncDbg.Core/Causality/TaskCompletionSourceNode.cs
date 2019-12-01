using AsyncDbg.Core;

#nullable enable

namespace AsyncDbg.Causality
{
    public class TaskCompletionSourceNode : CausalityNode
    {
        /// <nodoc />
        public TaskCompletionSourceNode(CausalityContext context, ClrInstance clrInstance)
            : base(context, clrInstance, NodeKind.TaskCompletionSource)
        {
            
        }

        /// <inheritdoc />
        public override bool IsComplete => UnderlyingTaskNode.IsComplete;

        public override bool Visible => false;

        /// <inheritdoc />
        protected override string ToStringCore()
        {
            // For task completion source we should use current dependnetis, but the dependents from the underlying task instance.
            var insAndOuts = InsAndOuts(Dependencies.Count, Dependents.Count);

            return $"{insAndOuts} {ClrInstance.Type}.Task ({ClrInstance.ObjectAddress})";
        }

        public TaskNode UnderlyingTaskNode
        {
            get
            {
                TaskNode? result = (TaskNode?)TryGetNodeFor(ClrInstance["m_task"].Instance);
                Contract.AssertNotNull(result);
                return result;
            }
        }

        /// <inheritdoc />
        public override void Link()
        {
            UnderlyingTaskNode.SetTaskCompletionSource(this);
            AddEdge(dependency: this, dependent: UnderlyingTaskNode);
        }
    }
}
