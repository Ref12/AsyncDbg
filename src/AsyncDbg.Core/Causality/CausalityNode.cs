using System;
using System.Collections.Generic;
using AsyncDbg.Core;

#nullable enable

namespace AsyncDbg.Causality
{
    public class CausalityNode : ICausalityNode
    {
        protected readonly CausalityContext Context;

        /// <summary>
        /// A CLR object instance that backs the current causality node.
        /// </summary>
        protected readonly ClrInstance ClrInstance;

        /// <summary>
        /// A set of causaility nodes waiting (or awaiting, or blocked on) the current node.
        /// </summary>
        public HashSet<ICausalityNode> Dependencies { get; } = new HashSet<ICausalityNode>();

        /// <summary>
        /// A set of causality nodes that the current node unblocks.
        /// </summary>
        public HashSet<ICausalityNode> Dependents { get; } = new HashSet<ICausalityNode>();

        /// <summary>
        /// A unique identity of the current causality node.
        /// </summary>
        public string Id { get; }

        /// <nodoc />
        public NodeKind Kind { get; }

        protected CausalityNode(CausalityContext context, ClrInstance clrInstance, NodeKind kind)
        {
            Context = context;
            ClrInstance = clrInstance;

            Id = Contract.AssertNotNull(clrInstance.ValueOrDefault?.ToString(), "ValueOrDefault should not be null");
            Kind = kind;
        }

        public static CausalityNode Create(CausalityContext context, ClrInstance clrInstance, NodeKind kind)
        {
            return kind switch
            {
                NodeKind.Task => new TaskNode(context, clrInstance),
                NodeKind.TaskCompletionSource => new TaskCompletionSourceNode(context, clrInstance),
                NodeKind.AsyncStateMachine => new AsyncStateMachineNode(context, clrInstance),
                NodeKind.AwaitTaskContinuation => new AwaitTaskContinuationNode(context, clrInstance),
                NodeKind.Thread => new ThreadNode(context, clrInstance),
                NodeKind.ManualResetEventSlim => new ManualResetEventSlimNode(context, clrInstance),
                NodeKind.SemaphoreSlim => new SemaphoreSlimNode(context, clrInstance),
                _ => Return(() => new CausalityNode(context, clrInstance, kind)),
            };
        }

        public virtual bool IsComplete => false; // Derive types should override the result.

        protected virtual string DisplayStatus
        {
            get
            {
                return Kind.ToString();
            }
        }

        private static T Return<T>(Func<T> provider) => provider();

        protected CausalityNode? TryGetCausalityNodeFor(ClrInstance? instance)
        {
            if (instance == null)
            {
                return null;
            }

            if (Context.TryGetNodeFor(instance, out var result))
            {
                return result;
            }

            return null;
        }

        public string CreateDisplayText()
        {
            var prefix = $"({Dependencies.Count}, {Dependents.Count}) ";
            var suffix = $"({Id})";

            var mainText = ToString();

            return $"{prefix} {mainText} {suffix}";
        }

        public virtual void Link()
        {

        }

        protected void AddDependency(ClrInstance dependency)
        {
            AddEdge(dependency: Context.GetNode(dependency), dependent: this);
        }

        protected void AddDependency(CausalityNode dependency)
        {
            AddEdge(dependency: dependency, dependent: this);
        }

        protected void AddDependent(ClrInstance dependent)
        {
            AddEdge(dependency: this, dependent: Context.GetNode(dependent));
        }

        protected void AddDependent(CausalityNode dependent)
        {
            AddEdge(dependency: this, dependent: dependent);
        }

        protected virtual bool AddEdge(CausalityNode dependency, CausalityNode dependent)
        {
            if (dependency.ClrInstance?.IsNull == true || dependent.ClrInstance?.IsNull == true)
            {
                // Can't add edge to nothing
                return false;
            }

            if (dependency == dependent)
            {
                // Avoiding self-references.
                return false;
            }

            dependency.Dependents.Add(dependent);
            dependent.Dependencies.Add(dependency);
            return true;
        }

        /// <inheritdoc />
        public sealed override string ToString()
        {
            return ToStringCore();
        }

        protected virtual string ToStringCore()
        {
            var result = $"{InsAndOuts()} [{DisplayStatus.ToString()}] {ClrInstance?.ToString(Context.Registry) ?? ""}";

            return result;
        }

        protected static string InsAndOuts(int dependencies, int dependents)
        {
            var up = '\x2191';
            var down = '\x2193';
            return $"({up}:{dependencies}, {down}:{dependents})";
        }

        protected string InsAndOuts() => InsAndOuts(Dependencies.Count, Dependents.Count);
    }
}
