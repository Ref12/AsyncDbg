using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncDbgCore.New;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace AsyncCausalityDebuggerNew
{
    public class VisualNode
    {
        public VisualContext Context { get; }
        public CausalityNode CausalityNode { get; }

        public bool IsActive { get; set; } = true;

        public HashSet<CausalityNode> AssociatedNodes { get; } = new HashSet<CausalityNode>();

        public string DisplayString { get; set; }

        public HashSet<VisualNode> Unblocks { get; } = new HashSet<VisualNode>();
        public HashSet<VisualNode> WaitingOn { get; } = new HashSet<VisualNode>();

        public VisualNode(VisualContext context, CausalityNode node)
        {
            Context = context;
            CausalityNode = node;
            AssociatedNodes.Add(node);
        }

        public override string ToString()
        {
            return DisplayString ?? CausalityNode.ToString();
        }

        public void Collapse(VisualNode other, string newDisplayString = null)
        {
            other.IsActive = false;
            DisplayString = newDisplayString ?? DisplayString;

            foreach (var node in other.AssociatedNodes)
            {
                Context.Associate(this, node);
            }

            foreach (var node in other.Unblocks)
            {
                node.WaitingOn.Remove(other);
                node.WaitingOn.Add(this);
            }

            foreach (var node in other.WaitingOn)
            {
                node.Unblocks.Remove(other);
                node.Unblocks.Add(this);
            }
        }
    }

    public class AsyncStateMachineVisualNode : VisualNode
    {
        public CausalityNode CreatedTaskNode { get; }

        public AsyncStateMachineVisualNode(VisualContext context, CausalityNode asyncStateMachineNode, CausalityNode createdTaskNode) 
            : base(context, asyncStateMachineNode)
        {
            CreatedTaskNode = createdTaskNode;
            context.Associate(this, createdTaskNode);
        }

        public override string ToString()
        {
            // Show return type from created task node
            return base.ToString();
        }
    }
}