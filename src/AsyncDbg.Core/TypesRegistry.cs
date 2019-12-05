using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncDbg.Core;
using Microsoft.Diagnostics.Runtime;

#nullable enable
#nullable enable annotations

namespace AsyncDbg
{
    public class TypesRegistry
    {
        private ClrHeap _heap => _heapContext;

        private readonly HeapContext _heapContext;
        private readonly ConcurrentDictionary<string, ClrType?> _fullNameToClrTypeMap = new ConcurrentDictionary<string, ClrType?>();

        // s_taskCompletionSentinel instances used to determine that a task is completed.
        private readonly HashSet<ClrInstance> _taskCompletionSentinels = new HashSet<ClrInstance>(ClrInstanceAddressComparer.Instance);

        private readonly HashSet<TypeIndex> _typeIndices = new HashSet<TypeIndex>();

        private readonly HashSet<ClrType> _whenAllTypes;
        private readonly ClrType _unwrapPromise;

        public TypeIndex TaskIndex { get; }
        public TypeIndex SynchronizationContextIndex { get; }

        public TypeIndex ValueTaskIndex { get; }
        public TypeIndex ManualResetEventIndex { get; }
        public TypeIndex ManualResetEventSlimIndex { get; }
        public TypeIndex AwaitTaskContinuationIndex { get; }
        public TypeIndex ThreadIndex { get; }
        public TypeIndex TaskCompletionSourceIndex { get; }
        public TypeIndex SemaphoreSlimIndex { get; }

        public ClrType ContinuationWrapperType { get; }
        public ClrType AsyncTaskMethodBuilderType { get; }
        public ClrType StandardTaskContinuationType { get; }
        public ClrType IAsyncStateMachineType { get; }
        public TypeIndex IAsyncStateMachineTypeIndex { get; }

        private TypesRegistry(HeapContext heapContext)
        {
            _heapContext = heapContext;
            ContinuationWrapperType = heapContext.DefaultHeap.GetTypeByName("System.Runtime.CompilerServices.AsyncMethodBuilderCore+ContinuationWrapper");
            AsyncTaskMethodBuilderType = heapContext.DefaultHeap.GetTypeByName("System.Runtime.CompilerServices.AsyncTaskMethodBuilder");

            StandardTaskContinuationType = Contract.AssertNotNull(
                GetClrTypeByFullName("System.Threading.Tasks.StandardTaskContinuation"), "Should not be null"); // This is ContinueWith continuation
            IAsyncStateMachineType = Contract.AssertNotNull(
                GetClrTypeByFullName("System.Runtime.CompilerServices.IAsyncStateMachine"), "Should not be null");

            _unwrapPromise = Contract.AssertNotNull(
                GetClrTypeByFullName("System.Threading.Tasks.UnwrapPromise"), "ShouldNotBeNull");

            TaskIndex = CreateTypeIndex(NodeKind.Task);
            ValueTaskIndex = CreateTypeIndex(NodeKind.ValueTask, isOptional: true); // value task types can be missing depending on framework version.
            SynchronizationContextIndex = CreateTypeIndex(NodeKind.SynchronizationContext);
            ManualResetEventSlimIndex = CreateTypeIndex(NodeKind.ManualResetEventSlim);
            ManualResetEventIndex = CreateTypeIndex(NodeKind.ManualResetEvent);
            AwaitTaskContinuationIndex = CreateTypeIndex(NodeKind.AwaitTaskContinuation);

            ThreadIndex = CreateTypeIndex(NodeKind.Thread);
            TaskCompletionSourceIndex = CreateTypeIndex(NodeKind.TaskCompletionSource);
            SemaphoreSlimIndex = CreateTypeIndex(NodeKind.SemaphoreSlim);
            IAsyncStateMachineTypeIndex = CreateTypeIndex(NodeKind.AsyncStateMachine);

            FillTaskSentinels(_taskCompletionSentinels);

            FillDerivedTypesFor(_typeIndices);

            // There are two "WhenAllPromise" types - one for Task<T>.WhenAll and another one for Task.WhenAll
            _whenAllTypes = TaskIndex.GetTypesByFullName("System.Threading.Tasks.Task+WhenAllPromise").ToHashSet(ClrTypeEqualityComparer.Instance);
        }

        public static TypesRegistry Create(HeapContext heap)
        {
            var result = new TypesRegistry(heap);
            result.Populate();
            return result;
        }

