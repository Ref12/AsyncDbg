// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using AsyncCausalityDebuggerNew;

#nullable enable

namespace AsyncDbgCore.New
{
    public class TaskContinuation
    {
        private readonly ClrInstance _continuation;
        private readonly TypesRegistry _typesRegistry;

        /// <inheritdoc />
        public TaskContinuation(ClrInstance continuation, TypesRegistry typesRegistry)
        {
            _continuation = continuation;
            _typesRegistry = typesRegistry ?? throw new ArgumentNullException(nameof(typesRegistry));
        }

        public bool IsNull => _continuation.IsNull();

        public IReadOnlyList<TaskContinuation> GetContinuations()
        {
            if (IsNull)
            {
                return Array.Empty<TaskContinuation>();
            }

            return GetContinuationsCore(_continuation).Where(t => t.IsNotNull()).Select(i => new TaskContinuation(i, _typesRegistry)).ToList();
        }

        private IEnumerable<ClrInstance> GetContinuationsCore(ClrInstance continuation)
        {
            if (continuation.Type == null)
            {
                yield break;
            }

            //
            // The current task is completed and it is an end of an async stack trace.
            //
            if (continuation.IsCompletedTaskContinuation(_typesRegistry))
            {
                yield break;
            }

            //
            // Continuation for a simple await.
            //
            if (_continuation.IsOfType(_typesRegistry.AwaitTaskContinuationIndex))
            {
                yield return continuation["m_action"].Instance;
                yield break;
            }

            //
            // Continuation is actually a list of continuations.
            //
            if (continuation.IsListOfObjects())
            {
                foreach (var item in _continuation.EnumerateListOfObjects())
                {
                    foreach (var c in GetContinuationsCore(item))
                    {
                        yield return c;
                    }
                }

                yield break;
            }

            //
            // Continuation is 'ContinueWith' or 'TaskCompletionSource<T>'
            //
            if (continuation.IsOfType(_typesRegistry.StandardTaskContinuationType) ||
                continuation.IsOfType(_typesRegistry.TaskCompletionSourceIndex))
            {
                yield return continuation["m_task"].Instance;
                yield break;
            }

            //
            // The most complicated case: a continuation is System.Action
            //
            Contract.AssertNotNull(_continuation.Type, "C# compiler should've detected that _continuation.Type is not nullable here!");
            if (_typesRegistry.IsInstanceOfType(_continuation.Type, typeof(Action)))
            {
                // This is a simple continuation created by async/await
                var actionTarget = continuation["_target"].Instance;

                if (actionTarget.IsOfType(_typesRegistry.ContinuationWrapperType))
                {
                    // Do we need to look at the m_innerTask field as well here?
                    yield return actionTarget["m_continuation"].Instance;
                    yield break;
                }

                // m_stateMachine field is defined in AsyncMethodBuilderCore and in MoveNextRunner.
                var stateMachine = actionTarget["m_stateMachine"].Instance;
                if (stateMachine.IsNull())
                {
                    yield break;
                }

                if (continuation.TryGetFieldValue("<>t__builder", out var asyncMethodBuilderField))
                {
                    var asyncMethodBuild = asyncMethodBuilderField.Instance;
                    if (asyncMethodBuild.TryGetFieldValue("m_builder",
                        out var innerAsyncMethodBuilderField))
                    {
                        asyncMethodBuild = innerAsyncMethodBuilderField.Instance;
                    }

                    if (asyncMethodBuild.TryGetFieldValue("m_task", out var asyncMethodBuilderTaskField))
                    {
                        yield return asyncMethodBuilderTaskField.Instance;
                    }
                    else if (_typesRegistry.IsTask(asyncMethodBuild.Type))
                    {
                        yield return asyncMethodBuild;
                    }
                    else
                    {
                    }
                }
                else
                {
                }
            }
        }
    }
}
