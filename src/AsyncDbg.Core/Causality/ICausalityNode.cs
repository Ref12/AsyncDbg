using System.Collections.Generic;

namespace AsyncDbg.Causality
{
    public interface ICausalityNode
    {
        string Id { get; }

        NodeKind Kind { get; }

        bool IsComplete { get; }

        HashSet<ICausalityNode> Dependencies { get; }

        HashSet<ICausalityNode> Dependents { get; }
    }
    
}
