using AsyncDbg.Core;
using System.Linq;

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
        /// Returns <see cref="System.Threading.SynchronizationContext"/> instance if awailable.
        /// </summary>
        public ClrInstance? SyncContext => ClrInstance.TryGetFieldValue("m_syncContext")?.Instance;

        /// <inheritdoc />
        public override void Link()
        {
            var continuation = ContinuationObject;

            // Establishing the edge like: 'async state machine -> await task continuation',

            if (continuation != null)
            {
                // TODO: maybe add a name of the link: like 'Runs IAsyncStateMachine.MoveNext'
                if (Context.TryGetNodeFor(continuation, out var continuationNode))
                {
                    if (continuationNode is AsyncStateMachineNode asm)
                    {
                        // Setting a sync context to make display string more descriptive.
                        asm.SetSyncContext(SyncContext);
                    }
                }

                AddDependent(continuation);
            }

            if (SyncContext != null)
            {
                AddDependency(SyncContext);
            }
        }

        /// <inheritdoc />
        public override void Simplify()
        {
            var awaitNode = Dependents.OfType<AsyncStateMachineNode>().FirstOrDefault();
            if (awaitNode != null && awaitNode.AwaitedTaskNode?.Status == System.Threading.Tasks.TaskStatus.WaitingForActivation)
            {
                // This is a very specific case, but it simplifies the final graph a lot.
                // In async method invocation chain, every async method has a dependency to SyncContextAwaitTaskContinuation
                // because every step of async method (if not decorated with ConfigureAwait(false)) is called
                // on a captured sync context.
                // When every async method in the chain is in awaiting state, and the awaited task is indeed in an awaited state
                // then we can remove this node from the graph to make it clearer.
                //
                // (Yes, it is possible that the state machine is in awaited state but the awaited task is completed.
                // This is the main case when the deadlock caused by the sync over async is happening).
                var syncContextAwaitTaskContinuation = awaitNode.Dependencies.OfType<AwaitTaskContinuationNode>().FirstOrDefault();
                if (syncContextAwaitTaskContinuation != null)
                {
                    //RemoveThisNode();
                }
            }
        }
    }
}
