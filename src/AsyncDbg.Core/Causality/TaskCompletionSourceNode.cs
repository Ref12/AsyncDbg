using AsyncDbg.Core;

#nullable enable

namespace AsyncDbg.Causality
{
    public class TaskCompletionSourceNode : CausalityNode
    {
        private TaskNode? _underlyingTask;

        /// <nodoc />
        public TaskCompletionSourceNode(CausalityContext context, ClrInstance task)
            : base(context, task, NodeKind.TaskCompletionSource)
        {
            
        }

        /// <inheritdoc />
        public override bool IsComplete
        {
            get
            {
                Contract.AssertNotNull(_underlyingTask, "Underlying task should be initialized first.");
                return _underlyingTask.IsComplete;
            }
        }

        public override bool Visible => false;

        /// <inheritdoc />
        protected override bool AddEdge(CausalityNode dependency, CausalityNode dependent)
        {
            Contract.Assert(dependent.Kind == NodeKind.Task, "Only tasks can directly depend on TaskCompletionSource instances");
            Contract.Assert(dependency.Kind == NodeKind.TaskCompletionSource, "dependency must be TaskCompletionSource");
            Contract.Assert(dependency == this, "dependency == this");
            Contract.Assert(_underlyingTask == null, "AddEdge method should be called only once");

            var taskNode = (TaskNode)dependent;
            _underlyingTask = taskNode;

            //ProcessingContinuations = taskNode.ProcessingContinuations;
            taskNode.SetTaskCompletionSource(this);

            return base.AddEdge(dependency, dependent);
        }

        /// <inheritdoc />
        protected override string ToStringCore()
        {
            Contract.AssertNotNull(_underlyingTask, "AddEdge method should be called to set _underlyingTask to an actual instance.");

            // For task completion source we should use current dependnetis, but the dependents from the underlying task instance.
            var insAndOuts = InsAndOuts(Dependencies.Count, _underlyingTask.Dependents.Count);

            return $"{insAndOuts} {ClrInstance.Type}.Task ({ClrInstance.ObjectAddress})";
        }
    }
}
