namespace AsyncDbg
{
    public enum NodeKind
    {
        Unknown,
        Task,
        ValueTask,
        UnwrapPromise,
        ContinuationTask,
        ManualResetEventSlim,
        AwaitTaskContinuation,
        ManualResetEvent,
        AsyncStateMachine,
        SemaphoreSlim,
        SemaphoreWrapper,
        Thread,

        // Blocking objects must be processed after threads
        BlockingObject,
        TaskCompletionSource,
    }
}
