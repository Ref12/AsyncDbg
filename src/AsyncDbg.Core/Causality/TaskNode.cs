using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AsyncDbg.Core;
using AsyncDbg.InstanceWrappers;

#nullable enable

namespace AsyncDbg.Causality
{
    public class TaskNode : CausalityNode
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private readonly TaskInstance _taskInstance;

        // Not null if the task originates from TaskCompletionSource<T>
        private TaskCompletionSourceNode? _taskCompletionSource;

        // Not null if the task originates from SemaphoreSlim
        private SemaphoreSlimNode? _semaphoreSlimNode;

        // Not null if the task originates from an async state machine.
        private AsyncStateMachineNode? _asyncStateMachine;

        public TaskNode(CausalityContext context, ClrInstance clrInstance)
            : base(context, clrInstance, NodeKind.Task)
        {
            _taskInstance = new TaskInstance(clrInstance);
        }

        /// <nodoc />
        public TaskStatus Status => _taskInstance.Status;

        /// <inheritdoc />
        protected override string DisplayStatus => $"Status={_taskInstance.Status}, Kind={TaskKind}";

        public TaskKind TaskKind
        {
            get
            {
                if (_taskCompletionSource != null) { return TaskKind.FromTaskCompletionSource; }

                if (_asyncStateMachine != null) { return TaskKind.AsyncMethodTask; }

                if (Context.Registry.IsTaskWhenAll(ClrInstance)) { return TaskKind.WhenAll; }

                if (_semaphoreSlimNode != null) { return TaskKind.SemaphoreSlimTaskNode; }

                if (Context.Registry.IsUnwrapPromise(ClrInstance))
                {
                    // There are two unwrap promises:
                    // One is used in Task.Unwrap() extension method
                    // and another one is returned by Task.Run method.
                    // Here is the comment from Task.cs
                    // "Should we check for OperationCanceledExceptions on the outer task and interpret them as proxy cancellation?"
                    // Unwrap() sets this to false, Run() sets it to true.
                    // private readonly bool _lookForOce;
                    return (bool)ClrInstance["_lookForOce"].Instance.Value ? TaskKind.TaskRun : TaskKind.UnwrapPromise;
                }

                if (Status == TaskStatus.Running)
                {
                    return TaskKind.TaskRun;
                }

                if (ClrInstance.Type?.Name.Contains("ContinuationTaskFromTask") == true)
                {
                    return TaskKind.ContinuationTaskFromTask;
                }

                return TaskKind.Unknown;
            }
        }

        public List<ClrInstance> WhenAllContinuations
        {
            get
            {
                Contract.Requires(TaskKind == TaskKind.WhenAll, "Only applicable to WhenAll task kind.");
                return ClrInstance["m_tasks"].Instance.Items.Where(i => i.IsNotNull()).ToList();
            }
        }

        public ClrInstance ContinuationObject => GetContinuationObject();

        private ClrInstance GetContinuationObject()
        {
            var continuationObject = ClrInstance["m_continuationObject"].Instance;
            if (continuationObject.IsNotNull())
            {
                return continuationObject;
            }

            return ClrInstance["m_action"].Instance;
        }

        ///// <inheritdoc />
        //public override bool Visible => TaskKind <= TaskKind.VisibleTaskKind;

        /// <inheritdoc />
        public override bool IsComplete => _taskInstance.IsCompleted;

        public void SetTaskCompletionSource(TaskCompletionSourceNode tcs)
        {
            Contract.Assert(TaskKind == TaskKind.Unknown, $"Can not change task origin because it was already set to '{TaskKind}'");

            _taskCompletionSource = tcs;
        }

        public void SetAsyncStateMachine(AsyncStateMachineNode asyncStateMachine)
        {
            Contract.Assert(TaskKind == TaskKind.Unknown || TaskKind == TaskKind.AsyncMethodTask, $"Can not change task origin because it was already set to '{TaskKind}'");

            _asyncStateMachine = asyncStateMachine;
        }

