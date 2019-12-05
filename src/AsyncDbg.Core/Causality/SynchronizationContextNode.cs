using AsyncDbg.Core;

#nullable enable

namespace AsyncDbg.Causality
{
    /// <summary>
    /// Represents a <see cref="System.Threading.SynchronizationContext"/> instance..
    /// </summary>
    public class SynchronizationContextNode : CausalityNode
    {
        /// <nodoc />
        public SynchronizationContextNode(CausalityContext context, ClrInstance clrInstance)
            : base(context, clrInstance, NodeKind.SynchronizationContext)
        {
        }

        /// <inheritdoc />
        protected override string ToStringCore()
        {
            var result = base.ToStringCore();
            return result;
        }

        /// <inheritdoc />
        public override void Link()
        {
            // Now we can do the following trick and explore the sync context.
            // Maybe it has some fields that are relevant for us. For instance, AsyncReaderWriterLock uses a sync context to limit the concurrency
            // so the sync context points to a semaphore slim.

            foreach (var f in ClrInstance.Fields)
            {
                if (Context.TryGetNodeFor(f.Instance, out var dependency))
                {
                    AddDependency(dependency);
                }
            }
        }
    }
}
