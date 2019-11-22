using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        /// A list of all causaility node instances found running on the stack.
        /// For instance, ReaderWriteLockSlim.Wait(), Task.Wait(), AsyncStatMachine.MoveNext etc.
        /// </summary>
        private readonly HashSet<(CausalityNode node, string method)> _causailityNodesOnTheStack = new HashSet<(CausalityNode node, string method)>();

        private int StackTraceLength => _clrThread?.StackTrace.Count ?? 0;

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
                if (type == null && methodName == null)
                {
                    continue;
                }

                CausalityNode? causalityNode = FindCausalityNode(clrThread.StackLimit, stackFrame.StackPointer);
                if (causalityNode != null && methodName != null)
                {
                    _causailityNodesOnTheStack.Add((node: causalityNode, method: methodName));
                }
            }
        }

        private CausalityNode? FindCausalityNode(ulong start, ulong stop)
        {
            foreach (ulong ptr in EnumeratePointersInRange(start, stop))
            {
                if (_runtime.ReadPointer(ptr, out ulong address))
                {
                    if (Context.TryGetNodeAt(address, out var result))
                    {
                        return result;
                    }
                }
            }

            return null;
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
        public override bool IsComplete => Dependencies.All(d => d.IsComplete) && StackTraceLength == 0;

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
        protected override bool AddEdge(CausalityNode? dependency, CausalityNode? dependent)
        {
            if (dependent == this && dependency is ManualResetEventSlimNode mre && BlockedOnTaskAwaiter())
            {
                // If the thread is blocked on task awaiter then we can simplify the graph
                // buy excluding ManualResetEvent node from it.
                // But to do so, ManualResetEventNode should be aware that it was used in this context.
                mre.UsedByTaskAwaiter();
            }

            return base.AddEdge(dependency, dependent);
        }

        /// <inheritdoc />
        public override void Link()
        {
            base.Link();

            foreach ((CausalityNode node, string method) in _causailityNodesOnTheStack)
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

        private static ClrThread? TryGetClrThread(CausalityContext context, ClrInstance instance)
        {
            // Indexer checks for _managedThreadId automatically (the field was renamed in coreclr).
            var threadId = (int)instance["m_ManagedThreadId"].Instance.ValueOrDefault!;

            // Not all the threads are presented in the context. If it's not there, the method returns null.
            context.TryGetThreadById(threadId, out var clrThread);
            return clrThread;
        }
        
        /// <summary>
        /// Returns true if the current thread instance is blocked on ManualResetEventSlim because of TaskAwaiter.GetResult.
        /// </summary>
        private bool BlockedOnTaskAwaiter()
        {
            return _enhancedStackTrace?.StackFrames.FirstOrDefault()?.Kind == EnhancedStackFrameKind.GetAwaiterGetResult;
        }
    }
}
