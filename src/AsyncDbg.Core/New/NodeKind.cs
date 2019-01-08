using System.Collections.Generic;
using System.IO;
using System.Text;
using AsyncDbgCore;

namespace AsyncCausalityDebuggerNew
{
    public enum NodeKind
    {
        Unknown,
        Task,
        ValueTask,
        UnwrapPromise,
        ManualResetEventSlim,
        AwaitTaskContinuation,
        ManualResetEvent,
        //AsyncStateMachine,
        SemaphoreSlim,
        SemaphoreWrapper,
        Thread,

        // Blocking objects must be processed after threads
        BlockingObject,
        TaskCompletionSource,
    }
}
