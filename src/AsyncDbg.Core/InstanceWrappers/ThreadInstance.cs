using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AsyncDbg;
using AsyncDbg.Core;
using AsyncDbgCore.Core;
using Microsoft.Diagnostics.Runtime;
using static System.Environment;

#nullable enable

namespace AsyncCausalityDebuggerNew
{
    /// <summary>
    /// Wrapper around <see cref="ClrInstance"/> that mimics an actual interface of <see cref="Thread"/> class.
    /// </summary>
    public class ThreadInstance
    {
        private readonly ClrThread _thread;
        private readonly TypesRegistry _registry;

        private readonly HashSet<ulong> _tcsSetResultFrames;
        private readonly HashSet<ulong> _stateMachineMoveNextFrames;
        private readonly List<ulong> _stackTraceAddresses;

        public ThreadInstance(ClrThread thread, TypesRegistry registry)
        {
            _thread = thread;
            _registry = registry;

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

        /// <inheritdoc />
        public override string ToString()
        {
            var result = PretifyStackTrace(StackTrace);
            return result;
        }

        private string PretifyStackTrace(IList<ClrStackFrame> stackTrace)
        {
            var sb = new StringBuilder();

            var reversedStack = stackTrace.Reverse().ToList();
            int index = 0;

            var reversedStackAsText = new List<string>(reversedStack.Count);
            for (index = 0; index < reversedStack.Count;)
            {
                // It is easier to reason about the stack in bottom to top order.
                if (TryGetStackFrameAsString(reversedStack, ref index, out var stackFrameText))
                {
                    reversedStackAsText.Add(stackFrameText);
                }
            }

            reversedStackAsText.Reverse();
            return string.Join(NewLine, reversedStackAsText);
        }

        private bool TryGetStackFrameAsString(IList<ClrStackFrame> stackTrace, ref int frameIdx, [NotNullWhenTrue]out string? stackFrameDisplayText)
        {
            stackFrameDisplayText = null;
            try
            {
                var currentFrame = stackTrace[frameIdx];

                if (!ShowInStackTrace(currentFrame))
                {
                    return false;
                }

                // Now we try to detect an asynchronous method call in the stack.
                // Usually it looks like this (stack goes up in this case):
                //
                // TypeName+<AsyncMethodA>d__10.MoveNext()
                // System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start[[System.__Canon, mscorlib]](System.__Canon ByRef)
                // TypeName.AsyncMethodA
                // And in this case only the first frame is useful.
                if (TryGetMethodName(currentFrame, out var asyncMethodCandidate) &&
                    frameIdx + 2 < stackTrace.Count &&
                    stackTrace[frameIdx + 1].DisplayString.Contains("System.Runtime.CompilerServices.AsyncTaskMethodBuilder") && stackTrace[frameIdx + 1].DisplayString.Contains("Start") &&
                    stackTrace[frameIdx + 2].DisplayString.Contains(asyncMethodCandidate) && stackTrace[frameIdx + 2].DisplayString.Contains("MoveNext"))
                {
                    // We've found our case.
                    // Skipping the next two frames, because they're useless.
                    frameIdx += 2;
                    stackFrameDisplayText = $"async {currentFrame.DisplayString}";
                }
                else
                {
                    stackFrameDisplayText = currentFrame.DisplayString;
                }
            }
            finally
            {
                frameIdx++;
            }

            return stackFrameDisplayText != null;
        }

        private static bool TryGetMethodName(ClrStackFrame stackFrame, [NotNullWhenTrue]out string? methodName)
        {
            methodName = stackFrame.Method.Name;
            return methodName != null;
        }

        private bool ShowInStackTrace(ClrStackFrame frame)
        {
            var method = frame.Method;

            if (frame.DisplayString == "GCFrame" ||
                frame.DisplayString == "HelperMethodFrame_1OBJ" ||
                frame.DisplayString.Contains("HelperMethodFrame") || // May be, for instance, "[HelperMethodFrame] (System.Threading.Therad.SleepInternal)
                method == null)
            {
                return false;
            }

            var type = method.Type;

            if (_registry.IsTask(type))
            {
                switch (method.Name)
                {
                    case "ExecuteWithThreadLocal":
                    case "Execute":
                    case "ExecutionContextCallback":
                    case "ExecuteEntry":
                    case "InnerInvoke":
                        return false;
                }
            }

            if (type.IsOfType(typeof(ExecutionContext)))
            {
                switch (method.Name)
                {
                    case "RunInternal":
                    case "Run":
                        return false;
                }
            }

            if (type.IsOfType(typeof(ExceptionDispatchInfo)) && method.Name == "Throw")
            {
                return false;
            }

            if (type.IsOfType(typeof(TaskAwaiter)) ||
                type.IsOfType(typeof(TaskAwaiter<>)) ||
                type.IsOfType(typeof(ConfiguredTaskAwaitable.ConfiguredTaskAwaiter)) ||
                type.IsOfType(typeof(ConfiguredTaskAwaitable<>.ConfiguredTaskAwaiter)))
            {
                switch (method.Name)
                {
                    case "HandleNonSuccessAndDebuggerNotification":
                    case "ThrowForNonSuccess":
                    case "ValidateEnd":
                    case "GetResult":
                        return false;
                }
            }

            if (type.Name == "System.ThrowHelper")
            {
                return false;
            }

            return true;

            /*
             * From Ben.Demystifier
             *  var type = method.DeclaringType;
                if (type == typeof(Task<>) && method.Name == "InnerInvoke")
                {
                    return false;
                }
                if (type == typeof(Task))
                {
                    switch (method.Name)
                    {
                        case "ExecuteWithThreadLocal":
                        case "Execute":
                        case "ExecutionContextCallback":
                        case "ExecuteEntry":
                        case "InnerInvoke":
                            return false;
                    }
                }
                if (type == typeof(ExecutionContext))
                {
                    switch (method.Name)
                    {
                        case "RunInternal":
                        case "Run":
                            return false;
                    }
                }

                // Don't show any methods marked with the StackTraceHiddenAttribute
                // https://github.com/dotnet/coreclr/pull/14652
                foreach (var attibute in EnumerableIList.Create(method.GetCustomAttributesData()))
                {
                    // internal Attribute, match on name
                    if (attibute.AttributeType.Name == "StackTraceHiddenAttribute")
                    {
                        return false;
                    }
                }

                if (type == null)
                {
                    return true;
                }

                foreach (var attibute in EnumerableIList.Create(type.GetCustomAttributesData()))
                {
                    // internal Attribute, match on name
                    if (attibute.AttributeType.Name == "StackTraceHiddenAttribute")
                    {
                        return false;
                    }
                }

                // Fallbacks for runtime pre-StackTraceHiddenAttribute
                if (type == typeof(ExceptionDispatchInfo) && method.Name == "Throw")
                {
                    return false;
                }
                else if (type == typeof(TaskAwaiter) ||
                    type == typeof(TaskAwaiter<>) ||
                    type == typeof(ConfiguredTaskAwaitable.ConfiguredTaskAwaiter) ||
                    type == typeof(ConfiguredTaskAwaitable<>.ConfiguredTaskAwaiter))
                {
                    switch (method.Name)
                    {
                        case "HandleNonSuccessAndDebuggerNotification":
                        case "ThrowForNonSuccess":
                        case "ValidateEnd":
                        case "GetResult":
                            return false;
                    }
                }
                else if (type.FullName == "System.ThrowHelper")
                {
                    return false;
                }
             * */
        }
    }
}
