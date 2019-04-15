using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace AsyncCausalityDebuggerNew
{
    /// <summary>
    /// Wrapper around <see cref="ClrInstance"/> that mimics an actual interface of <see cref="Thread"/> class.
    /// </summary>
    public class ThreadInstance
    {
        private readonly ClrThread _thread;

        private readonly HashSet<ulong> _tcsSetResultFrames;
        private readonly HashSet<ulong> _stateMachineMoveNextFrames;
        private readonly List<ulong> _stackTraceAddresses;

        public ThreadInstance(ClrThread thread, TypesRegistry registry)
        {
            _thread = thread;

            _stateMachineMoveNextFrames = new HashSet<ulong>();
            _tcsSetResultFrames = new HashSet<ulong>();
            _stackTraceAddresses = new List<ulong>();

            foreach (var stackFrame in thread.StackTrace)
            {
                _stackTraceAddresses.Add(stackFrame.StackPointer);

                if (registry.IsTaskCompletionSource(stackFrame.Method?.Type) &&
                    stackFrame.Method?.Name == "TrySetResult")
                {
                    _tcsSetResultFrames.Add(stackFrame.StackPointer);
                }

                if (registry.IsAsyncStateMachine(stackFrame.Method?.Type) &&
                    stackFrame.Method?.Name == "MoveNext")
                {
                    _stateMachineMoveNextFrames.Add(stackFrame.StackPointer);
                }
            }
        }

        public IList<ClrStackFrame> StackTrace => _thread.StackTrace;

        public int StackTraceLength => _thread.StackTrace.Count;

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

            if (result < _thread.StackTrace.Count)
            {
                return _thread.StackTrace[result];
            }

            return null;
        }
    }
}
