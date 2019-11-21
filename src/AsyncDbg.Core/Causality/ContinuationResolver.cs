using System;
using System.Collections.Generic;
using AsyncDbg.Core;

#nullable enable

namespace AsyncDbg.Causality
{
    internal static class ContinuationResolver
    {
        // TODO: this should be causality nodes!!
        // These methods could be something like 'UnwrapContinuation'
        // And the ResolveContinuations should return CausalityNode[]s
        public static ClrInstance[] TryResolveContinuations(ClrInstance continuation, CausalityContext context)
        {
            if (continuation.Type?.Name == "System.Collections.Generic.List<System.Object>")
            {
                var result = new List<ClrInstance>();
                var size = (int)continuation["_size"].Instance.ValueOrDefault!;
                var items = continuation["_items"].Instance.Items;
                for (var i = 0; i < size; i++)
                {
                    var continuationItem = TryResolveContinuationInstance(items[i], context);
                    if (continuationItem != null)
                    {
                        result.Add(continuationItem);
                    }

                    return result.ToArray();
                }
            }

            var resolvedContinuation = TryResolveContinuationInstance(continuation, context);
            if (resolvedContinuation != null)
            {
                return new[] { resolvedContinuation };
            }

            return Array.Empty<ClrInstance>();
        }

        public static ClrInstance? TryResolveContinuationInstance(ClrInstance continuation, CausalityContext context)
        {
            if (continuation.IsOfType(context.StandardTaskContinuationType))
            {
                return continuation["m_task"].Instance;
            }

            if (context.TaskCompletionSourceIndex.ContainsType(continuation.Type))
            {
                return continuation["m_task"].Instance;
            }

            if (continuation.IsCompletedTaskContinuation(context))
            {
                // Continuation is a special sentinel instance that indicates that the task is completed.
                return null;
            }

            if (continuation.IsOfType(typeof(Action), context))
            {
                return TryResolveContinuationForAction(continuation, context);
            }

            if (context.AwaitTaskContinuationIndex.ContainsType(continuation.Type) || continuation.IsOfType(context.Registry.TaskIndex))
            {
                return TryResolveContinuationForAction(continuation["m_action"].Instance, context);
            }

            // Need to compare by name since GetTypeByName does not work for the generic type during initialization
            if (continuation.Type?.Name == "System.Collections.Generic.List<System.Object>")
            {
                Contract.Assert(false, "Please call 'TryResolveContinuations' for a list of continuations.");
            }

            return null;
        }

        public static ClrInstance? TryResolveContinuationForAction(ClrInstance instance, CausalityContext context)
        {
            Contract.Requires(instance.IsOfType(typeof(Action), context), $"A given instance should be of type System.Action, but was '{instance.Type.TypeToString(context.Registry)}'");

            var continuation = instance;
            var actionTarget = continuation["_target"].Instance;
            if (actionTarget.IsOfType(context.ContinuationWrapperType))
            {
                // Do we need to look at the m_innerTask field as well here?
                return actionTarget["m_continuation"].Instance;
            }

            // If the action points to a closure, it is possible that the closure
            // is responsible for setting the result of a task completion source.
            // There is no simple way to detect whether this is the case or not, so we will add the "edge" unconditionally.
            if (actionTarget.Type.IsClosure())
            {
                foreach (var field in actionTarget.Fields)
                {
                    if (context.TaskCompletionSourceIndex.ContainsType(field.Instance.Type))
                    {
                        return field.Instance;
                    }
                }
            }

            // m_stateMachine field is defined in AsyncMethodBuilderCore and in MoveNextRunner.
            var stateMachine = actionTarget.TryGetFieldValue("m_stateMachine")?.Instance;
            if (stateMachine.IsNull())
            {
                return null;
            }

            return stateMachine;
        }
    }
}
