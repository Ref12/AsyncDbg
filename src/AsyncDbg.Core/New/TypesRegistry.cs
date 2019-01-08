using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncDbgCore.New;
using Microsoft.Diagnostics.Runtime;

namespace AsyncCausalityDebuggerNew
{
    #nullable enable

    public class TypesRegistry
    {
        private readonly ClrHeap _heap;
        private readonly ConcurrentDictionary<string, ClrType> _fullNameToClrTypeMap = new ConcurrentDictionary<string, ClrType>();

        // s_taskCompletionSentinel instances used to determine that task is completed.
        private readonly HashSet<ClrInstance> _taskCompletionSentinels = new HashSet<ClrInstance>(ClrInstanceAddressComparer.Instance);

        private readonly HashSet<TypeIndex> _typeIndices = new HashSet<TypeIndex>();

        private readonly HashSet<ClrType> _whenAllTypes;

        public TypeIndex TaskIndex { get; }
        public TypeIndex ValueTaskIndex { get; }
        public TypeIndex ManualResetEventIndex { get; }
        public TypeIndex ManualResetEventSlimIndex { get; }
        public TypeIndex AwaitTaskContinuationIndex { get; }
        public TypeIndex ThreadIndex { get; }
        public TypeIndex TaskCompletionSourceIndex { get; }
        public TypeIndex SemaphoreSlimIndex { get; }
        public TypeIndex SemaphoreWrapperIndex { get; }

        public ClrType ContinuationWrapperType { get; }
        public ClrType AsyncTaskMethodBuilderType { get; }
        public ClrType StandardTaskContinuationType { get; }

        private TypesRegistry(ClrHeap heap)
        {
            _heap = heap;
            ContinuationWrapperType = heap.GetTypeByName("System.Runtime.CompilerServices.AsyncMethodBuilderCore+ContinuationWrapper");
            AsyncTaskMethodBuilderType = heap.GetTypeByName("System.Runtime.CompilerServices.AsyncTaskMethodBuilder");

            StandardTaskContinuationType =
                GetClrTypeByFullName("System.Threading.Tasks.StandardTaskContinuation"); // This is ContinueWith continuation

            TaskIndex = CreatTypeIndex(NodeKind.Task);
            ValueTaskIndex = CreatTypeIndex(NodeKind.ValueTask);
            ManualResetEventSlimIndex = CreatTypeIndex(NodeKind.ManualResetEventSlim);
            ManualResetEventIndex = CreatTypeIndex(NodeKind.ManualResetEvent);
            AwaitTaskContinuationIndex = CreatTypeIndex(NodeKind.AwaitTaskContinuation);

            ThreadIndex = CreatTypeIndex(NodeKind.Thread);
            TaskCompletionSourceIndex = CreatTypeIndex(NodeKind.TaskCompletionSource);
            SemaphoreSlimIndex = CreatTypeIndex(NodeKind.SemaphoreSlim, addToIndex: false);
            SemaphoreWrapperIndex = CreatTypeIndex(NodeKind.SemaphoreWrapper, addToIndex: false);

            FillTaskSentinels(_taskCompletionSentinels);

            FillDerivedTypesFor(_typeIndices);

            // There are two "WhenAllPromise" types - one for Task<T>.WhenAll and another one for Task.WhenAll
            _whenAllTypes = TaskIndex.GetTypesByFullName("System.Threading.Tasks.Task+WhenAllPromise").ToHashSet(ClrTypeEqualityComparer.Instance);
        }

        public static TypesRegistry Create(ClrHeap heap)
        {
            var result = new TypesRegistry(heap);
            result.Populate();
            return result;
        }

        public IEnumerable<(ClrInstance instance, NodeKind typeKind)> EnumerateRegistry()
        {
            return _typeIndices.SelectMany(index => index.Instances.Select(i => (i, index.Kind)));
        }

        public bool IsTask(ClrType type) => TaskIndex.ContainsType(type);

        public bool IsTaskCompletionSource(ClrType type) => TaskCompletionSourceIndex.ContainsType(type);

        public bool IsSemaphoreWrapper(ClrType type) => SemaphoreWrapperIndex.ContainsType(type);

        /// <summary>
        /// Returns true if a given <paramref name="continuation"/> is a special Task.s_taskCompletionSentinel instance.
        /// </summary>
        public bool IsTaskCompletionSentinel(ClrInstance continuation) => _taskCompletionSentinels.Contains(continuation);

        public bool IsTaskWhenAll(ClrInstance task)
        {
            return task?.Type != null && _whenAllTypes.Contains(task.Type);
        }

