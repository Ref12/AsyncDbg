using AsyncDbg.Core;
using System;

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

            // Establishing the edge like: 'async state machine -> await task continuation',
            // because the conituation usually points to MoveNextRunner that moves a given state machine forward.

            if (continuation != null)
            {
                // TODO: maybe add a name of the link: like 'Runs IAsyncStateMachine.MoveNext'
                AddDependent(continuation);
            }

            // Now we can do the following trick and explore the sync context.
            // Maybe it has some fields that are relevant for us. For instance, AsyncReaderWriterLock uses a sync context to limit the concurrency
            // so the sync context points to a semaphore slim.

            if (SyncContext != null)
            {
                foreach (var f in SyncContext.Fields)
                {
                    if (Context.TryGetNodeFor(f.Instance, out var dependency))
                    {
                        // TODO: maybe add a name for the link, something like 'from SyncContext'.
                        // TODO: another option is to add sync context as a separate node here!
                        AddDependency(dependency);
                    }
                }
            }
        }

        /// <inheritdoc />
        protected override string ToStringCore()
        {
            var result = base.ToStringCore();

            if (SyncContext != null)
            {
                result += $"{Environment.NewLine}(with sync context: {SyncContext.Type?.TypeToString(Context.Registry)})";
            }

            return result;
        }
    }
}
