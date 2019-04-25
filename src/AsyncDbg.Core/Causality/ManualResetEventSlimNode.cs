using AsyncDbg.Core;

#nullable enable

namespace AsyncDbg.Causality
{
    public class ManualResetEventSlimNode : CausalityNode
    {
        private bool _onInvokeMresInstanceUsedByTaskAwaiter;

        /// <nodoc />
        public ManualResetEventSlimNode(CausalityContext context, ClrInstance task)
            : base(context, task, NodeKind.ManualResetEventSlim)
        {
            
        }

        /// <inheritdoc />
        public override bool Visible => !_onInvokeMresInstanceUsedByTaskAwaiter;

        public void UsedByTaskAwaiter()
        {
            if (ClrInstance.Type?.Name.Contains("SetOnInvokeMres") == true)
            {
                _onInvokeMresInstanceUsedByTaskAwaiter = true;
            }
        }
    }
}
