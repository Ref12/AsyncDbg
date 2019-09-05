﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using AsyncDbg.Core;
using Microsoft.Diagnostics.Runtime;

using static System.Environment;

#nullable enable

namespace AsyncDbg.Causality
{
    public class ThreadNode : CausalityNode
    {
        private readonly ClrThread? _clrThread;
        private readonly EnhancedStackTrace? _enhancedStackTrace;
        private readonly HashSet<ulong> _tcsSetResultFrames = new HashSet<ulong>();
        private readonly HashSet<ulong> _stateMachineMoveNextFrames = new HashSet<ulong>();
        private readonly List<ulong> _stackTraceAddresses = new List<ulong>();

        public IList<ClrStackFrame> StackTrace => _clrThread?.StackTrace ?? new List<ClrStackFrame>();

        public int StackTraceLength => _clrThread?.StackTrace.Count ?? 0;

        /// <nodoc />
        public ThreadNode(CausalityContext context, ClrInstance thread)
            : base(context, thread, NodeKind.Thread)
        {
            _clrThread = TryGetClrThread(context, thread);
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
                        _stateMachineMoveNextFrames.Add(stackFrame.StackPointer);
                    }
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

        protected override void AddEdge(CausalityNode dependency, CausalityNode dependent)
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

        private static ClrThread? TryGetClrThread(CausalityContext context, ClrInstance instance)
        {
            var threadId = (int)instance["m_ManagedThreadId"].Instance.ValueOrDefault;

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

        /// <summary>
        /// Returns true if <see cref="IAsyncStateMachine.MoveNext"/> method call is somewhere on the thread's stack.
        /// </summary>
        public bool HasAsyncStateMachineMoveNextCall(ClrRoot stackInstance)
        {
            // This method is way less precise than InsideTrySetResultMethodCall, because
            // the tread can be actually down the stack and do something else already.
            return _stateMachineMoveNextFrames.Count != 0;
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
