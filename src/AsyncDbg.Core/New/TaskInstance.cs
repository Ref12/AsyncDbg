using System.Threading.Tasks;
using static AsyncCausalityDebuggerNew.TaskInstanceHelpers;
#nullable enable

namespace AsyncCausalityDebuggerNew
{
    /// <summary>
    /// Helper struct that mimics and actual interface of <see cref="Task"/> class.
    /// </summary>
    public readonly struct TaskInstance
    {
        private readonly ClrInstance _instance;

        public TaskInstance(ClrInstance instance)
        {
            _instance = instance;
        }

        public TaskStatus Status => GetStatus(StateFlags);

        public int Id => (int)(_instance["m_taskId"].Instance.Value);

        public bool IsCompleted => (StateFlags & TASK_STATE_COMPLETED_MASK) != 0;

        public bool IsCanceled => (StateFlags & (TASK_STATE_CANCELED | TASK_STATE_FAULTED)) == TASK_STATE_CANCELED;

        public bool IsFaulted => (StateFlags & TASK_STATE_CANCELED) != 0;

        public object ExecutingTaskScheduler => _instance["m_taskScheduler"].Instance;

        public TaskCreationOptions Options => GetCreationOptions(OptionsMethod(StateFlags));

        private int StateFlags => (int)(_instance["m_stateFlags"].Instance.Value);
    }
}