        public IEnumerable<(ClrInstance instance, NodeKind typeKind)> EnumerateRegistry()
        {
            return _typeIndices.SelectMany(index => index.Instances.Select(i => (i, index.Kind)));
        }

        public bool IsTask(ClrType? type) => type != null && TaskIndex.ContainsType(type);

        public bool IsUnwrapPromise(ClrInstance task) => ClrTypeEqualityComparer.Instance.Equals(task?.Type, _unwrapPromise);

        public bool IsTaskCompletionSource(ClrType? type) => type != null && (TaskCompletionSourceIndex.ContainsType(type) || type.Name.Contains("TaskSourceSlim"));

        public bool IsAsyncStateMachine(ClrType? type) => type != null && type.Interfaces.Any(i => i.Name == IAsyncStateMachineType.Name);

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

        public bool IsContinuationTaskFromTask(ClrInstance c)
        {
            var type = c.Type;
            return type != null && type.Name.Contains("ContinuationTaskFromTask");
        }

        private void Populate()
        {
            Console.WriteLine("Analyzing the heap...");
            var sw = Stopwatch.StartNew();

            var counter = 0;

            // failing to cast to IDebugSystemObjects3 with parallel (race condition?)
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 1
            };

            Parallel.For(0, _heap.Segments.Count, parallelOptions, segmentIndex =>
            {
                var heap = _heapContext.CreateHeap();
                var segment = heap.Segments[segmentIndex];

                foreach (var obj in segment.EnumerateObjectAddresses())
                {
                    var counterValue = Interlocked.Increment(ref counter);
                    if (counterValue % 100000 == 0)
                    {
                        Console.WriteLine($"Analyzed {counterValue} objects");
                    }

                    var type = heap.GetObjectType(obj);

                    foreach (var typeIndex in _typeIndices)
                    {
                        if (typeIndex.ContainsType(type))
                        {
                            var instance = ClrInstance.CreateInstance(heap, obj, type);
                            lock (typeIndex)
                            {
                                typeIndex.AddInstance(instance);
                            }

                            break;
                        }
                    }
                }
            });

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
            var taskType = GetClrTypeFor(typeof(Task))!;

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

        private ClrType? GetClrTypeFor(Type systemType)
        {
            var fullName = systemType.FullName ?? throw new InvalidOperationException("Cannot find a full name for a given type.");

            // Removing '`' from the name, because ClrMd doesn't recognize them.
            if (fullName.Contains("`"))
            {
                fullName = fullName.Substring(0, fullName.IndexOf("`"));
            }

            return GetClrTypeByFullName(fullName);
        }

        private ClrType? GetClrTypeByFullName(string fullName)
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

        private ClrType? GetClrTypeFor(NodeKind kind)
        {
            var result = kind switch
            {
                NodeKind.Task => GetClrTypeFor(typeof(Task)),

                NodeKind.ValueTask => GetClrTypeByFullName("System.Threading.Tasks.ValueTask"),
                NodeKind.ManualResetEventSlim => GetClrTypeFor(typeof(ManualResetEventSlim)),
                NodeKind.ManualResetEvent => GetClrTypeFor(typeof(ManualResetEvent)),
                NodeKind.AwaitTaskContinuation => GetClrTypeByFullName("System.Threading.Tasks.AwaitTaskContinuation"),
                NodeKind.SemaphoreSlim => GetClrTypeFor(typeof(SemaphoreSlim)),
                NodeKind.Thread => GetClrTypeFor(typeof(Thread)),
                NodeKind.BlockingObject => throw new NotSupportedException(), // todoc
                NodeKind.TaskCompletionSource => GetClrTypeFor(typeof(TaskCompletionSource<>)),
                NodeKind.AsyncStateMachine => GetClrTypeByFullName("System.Runtime.CompilerServices.IAsyncStateMachine"),
                NodeKind.SynchronizationContext => GetClrTypeByFullName("System.Threading.SynchronizationContext"),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };

            return result;
        }

        private TypeIndex CreateTypeIndex(NodeKind kind, bool addToIndex = true, bool isOptional = false)
        {
            var clrType = GetClrTypeFor(kind);
            if (!isOptional)
            {
                Contract.AssertNotNull(clrType, $"Can't find a type for non-optional node kind '{kind}'.");
            }

            var result = new TypeIndex(GetClrTypeFor(kind), kind);

            if (addToIndex)
            {
                _typeIndices.Add(result);
            }

            return result;
        }
    }
}
