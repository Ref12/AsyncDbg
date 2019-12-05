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
        Thread,

        SynchronizationContext,

        // Blocking objects must be processed after threads
        BlockingObject,
        TaskCompletionSource,
    }
}