        public bool IsInstanceOfType(ClrType type, Type systemType)
        {
            type = type ?? throw new ArgumentNullException(nameof(type));
            systemType = systemType ?? throw new ArgumentNullException(nameof(type));

            var clrType = GetClrTypeFor(systemType);

            // Does it make sense to add our own Type class?
            if (ClrTypeEqualityComparer.Instance.Equals(type, clrType))
            {
                return true;
            }

            if (type.IsSealed)
            {
                // No need to check derived types!
                return false;
            }

            // Look for derived types.
            return false;
        }

        private void Populate()
        {
            Console.WriteLine("Analyzing the heap...");
            var sw = Stopwatch.StartNew();

            int counter = 0;
            foreach (var obj in _heap.EnumerateObjectAddresses())
            {
                counter++;
                if (counter % 10000 == 0)
                {
                    Console.WriteLine($"Analyzed {counter} objects");
                }

                var type = _heap.GetObjectType(obj);

                foreach (var typeIndex in _typeIndices)
                {
                    if (typeIndex.ContainsType(type))
                    {
                        var instance = ClrInstance.CreateInstance(_heap, obj, type);
                        typeIndex.AddInstance(instance);

                        break;
                    }
                }
            }

            Console.WriteLine($"Heap analysis is done by {sw.ElapsedMilliseconds}ms.");
        }

        private void FillDerivedTypesFor(HashSet<TypeIndex> types)
        {
            foreach (var type in _heap.EnumerateTypes())
            {
                if (type.BaseType != null)
                {
                    foreach (var typeIndex in types)
                    {
                        typeIndex.AddIfDerived(type);
                    }
                }
            }
        }

        private void FillTaskSentinels(HashSet<ClrInstance> sentinels)
        {
            var taskType = GetClrTypeFor(typeof(Task));

            var taskCompletionSentinelField =
                taskType.GetStaticFieldByName("s_taskCompletionSentinel") ??
                throw new InvalidOperationException("Could not find s_taskCompletionSentinel field in Task type.");

            foreach (var appDomain in _heap.Runtime.AppDomains)
            {
                var addressInstance = taskCompletionSentinelField.GetValue(appDomain);
                if (addressInstance != null)
                {
                    var address = (ulong)addressInstance;
                    var instance = ClrInstance.CreateInstance(_heap, address);
                    sentinels.Add(instance);
                }
            }
        }

        private ClrType GetClrTypeFor(Type systemType)
        {
            var fullName = systemType.FullName ?? throw new InvalidOperationException("Cannot find a full name for a given type.");

            // Removing '`' from the name, because ClrMd doesn't recognize them.
            if (fullName.Contains("`"))
            {
                fullName = fullName.Substring(0, fullName.IndexOf("`"));
            }

            return GetClrTypeByFullName(fullName);
        }

        private ClrType GetClrTypeByFullName(string fullName)
        {
            return _fullNameToClrTypeMap.GetOrAdd(fullName, fullTypeName =>
            {
                // In some cases the type names for base type and the current type are the same,
                // like for Task<T> and Task.
                // Unfortunately, if we call Heap.GetTypeByName("System.Threading.Tasks.Task") will get the generic task not a non-generic one.
                // This check allows us to get the right type.
                var type = _heap.GetTypeByName(fullTypeName);
                if (type != null && type.BaseType?.Name == fullName)
                {
                    return type.BaseType;
                }

                // The result can be null.
                return type;
            });
        }

        private ClrType GetClrTypeFor(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.Task:
                    return GetClrTypeFor(typeof(Task));

                case NodeKind.ValueTask:
                    return GetClrTypeByFullName("System.Threading.Tasks.ValueTask");
                case NodeKind.ManualResetEventSlim:
                    return GetClrTypeFor(typeof(ManualResetEventSlim));
                case NodeKind.ManualResetEvent:
                    return GetClrTypeFor(typeof(ManualResetEvent));
                case NodeKind.AwaitTaskContinuation:
                    return GetClrTypeByFullName("System.Threading.Tasks.AwaitTaskContinuation");
                case NodeKind.SemaphoreSlim:
                    return GetClrTypeFor(typeof(SemaphoreSlim));
                case NodeKind.SemaphoreWrapper:
                    return GetClrTypeByFullName("ContentStoreInterfaces.Synchronization.SemaphoreSlimToken");
                case NodeKind.Thread:
                    return GetClrTypeFor(typeof(Thread));
                case NodeKind.BlockingObject:
                    throw new NotSupportedException(); // todoc
                case NodeKind.TaskCompletionSource:
                    return GetClrTypeFor(typeof(TaskCompletionSource<>));
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private TypeIndex CreatTypeIndex(NodeKind kind, bool addToIndex = true)
        {
            var result = new TypeIndex(GetClrTypeFor(kind), kind);

            if (addToIndex)
            {
                _typeIndices.Add(result);
            }

            return result;
        }
    }
}
