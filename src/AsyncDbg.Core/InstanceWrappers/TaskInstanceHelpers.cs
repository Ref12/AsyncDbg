using System;
using System.Threading.Tasks;

namespace AsyncDbg.InstanceWrappers
{
    public static class TaskInstanceHelpers
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
        internal const int TASK_STATE_COMPLETED_MASK = TASK_STATE_CANCELED | TASK_STATE_FAULTED | TASK_STATE_RAN_TO_COMPLETION;

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

        public static bool IsCompletedMethod(int flags)
        {
            return (flags & TASK_STATE_COMPLETED_MASK) != 0;
        }

        public static TaskCreationOptions GetCreationOptions(TaskCreationOptions options)
        {
            return options & (TaskCreationOptions)~InternalTaskOptions.InternalOptionsMask;
        }

        public static TaskCreationOptions GetOptions(int stateFlags)
        {
            return OptionsMethod(stateFlags);
        }

        public static TaskCreationOptions OptionsMethod(int flags)
        {
            //Contract.Assert((OptionsMask & 1) == 1, "OptionsMask needs a shift in Options.get");
            return (TaskCreationOptions)(flags & OptionsMask);
        }

        [Flags]
        [Serializable]
        internal enum InternalTaskOptions
        {
            /// <summary> Specifies "No internal task options" </summary>
            None,

            /// <summary>Used to filter out internal vs. public task creation options.</summary>
            InternalOptionsMask = 0x0000FF00,

            ChildReplica = 0x0100,
            ContinuationTask = 0x0200,
            PromiseTask = 0x0400,
            SelfReplicating = 0x0800,

            /// <summary>
            /// Store the presence of TaskContinuationOptions.LazyCancellation, since it does not directly
            /// translate into any TaskCreationOptions.
            /// </summary>
            LazyCancellation = 0x1000,

            /// <summary>Specifies that the task will be queued by the runtime before handing it over to the user. 
            /// This flag will be used to skip the cancellationtoken registration step, which is only meant for unstarted tasks.</summary>
            QueuedByRuntime = 0x2000,

            /// <summary>
            /// Denotes that Dispose should be a complete nop for a Task.  Used when constructing tasks that are meant to be cached/reused.
            /// </summary>
            DoNotDispose = 0x4000
        }
    }
}
