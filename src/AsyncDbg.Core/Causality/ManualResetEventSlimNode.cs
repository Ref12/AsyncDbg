using AsyncDbg.Core;

#nullable enable

namespace AsyncDbg.Causality
{
    public class ManualResetEventSlimNode : CausalityNode
    {
        /// <nodoc />
        public ManualResetEventSlimNode(CausalityContext context, ClrInstance clrInstance)
            : base(context, clrInstance, NodeKind.ManualResetEventSlim)
        {
        }
    }
}
