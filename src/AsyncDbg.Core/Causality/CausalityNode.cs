using System;
using System.Collections.Generic;
using System.Linq;
using AsyncDbg.Core;

#nullable enable

namespace AsyncDbg.Causality
{
    public class CausalityNode
    {
        protected readonly CausalityContext Context;

        protected TypesRegistry Types => Context.Registry;

        /// <summary>
        /// A CLR object instance that backs the current causality node.
        /// </summary>
        public ClrInstance ClrInstance { get; }

        public CausalityNode? CompletionSourceTaskNode { get; private set; }

        public Lazy<Guid> Key { get; }

        public bool IsRoot
        {
            get
            {
                if (Dependents.Count == 0) { return true; }

                if (this is ThreadNode) { return true; }

                if (this is AsyncStateMachineNode stateMachine && stateMachine.Dependents.Count != 0) { return true; }

                //if (Dependents.Count == 0) return true;
                return false;
            }
        }

        public bool IsLeaf => Dependencies.Count == 0;

        /// <summary>
        /// Some nodes in the async graph are auxiliary and should not be visible.
        /// For instance, TaskCompletionSource instance and the underlying Task instance
        /// are tightly coupled together and only one of them should be printed out.
        /// </summary>
        public virtual bool Visible => true;

        public readonly HashSet<CausalityNode> Dependencies = new HashSet<CausalityNode>();
        public readonly HashSet<CausalityNode> Dependents = new HashSet<CausalityNode>();

        public HashSet<CausalityNode> WaitingOn => Dependencies;

        public HashSet<CausalityNode> Unblocks => Dependents;

        protected CausalityNode(CausalityContext context, ClrInstance clrInstance, NodeKind kind)
        {
            Context = context;
            ClrInstance = clrInstance;

            Id = clrInstance.ValueOrDefault?.ToString() ?? string.Empty;
            Kind = kind;
            Key = new Lazy<Guid>(() => ComputeKey());
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

        private static T Return<T>(Func<T> provider) => provider();

        protected CausalityNode? TryGetNodeFor(ClrInstance? instance)
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

        public IEnumerable<CausalityNode> EnumerateDependenciesAndSelfDepthFirst()
        {
            return EnumerateNeighborsAndSelfDepthFirst(n => n.Dependencies);
        }

        public IEnumerable<CausalityNode> EnumerateNeighborsAndSelfDepthFirst()
        {
            return EnumerateNeighborsAndSelfDepthFirst(n => n.Dependencies.Concat(n.Dependents));
        }

        public Guid ComputeKey()
        {
            return Guid.NewGuid();
            //Murmur3 murmur = new Murmur3();
            //var bytes = Encoding.UTF8.GetBytes(ClrInstance.AddressRegex.Replace(ToString(), ""));

            //var dependencies = EnumerateDependenciesAndSelfDepthFirst();
            //var hash = dependencies.Select(t => Encoding.UTF8.GetBytes(ClrInstance.AddressRegex.Replace(t.ToString(), "")));
            //// Hash dependencies nodes and normalized display text for self
            //return murmur.ComputeHash(hash.Select(ba => new ArraySegment<byte>(ba))).AsGuid();
        }

        public IEnumerable<CausalityNode> EnumerateNeighborsAndSelfDepthFirst(Func<CausalityNode, IEnumerable<CausalityNode>> getNeighbors)
        {
            var enumeratedSet = new HashSet<CausalityNode>();

            // Using queue to get depth first left to right traversal. Stack would give right to left traversal.
            // TODO: explain why depth first is so important!
            var queue = new Stack<CausalityNode>();

            queue.Push(this);

            while (queue.Count > 0)
            {
                var next = queue.Pop();

                if (enumeratedSet.Contains(next))
                {
                    continue;
                }

                enumeratedSet.Add(next);
                yield return next;

                foreach (var n in getNeighbors(next))
                {
                    queue.Push(n);
                }
            }
        }

        public string Id { get; }

        public NodeKind Kind { get; }

        public virtual bool IsComplete => false; // Derive types should override the result.

        protected virtual string DisplayStatus
        {
            get
            {
                return Kind.ToString();
            }
        }

        public virtual void Link()
        {

        }

        protected void AddDependency(ClrInstance dependency)
        {
            AddEdge(dependency: Context.GetNode(dependency), dependent: this);
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
            if (dependency.ClrInstance?.IsNull == true || dependency.ClrInstance?.IsNull == true)
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

        //protected virtual string 
        protected virtual string ToStringCore()
        {
            var result = $"{InsAndOuts()} [{DisplayStatus.ToString()}] {ClrInstance?.ToString(Types) ?? ""}";

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
