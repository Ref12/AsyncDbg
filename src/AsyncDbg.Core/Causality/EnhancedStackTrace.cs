using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using AsyncDbgCore.Core;
using Microsoft.Diagnostics.Runtime;
using static System.Environment;
using System.Runtime.CompilerServices;

#nullable enable

namespace AsyncDbg.Causality
{
    public class EnhancedStackFrame
    {
        public EnhancedStackFrameKind Kind { get; }

        public string DisplayText { get; }

        public ClrStackFrame Frame { get; }

        public EnhancedStackFrame(EnhancedStackFrameKind kind, string displayText, ClrStackFrame frame)
            => (Kind, DisplayText, Frame) = (kind, displayText, frame);

        public EnhancedStackFrame(EnhancedStackFrameKind kind, ClrStackFrame frame)
            => (Kind, DisplayText, Frame) = (kind, frame.DisplayString, frame);

        /// <inheritdoc />
        public override string ToString() => DisplayText;
    }

    public enum EnhancedStackFrameKind
    {
        RegularStackFrame,
        GetAwaiterGetResult,
        AsyncMethodCall,
    }

    public class EnhancedStackTrace
    {
        public List<EnhancedStackFrame> StackFrames { get; }

        private EnhancedStackTrace(List<EnhancedStackFrame> stackFrames) => StackFrames = stackFrames;

        public static EnhancedStackTrace Create(IList<ClrStackFrame> clrStack, TypesRegistry registry)
        {
            // It is easier to reason about the reversed stack.
            var reversedStack = clrStack.Reverse().ToList();
            var index = 0;

            var reservedEnhancedStack = new List<EnhancedStackFrame>(clrStack.Count);
            for (index = 0; index < reversedStack.Count;)
            {
                // It is easier to reason about the stack in bottom to top order.
                if (TryCreateEnhancedStackFrame(reversedStack, registry, ref index, out var enhancedStackFrame))
                {
                    reservedEnhancedStack.Add(enhancedStackFrame);
                }
            }

            reservedEnhancedStack.Reverse();
            return new EnhancedStackTrace(reservedEnhancedStack);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Join(NewLine, StackFrames);
        }

        private static bool TryCreateEnhancedStackFrame(IList<ClrStackFrame> stackTrace, TypesRegistry types, ref int frameIdx, [NotNullWhenTrue]out EnhancedStackFrame? enhancedStackFrame)
        {
            enhancedStackFrame = null;
            try
            {
                var currentFrame = stackTrace[frameIdx];

                if (!ShowInStackTrace(currentFrame, types))
                {
                    return false;
                }

                if (IsGetAwaiterGetResult(stackTrace, ref frameIdx, out enhancedStackFrame))
                {
                    return true;
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
                    enhancedStackFrame = new EnhancedStackFrame(EnhancedStackFrameKind.AsyncMethodCall, $"async {currentFrame.DisplayString}", currentFrame);
                }
                else
                {
                    enhancedStackFrame = new EnhancedStackFrame(EnhancedStackFrameKind.RegularStackFrame, currentFrame);
                }
            }
            finally
            {
                frameIdx++;
            }

            return enhancedStackFrame != null;
        }

        private static bool TryGetMethodName(ClrStackFrame stackFrame, [NotNullWhenTrue]out string? methodName)
        {
            methodName = stackFrame.Method.Name;
            return methodName != null;
        }

        private static bool ShowInStackTrace(ClrStackFrame frame, TypesRegistry types)
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

            if (types.IsTask(type))
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

            if (type.Name == "System.ThrowHelper")
            {
                return false;
            }

            return true;
        }

        private static bool IsGetAwaiterGetResult(IList<ClrStackFrame> stackTrace, ref int frameIdx, [NotNullWhenTrue]out EnhancedStackFrame? result)
        {
            result = null;
            var frame = stackTrace[frameIdx];

            var method = frame.Method;
            if (method == null)
            {
                return false;
            }

            var type = method.Type;

            if (method.Name == "GetResult" &&
                (type.IsOfType(typeof(TaskAwaiter)) ||
                 type.IsOfType(typeof(TaskAwaiter<>)) ||
                 type.IsOfType(typeof(ConfiguredTaskAwaitable.ConfiguredTaskAwaiter)) ||
                 type.IsOfType(typeof(ConfiguredTaskAwaitable<>.ConfiguredTaskAwaiter))))
            {
                // This is GetAwaiter().GetResult()
                result = new EnhancedStackFrame(EnhancedStackFrameKind.GetAwaiterGetResult, frame);

                // We may potentially skip a bunch of useless frames.
                // Looking for the frame with ManualResetEventSlim.Wait().
                // The usual structure is this one;
                // System.Threading.ManualResetEventSlim.Wait(Int32, System.Threading.CancellationToken)
                // System.Threading.Tasks.Task.SpinThenBlockingWait(Int32, System.Threading.CancellationToken)
                // System.Threading.Tasks.Task.InternalWait(Int32, System.Threading.CancellationToken)
                // System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(System.Threading.Tasks.Task)
                // System.Runtime.CompilerServices.TaskAwaiter.GetResult()
                if (frameIdx + 4 < stackTrace.Count)
                {
                    var manualResetEvent = stackTrace[frameIdx + 4];
                    if (manualResetEvent?.Method?.Type?.IsOfType(typeof(ManualResetEventSlim)) == true)
                    {
                        frameIdx += 4;
                    }
                }

                return true;
            }

            return false;
        }
    }
}