        public void SetSemaphoreSlim(SemaphoreSlimNode semaphoreSlimNode)
        {
            Contract.Assert(TaskKind == TaskKind.Unknown, $"Can not change task origin because it was already set to '{TaskKind}'");
            _semaphoreSlimNode = semaphoreSlimNode;
        }

        /// <inheritdoc />
        public override void Link()
        {
            if (TaskKind == TaskKind.WhenAll)
            {
                foreach (var item in WhenAllContinuations)
                {
                    AddDependency(item);
                }
            }

            var parent = ClrInstance.TryGetFieldValue("m_parent")?.Instance;
            if (parent.IsNotNull())
            {
                // m_parent is not null for Parallel.ForEach, for instance.
                AddDependent(parent);
            }

            // The continuation instance is a special (causality) node, then just adding the edge
            // without extra processing.
            if (Context.TryGetNodeFor(ContinuationObject, out var dependentNode))
            {
                AddDependent(dependentNode);
            }
            else
            {
                var continuations = ContinuationResolver.TryResolveContinuations(ContinuationObject, Context);
                foreach (var c in continuations)
                {
                    // TODO: need to add an adge name for visualization purposes!
                    // Tasks created with 'ContinueWith' are different from other continuations.
                    // In this case they're dependencies not dependents.
                    if (Context.Registry.IsContinuationTaskFromTask(c))
                    {
                        AddDependency(c);
                    }
                    else
                    {
                        AddDependent(c);
                    }
                }
            }
        }

        /// <inhertidoc />
        protected override string ToStringCore()
        {
            var result = TaskKind switch
            {
                //TaskKind.FromTaskCompletionSource => Contract.AssertNotNull(_taskCompletionSource).ToString(),
                TaskKind.ContinuationTaskFromTask => $"{base.ToStringCore()}{Environment.NewLine}ContinueWith on: {ContinueWithNameToString()}",
                //TaskKind.AsyncMethodTask => Contract.AssertNotNull(_asyncStateMachine).ToString(),
                // var result = $"{InsAndOuts()} [{DisplayStatus.ToString()}] {ClrInstance?.ToString(Types) ?? ""}";
                //TaskKind.WhenAll => $"{InsAndOuts()} [{DisplayStatus.ToString()}] {ClrInstance.ToString(Types)}",
                TaskKind.TaskRun => $"{InsAndOuts()} Task.Run ({ClrInstance.ObjectAddress})",
                _ => base.ToStringCore(),
            };

            return result;

            string ContinueWithNameToString()
            {
                // A current task node is the task created by calling ContinueWith method.
                // So we can grab m_action field and get a "method name" that was given to ContinueWith method.
                // Is it possible to get a method name by the method pointer?
                var action = ClrInstance["m_action"].Instance;
                // Here is a comment in Task.cs
                // internal object m_action;    
                // The body of the task.  Might be Action<object>, Action<TState> or Action.  Or possibly a Func.
                // If m_action is set to null it will indicate that we operate in the
                // "externally triggered completion" mode, which is exclusively meant 
                // for the signalling Task<TResult> (aka. promise). In this mode,
                // we don't call InnerInvoke() in response to a Wait(), but simply wait on
                // the completion event which will be set when the Future class calls Finish().
                // But the event would now be signalled if Cancel() is called
                if (action.IsNull)
                {
                    return "externally triggered completion mode";
                }

                var methodPtr = action["_methodPtr"].Instance;

                var runtime = Context.Runtime;
                var methodInfo = runtime.GetMethodByAddress((ulong)(long)methodPtr.Value);
                if (methodInfo == null)
                {
                    methodPtr = action["_methodPtrAux"].Instance;
                    methodInfo = runtime.GetMethodByAddress((ulong)(long)methodPtr.Value);
                }

                Contract.AssertNotNull(methodInfo, "Can't find method name for a delegate");
                var signature = methodInfo.GetFullSignature();
                return TypeNameHelper.TrySimplifyMethodSignature(signature);
            }
        }
    }
}
