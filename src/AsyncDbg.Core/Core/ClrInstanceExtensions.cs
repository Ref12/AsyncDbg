// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AsyncCausalityDebuggerNew;

namespace AsyncDbgCore.New
{
    #nullable enable

    public static class ClrInstanceExtensions
    {
        public static bool IsOfType(this ClrInstance instance, Type type, CausalityContext context)
        {
            return instance.IsOfType(type, context.Registry);
        }

        public static bool IsOfType(this ClrInstance instance, Type type, TypesRegistry registry)
        {
            if (instance.Type == null)
            {
                return false;
            }

            return registry.IsInstanceOfType(instance.Type, type);
        }

        public static bool IsOfType(this ClrInstance instance, AsyncCausalityDebuggerNew.TypeIndex typeIndex)
        {
            if (instance.Type == null)
            {
                return false;
            }

            return typeIndex.ContainsType(instance.Type);
        }

        public static bool IsTaskLike(this ClrInstance instance, TypesRegistry registry)
        {
            if (instance.IsOfType(typeof(Task), registry))
            {
                return true;
            }

            // Add support for ValueTask and other task-like types.
            return false;
        }

        public static bool IsNotNull(this ClrInstance instance)
        {
            return instance != null && !instance.IsNull;
        }

        public static bool IsNull(this ClrInstance instance) => !IsNotNull(instance);

        public static bool IsTaskWhenAll(this ClrInstance instance, CausalityContext context)
        {
            return context.Registry.IsTaskWhenAll(instance);
        }

        /// <summary>
        /// Returns true if a given <paramref name="continuation"/> instance is a special continuation object that is used as m_continuationObject field when the task is finished.
        /// </summary>
        /// <remarks>
        /// This method helps to find an end of a async stacks when a task's continuation is a special task completion sentinel.
        /// See http://index/?query=s_taskCompletionSentinel&rightProject=mscorlib&file=system%5Cthreading%5CTasks%5CTask.cs&line=213 for more details.
        /// </remarks>
        public static bool IsCompletedTaskContinuation(this ClrInstance continuation, CausalityContext context)
        {
            return context.Registry.IsTaskCompletionSentinel(continuation);
        }

        public static bool IsCompletedTaskContinuation(this ClrInstance continuation, TypesRegistry registry)
        {
            return registry.IsTaskCompletionSentinel(continuation);
        }

        public static bool IsListOfObjects(this ClrInstance instance)
        {
            // Need to compare by name since GetTypeByName does not work for the generic type during initialization
            return instance.Type?.Name == "System.Collections.Generic.List<System.Object>";
        }

        public static IEnumerable<ClrInstance> EnumerateListOfObjects(this ClrInstance instance)
        {
            if (!instance.IsListOfObjects())
            {
                throw new ArgumentException($"'instance' should be of type 'List<object>' but was '{instance.Type}'");
            }

            var size = (int)instance.GetField("_size").Instance.Value;
            var items = instance.GetField("_items").Instance.Items;
            for (int i = 0; i < size; i++)
            {
                yield return items[i];
            }
        }
    }
}
