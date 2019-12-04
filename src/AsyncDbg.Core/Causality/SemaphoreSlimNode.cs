#nullable enable

using AsyncDbg.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AsyncDbg.Causality
{
    /// <summary>
    /// A node that represents <see cref="System.Threading.SemaphoreSlim"/> instance.
    /// </summary>
    public class SemaphoreSlimNode : CausalityNode
    {
        private readonly List<ClrInstance> _asyncWaiters;

        public SemaphoreSlimNode(CausalityContext context, ClrInstance clrInstance) : base(context, clrInstance, NodeKind.SemaphoreSlim)
        {
            _asyncWaiters = GetAsynchronousWaiters().ToList();
        }

        /// <inheritdoc />
        public override void Link()
        {
            foreach (var asyncWaiter in _asyncWaiters)
            {
                if (TryGetCausalityNodeFor(asyncWaiter) is TaskNode taskNode)
                {
                    taskNode.SetSemaphoreSlim(this);
                    AddDependent(asyncWaiter);
                }
            }
        }

        private IEnumerable<ClrInstance> GetAsynchronousWaiters()
        {
            var asyncHead = ClrInstance["m_asyncHead"].Instance;
            while (asyncHead.IsNotNull())
            {
                yield return asyncHead;

                asyncHead = asyncHead["Next"].Instance;
            }
        }

        public int CurrentCount => Convert.ToInt32(ClrInstance["m_currentCount"].Instance.Value);

        public int SyncWaiters => Convert.ToInt32(ClrInstance["m_waitCount"].Instance.Value);

        public int AsyncWaiters => _asyncWaiters.Count;

        /// <inheritdoc />
        protected override string ToStringCore()
        {
            var result = base.ToStringCore();
            result += $"{Environment.NewLine}(CurrentCount={CurrentCount}, SyncWaiters={SyncWaiters}, AsyncWaiters={AsyncWaiters})";
            return result;
        }
    }
}
