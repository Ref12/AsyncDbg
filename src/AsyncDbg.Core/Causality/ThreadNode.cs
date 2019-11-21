using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly HashSet<ulong> _tcsSetResultFrames = new HashSet<ulong>();

        /// <summary>
        /// Instances of async state machines currently running MoveNext method.
        /// </summary>
        private readonly List<ClrInstance> _asyncStateMachinesInMoveNext = new List<ClrInstance>();

        private readonly List<ulong> _stackTraceAddresses = new List<ulong>();

        public IList<ClrStackFrame> StackTrace => _clrThread?.StackTrace ?? new List<ClrStackFrame>();

        public int StackTraceLength => _clrThread?.StackTrace.Count ?? 0;

        /// <nodoc />
        public ThreadNode(CausalityContext context, ClrInstance thread)
            : base(context, thread, NodeKind.Thread)
        {
            _clrThread = TryGetClrThread(context, thread);
            _runtime = context.Runtime;

            if (_clrThread != null)
            {

                _enhancedStackTrace = EnhancedStackTrace.Create(_clrThread.StackTrace, context.Registry);

                foreach (var stackFrame in _clrThread.StackTrace)
                {
                    _stackTraceAddresses.Add(stackFrame.StackPointer);

                    if (context.Registry.IsTaskCompletionSource(stackFrame.Method?.Type) &&
                        stackFrame.Method?.Name == "TrySetResult")
                    {
                        _tcsSetResultFrames.Add(stackFrame.StackPointer);
                    }

                    if (context.Registry.IsAsyncStateMachine(stackFrame.Method?.Type) &&
                        stackFrame.Method?.Name == "MoveNext")
                    {
                        var stateMachine = FindAsyncStateMachine(_clrThread.StackLimit, stackFrame.StackPointer);
                        if (stateMachine != null)
                        {
                            _asyncStateMachinesInMoveNext.Add(stateMachine);
                        }
                    }
                }
            }
        }

        private ClrInstance? FindAsyncStateMachine(ulong start, ulong stop)
        {
            foreach (ulong ptr in EnumeratePointersInRange(start, stop))
            {
                if (_runtime.ReadPointer(ptr, out ulong val))
                {
                    if (Context.Registry.IAsyncStateMachineTypeIndex.TryGetInstanceAt(val, out ClrInstance clrInstance))
                    {
                        // Check the type?
                        return clrInstance;
                    }
                }
            }

            return null;
        }

        // The next two methods are copied from clrmd codebase.
        private IEnumerable<ulong> EnumerateObjectsOfTypes(ulong start, ulong stop, HashSet<string> types)
        {
            ClrHeap heap = _runtime.Heap;
            foreach (ulong ptr in EnumeratePointersInRange(start, stop))
            {
                if (_runtime.ReadPointer(ptr, out ulong obj))
                {
                    if (heap.IsInHeap(obj))
                    {
                        ClrType type = heap.GetObjectType(obj);

                        int sanity = 0;
                        while (type != null)
                        {
                            if (types.Contains(type.Name))
                            {
                                yield return obj;

                                break;
                            }

                            type = type.BaseType;

                            if (sanity++ == 16)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        private IEnumerable<ulong> EnumerateObjectsOfType(ulong start, ulong stop, Func<ClrType, bool> typeMatchPredicate)
        {
            ClrHeap heap = _runtime.Heap;
            foreach (ulong ptr in EnumeratePointersInRange(start, stop))
            {
                if (_runtime.ReadPointer(ptr, out ulong obj))
                {
                    if (heap.IsInHeap(obj))
                    {
                        ClrType type = heap.GetObjectType(obj);

                        int sanity = 0;
                        while (type != null)
                        {
                            if (typeMatchPredicate(type))
                            {
                                yield return obj;

                                break;
                            }

                            type = type.BaseType;

                            if (sanity++ == 16)
                            {
                                break;
                            }
                        }
                    }
                }
            }
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

        public IEnumerable<ClrRoot> EnumerateStackObjects()
        {
            return _clrThread?.EnumerateStackObjects() ?? Enumerable.Empty<ClrRoot>();
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
        protected override void AddEdge(CausalityNode? dependency, CausalityNode? dependent)
        {
            if (dependent == this && dependency is ManualResetEventSlimNode mre && BlockedOnTaskAwaiter())
            {
                // If the thread is blocked on task awaiter then we can simplify the graph
                // buy excluding ManualResetEvent node from it.
                // But to do so, ManualResetEventNode should be aware that it was used in this context.
                mre.UsedByTaskAwaiter();
            }

            base.AddEdge(dependency, dependent);
        }

        /// <inheritdoc />
        public override void Link()
        {
            base.Link();

            foreach (var moveNextRunner in _asyncStateMachinesInMoveNext)
            {
                if (TryGetNodeFor(moveNextRunner) is AsyncStateMachineNode stateMachine)
                {
                    stateMachine.RunningMoveNext(this);
                    AddEdge(dependency: this, dependent: stateMachine);
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
        
        /// <summary>
        /// Returns true if the thread is blocked inside <see cref="TaskCompletionSource{TResult}.TrySetResult(TResult)"/>
        /// </summary>
        public bool InsideTrySetResultMethodCall(ClrRoot stackInstance)
        {
            var stackFrame = GetStackFrame(stackInstance);
            return _tcsSetResultFrames.Contains(stackFrame?.StackPointer ?? ulong.MaxValue);
        }

        private ClrStackFrame? GetStackFrame(ClrRoot stackObject)
        {
            // TODO: I'm not sure I need it!
            if (stackObject.StackFrame != null)
            {
                return stackObject.StackFrame;
            }

            var result = _stackTraceAddresses.BinarySearch(stackObject.Address);
            if (result < 0)
            {
                result = ~result;
            }

            if (result < StackTrace.Count)
            {
                return StackTrace[result];
            }

            return null;
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
