using System.Threading.Tasks;

namespace AsyncCausalityDebuggerNew
{
    class TaskInstanceHelpers
    {
        // State constants for m_stateFlags;
        // The bits of m_stateFlags are allocated as follows:
        //   0x40000000 - TaskBase state flag
        //   0x3FFF0000 - Task state flags
        //   0x0000FF00 - internal TaskCreationOptions flags
        //   0x000000FF - publicly exposed TaskCreationOptions flags
        //
        // See TaskCreationOptions for bit values associated with TaskCreationOptions
        //
        private const int OptionsMask = 0xFFFF; // signifies the Options portion of m_stateFlags bin: 0000 0000 0000 0000 1111 1111 1111 1111
        internal const int TASK_STATE_STARTED = 0x10000;                                       //bin: 0000 0000 0000 0001 0000 0000 0000 0000
        internal const int TASK_STATE_DELEGATE_INVOKED = 0x20000;                              //bin: 0000 0000 0000 0010 0000 0000 0000 0000
        internal const int TASK_STATE_DISPOSED = 0x40000;                                      //bin: 0000 0000 0000 0100 0000 0000 0000 0000
        internal const int TASK_STATE_EXCEPTIONOBSERVEDBYPARENT = 0x80000;                     //bin: 0000 0000 0000 1000 0000 0000 0000 0000
        internal const int TASK_STATE_CANCELLATIONACKNOWLEDGED = 0x100000;                     //bin: 0000 0000 0001 0000 0000 0000 0000 0000
        internal const int TASK_STATE_FAULTED = 0x200000;                                      //bin: 0000 0000 0010 0000 0000 0000 0000 0000
        internal const int TASK_STATE_CANCELED = 0x400000;                                     //bin: 0000 0000 0100 0000 0000 0000 0000 0000
        internal const int TASK_STATE_WAITING_ON_CHILDREN = 0x800000;                          //bin: 0000 0000 1000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_RAN_TO_COMPLETION = 0x1000000;                           //bin: 0000 0001 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_WAITINGFORACTIVATION = 0x2000000;                        //bin: 0000 0010 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_COMPLETION_RESERVED = 0x4000000;                         //bin: 0000 0100 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_THREAD_WAS_ABORTED = 0x8000000;                          //bin: 0000 1000 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_WAIT_COMPLETION_NOTIFICATION = 0x10000000;               //bin: 0001 0000 0000 0000 0000 0000 0000 0000
        //This could be moved to InternalTaskOptions enum
        internal const int TASK_STATE_EXECUTIONCONTEXT_IS_NULL = 0x20000000;                   //bin: 0010 0000 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_TASKSCHEDULED_WAS_FIRED = 0x40000000;                    //bin: 0100 0000 0000 0000 0000 0000 0000 0000

        // A mask for all of the final states a task may be in
        private const int TASK_STATE_COMPLETED_MASK = TASK_STATE_CANCELED | TASK_STATE_FAULTED | TASK_STATE_RAN_TO_COMPLETION;

        // Values for ContingentProperties.m_internalCancellationRequested.
        private const int CANCELLATION_REQUESTED = 0x1;

        public static TaskStatus GetStatus(int flags)
        {
            TaskStatus rval;

            // get a cached copy of the state flags.  This should help us
            // to get a consistent view of the flags if they are changing during the
            // execution of this method.

            if ((flags & TASK_STATE_FAULTED) != 0)
                rval = TaskStatus.Faulted;
            else if ((flags & TASK_STATE_CANCELED) != 0)
                rval = TaskStatus.Canceled;
            else if ((flags & TASK_STATE_RAN_TO_COMPLETION) != 0)
                rval = TaskStatus.RanToCompletion;
            else if ((flags & TASK_STATE_WAITING_ON_CHILDREN) != 0)
                rval = TaskStatus.WaitingForChildrenToComplete;
            else if ((flags & TASK_STATE_DELEGATE_INVOKED) != 0)
                rval = TaskStatus.Running;
            else if ((flags & TASK_STATE_STARTED) != 0)
                rval = TaskStatus.WaitingToRun;
            else if ((flags & TASK_STATE_WAITINGFORACTIVATION) != 0)
                rval = TaskStatus.WaitingForActivation;
            else
                rval = TaskStatus.Created;

            return rval;
        }
    }
}
