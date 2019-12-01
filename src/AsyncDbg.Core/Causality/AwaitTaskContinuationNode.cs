using AsyncDbg.Core;

#nullable enable

namespace AsyncDbg.Causality
{
    public class AwaitTaskContinuationNode : CausalityNode
    {
        public AwaitTaskContinuationNode(CausalityContext context, ClrInstance clrInstance)
            : base(context, clrInstance, NodeKind.AwaitTaskContinuation)
        {
        }

        // - TaskContinuation: abstract base that provides a virtual Run method
        //     - StandardTaskContinuation: wraps a task,options,and scheduler, and overrides Run to process the task with that configuration
        //     - AwaitTaskContinuation: base for continuations created through TaskAwaiter; targets default scheduler by default
        //         - TaskSchedulerAwaitTaskContinuation: awaiting with a non-default TaskScheduler
        //         - SynchronizationContextAwaitTaskContinuation: awaiting with a "current" sync ctx


        public ClrInstance? ContinuationObject => ContinuationResolver.TryResolveContinuationForAction(ClrInstance["m_action"].Instance, Context);

        /// <summary>
        /// Returns true if the current instance is SynchronizationContextAwaitTaskContinuation instance.
        /// We can check this by looking into the field m_syncContext.
        /// </summary>
        public bool IsSyncContextAware => SyncContext != null;

        /// <summary>
        /// Returns <see cref="System.Threading.SynchronizationContext"/> instance if awailable.
        /// </summary>
        public ClrInstance? SyncContext => ClrInstance.TryGetFieldValue("m_syncContext")?.Instance;

        /// <inheritdoc />
        public override void Link()
        {
            var continuation = ContinuationObject;

            // Await task continuation usually points to a state machine.
            if (SyncContext != null && TryGetNodeFor(continuation) is AsyncStateMachineNode asyncStateMachine)
            {
                asyncStateMachine.SetSyncContext(SyncContext);
            }
            else if (continuation != null)
            {
                AddDependency(continuation);
            }
        }
    }
}
