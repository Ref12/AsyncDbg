using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncDbg.Core;
using Microsoft.Diagnostics.Runtime;

using static System.Environment;

#nullable enable

namespace AsyncDbg.Causality
{
    /// <summary>
    /// Represents a managed thread node.
    /// </summary>
    public class ThreadNode : CausalityNode
    {
        private readonly ClrRuntime _runtime;
        private readonly ClrThread? _clrThread;

        private readonly EnhancedStackTrace? _enhancedStackTrace;

        /// <summary>
        /// A list of all object instances of relevant types found on the stack.
        /// For instance, ReaderWriteLockSlim.Wait(), Task.Wait(), AsyncStatMachine.MoveNext etc.
        /// We keep addresses here and not actual causality node instances, because some of the nodes may not be created
        /// at the thread's instance construction time.
        /// </summary>
        private readonly HashSet<(ClrInstance clrInstance, string method)> _objectsOnTheStack = new HashSet<(ClrInstance clrInstance, string method)>();

        /// <nodoc />
        public ThreadNode(CausalityContext context, ClrInstance thread)
            : base(context, thread, NodeKind.Thread)
        {
            _clrThread = TryGetClrThread(context, thread);
            _runtime = context.Runtime;

            if (_clrThread != null)
            {
                _enhancedStackTrace = EnhancedStackTrace.Create(_clrThread.StackTrace, context.Registry);
                PopulateCausalityNodesOnStack(_clrThread);
            }
        }

        private void PopulateCausalityNodesOnStack(ClrThread clrThread)
        {
            foreach (var stackFrame in clrThread.StackTrace)
            {
                ClrType? type = stackFrame.Method?.Type;
                string? methodName = stackFrame.Method?.Name;
                if (type == null)
                {
                    continue;
                }

                ClrInstance? clrInstance = FindInstanceAddressOfType(clrThread, stackFrame, type);
                if (clrInstance != null && methodName != null)
                {
                    _objectsOnTheStack.Add((clrInstance: clrInstance, method: methodName));
                }
            }
        }

        /// <summary>
        /// Finds a Clr instance of a given type that lives on the stack and returns an address of the instance in the heap.
        /// </summary>
        private ClrInstance? FindInstanceAddressOfType(ClrThread thread, ClrStackFrame frame, ClrType expectedType)
        {
            foreach (ulong ptr in EnumeratePointersForThreadFrame(thread, frame))
            {
                if (_runtime.ReadPointer(ptr, out ulong address))
                {
                    if (!_runtime.Heap.IsInHeap(address))
                    {
                        continue;
                    }

                    ClrType type = _runtime.Heap.GetObjectType(address);

                    // Expected type can be a base type or an interface.
                    // So we need to check if the curren
                    if (type.EnumerateBaseTypesAndSelf().Contains(expectedType))
                    {
                        return ClrInstance.CreateInstance(_runtime.Heap, address, type);
                    }
                }
            }

            return null;
        }

        private IEnumerable<ulong> EnumeratePointersForThreadFrame(ClrThread thread, ClrStackFrame frame)
        {
            // Not sure why we need to do that, but it seems that this is the pattern used for instance here: https://github.com/HarmJ0y/KeeThief/blob/master/KeeTheft/ClrMD/src/Microsoft.Diagnostics.Runtime/Desktop/lockinspection.cs
            return EnumeratePointersInRange(thread.StackLimit, frame.StackPointer)
                .Concat(EnumeratePointersInRange(frame.StackPointer, thread.StackBase));
        }

        private IEnumerable<ulong> EnumeratePointersInRange(ulong start, ulong stop)
        {
            uint diff = (uint)_runtime.PointerSize;

            if (start > stop)
            {
                for (ulong ptr = stop; ptr <= start; ptr += diff)
                {
                    yield return ptr;
                }
            }
            else
            {
                for (ulong ptr = stop; ptr >= start; ptr -= diff)
                {
                    yield return ptr;
                }
            }
        }

        /// <inheritdoc />
        public override bool IsComplete => Dependencies.All(d => d.IsComplete) && (_clrThread?.StackTrace.Count ?? 0) == 0;

        /// <inheritdoc />
        protected override string ToStringCore()
        {
            var result = base.ToStringCore();
            if ((Dependencies.Count != 0 || Dependents.Count != 0) && _enhancedStackTrace != null)
            {
                result += NewLine + _enhancedStackTrace.ToString();
            }

            return result;
        }

        /// <inheritdoc />
        protected override string DisplayStatus => $"{base.DisplayStatus} (Id={_clrThread?.ManagedThreadId})";

        /// <inheritdoc />
        public override void Link()
        {
            foreach ((CausalityNode node, string method) in GetCausalityNodesOnTheStack())
            {
                switch (node, method)
                {
                    case (AsyncStateMachineNode sm, "MoveNext"):
                        sm.RunningMoveNext(this);
                        AddEdge(this, sm);
                        break;
                    case (TaskCompletionSourceNode tcs, string m) when m.StartsWith("Set") || m.StartsWith("TrySet"):
                        AddEdge(this, tcs);
                        break;
                    case ({ Kind: NodeKind.ManualResetEventSlim }, "Wait"):
                    case ({ Kind: NodeKind.ManualResetEvent }, "WaitOne"):
                        AddEdge(dependency: node, dependent: this);
                        break;
                    case (TaskNode task, string m) when task.Status == TaskStatus.Running:
                        AddEdge(dependency: this, dependent: node);
                        break;
                    default:
                        // This is actually pretty common case.
                        // For instance, if a thread is blocked on a task, then we'll see it here with method == "Wait",
                        // but in our case the thread will be blocked on the underlying ManualResetEventSlim.
                        // Console.WriteLine($"Unknown causailty node's status on the stack. ThreadId={_clrThread?.ManagedThreadId}, Method={method}, Node={node}");
                        break;

                }
            };
        }

        private IEnumerable<(CausalityNode node, string method)> GetCausalityNodesOnTheStack()
        {
            foreach (var (clrInstance, method) in _objectsOnTheStack)
            {
                if (Context.TryGetNodeFor(clrInstance, out var node))
                {
                    yield return (node, method);
                }
            }
        }

        private static ClrThread? TryGetClrThread(CausalityContext context, ClrInstance instance)
        {
            // Indexer checks for _managedThreadId automatically (the field was renamed in coreclr).
            var threadId = (int)instance["m_ManagedThreadId"].Instance.ValueOrDefault!;

            // Not all the threads are presented in the context. If it's not there, the method returns null.
            context.TryGetThreadById(threadId, out var clrThread);
            return clrThread;
        }
    }
}
