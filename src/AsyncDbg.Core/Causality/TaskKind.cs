using System.Threading.Tasks;

#nullable enable

namespace AsyncDbg.Causality
{
    /// <summary>
    /// Describes kind of a task.
    /// </summary>
    /// <remarks>
    /// Task type can be used for different purposes: it may be created by <see cref="TaskCompletionSource{TResult}"/>, or as part of async method, or represents a wrapper returned by <see cref="Task.Run(System.Func{Task})"/>.
    /// </remarks>
    public enum TaskKind
    {
        Unknown,
        UnwrapPromise,
        TaskRun,
        WhenAll,
        ContinuationTaskFromTask,
        FromTaskCompletionSource,
        AsyncMethodTask,
        SemaphoreSlimTaskNode,
        VisibleTaskKind = WhenAll
    }
}
