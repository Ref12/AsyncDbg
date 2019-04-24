using AsyncDbg.Core;

#nullable enable

namespace AsyncDbg.Causality
{
    public class AwaitTaskContinuationNode : CausalityNode
    {
        public AwaitTaskContinuationNode(CausalityContext context, ClrInstance task)
            : base(context, task, NodeKind.AwaitTaskContinuation)
        {
        }

        /// <summary>
        /// Returns true if the current instance is SynchronizationContextAwaitTaskContinuation instance.
        /// We can check this by looking into the field m_syncContext.
        /// </summary>
        public bool IsSyncContextAware => SyncContext != null;

        /// <summary>
        /// Returns <see cref="System.Threading.SynchronizationContext"/> instance if awailable.
        /// </summary>
        public ClrInstance? SyncContext => ClrInstance.TryGetFieldValue("m_syncContext")?.Instance;
    }
}
