using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using AsyncDbgCore.Core;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace AsyncDbg.Core
{
    /// <todoc />
    //[DebuggerTypeProxy(typeof(ClrInstanceDynamicProxy))]
    public class ClrInstance
    {
        private ClrFieldValue[]? _fields;
        private ClrInstance[]? _items;

        private readonly ClrType? _primitiveTypeOptional;
        private readonly ClrType? _originalType;

        /// <summary>
        /// True if the instance is embedded in another instance (like a struct in an array).
        /// </summary>
        private readonly bool _interior;

        /// <nodoc />
        public ClrHeap Heap { get; }

        /// <summary>
        /// Returns optional primitive value of the instance.
        /// </summary>
        public object? ValueOrDefault { get; } // Null if instance is null.

        /// <summary>
        /// Returns primitive value of the instance.
        /// </summary>
        public object Value
        {
            get
            {
                var result = ValueOrDefault;

                Contract.AssertNotNull(result);

                return result;
            }
        }

        /// <summary>
        /// Gets the type of the instance (null if the value is null).
        /// </summary>
        public ClrType? Type => _primitiveTypeOptional ?? _originalType;

        /// <summary>
        /// Returns an address of an object (not-null for reference types).
        /// </summary>
        public ulong? ObjectAddress => IsObject ? (ulong?)ValueOrDefault : null;

        /// <summary>
        /// Returns an instance address without an object header.
        /// </summary>
        public ulong AddressWithoutHeader
        {
            get
            {
                Contract.Requires(IsObject, "Cannot get an address for a value.");

                Contract.AssertNotNull(ObjectAddress);
                return _interior ? ObjectAddress.Value : ObjectAddress.Value + (ulong)Heap.PointerSize;
            }
        }

        public bool IsNull => ValueOrDefault == null || ValueOrDefault.Equals(0UL);

        public bool IsObject => IsNull || Type is null || !Type.IsIntrinsic();

        public bool IsObjectRefernece => IsNull || Type?.IsObjectReference == true;

        public bool IsArray => Type?.IsArray == true;

        public ClrFieldValue this[string fieldName] => GetField(fieldName);

        public ClrFieldValue GetField(string fieldName)
        {
            return Contract.AssertNotNull(TryGetFieldValue(fieldName), $"The field '{fieldName}' is not found.");
        }

        public ClrFieldValue[] Fields => _fields = _fields ?? ComputeFields();

        public ClrInstance[] Items => _items = _items ?? ComputeItems();

        internal ClrInstance(ClrHeap heap, object? value, ClrType? type, bool interior)
        {
            if (value != null && type == null)
            {
                throw new ArgumentNullException(nameof(type), $"Non null value must have a type.");
            }

            Heap = heap;
            ValueOrDefault = value;
            _originalType = type;
            _interior = interior;
            (ValueOrDefault, _primitiveTypeOptional) = InitializeCore(type);
        }

        public static ClrInstance CreateInstance(ClrHeap heap, object? value, ClrType? type)
        {
            if (value is ulong address && address == 0)
            {
                // this is null.
                return new ClrInstance(heap, null, type, interior: false);
            }

            return new ClrInstance(heap, value, type, interior: false);
        }

        public static ClrInstance CreateInterior(ClrHeap heap, object value, ClrType type)
        {
            if (value is ulong address && address == 0)
            {
                // this is null.
                return new ClrInstance(heap, null, type, interior: false);
            }

            return new ClrInstance(heap, value, type, interior: true);
        }

        public static ClrInstance CreateInstance(ClrHeap heap, ulong address, ClrType? type = null)
        {
            type = type ?? heap.GetObjectType(address);

            if (address == 0)
            {
                // this is null.
                return CreateInstance(heap, null, type);
            }

            if (type == null)
            {
                throw new InvalidOperationException($"It seems that address '{address}' does not points to a valid managed object.");
            }

            return new ClrInstance(heap, address, type, interior: false);
        }

        private (object? value, ClrType? primitiveTypeOptional) InitializeCore(ClrType? originalType)
        {
            var value = ValueOrDefault;
            var type = originalType;

            if (type?.IsEnum == true)
            {
                var info = ClrEnumTypeInfo.GetOrCreateEnumTypeInfo(type);
                if (info.TryGetEnumByValue(ValueOrDefault, out var e))
                {
                    value = e;
                }
                else
                {
                    var values = new List<ClrEnumValue>();
                    var bits = Convert.ToInt64(ValueOrDefault);
                    foreach (var enumValue in info.Values)
                    {
                        if ((enumValue.Value & bits) == enumValue.Value)
                        {
                            values.Add(enumValue);
                        }
                    }

                    value = new ClrEnumValue(bits, values);
                }
            }

            var isObject = IsObject;

            ClrType? primitiveTypeOptional = null;
            if (!isObject || type?.IsValueClass == true)
            {
                Contract.AssertNotNull(type);

                primitiveTypeOptional = type;
                if (!isObject)
                {
                    if (value is ulong address)
                    {
                        value = type.GetValue(address);
                    }
                }
            }

            return (value, primitiveTypeOptional);
        }

        public bool IsOfType(ClrType type)
        {
            // What about subtyping?
            return ClrTypeEqualityComparer.Instance.Equals(Type, type);
        }

        public ClrFieldValue? TryGetFieldValue(string fieldName)
        {
            return TryGetFieldValueCore(fieldName, ignoreCase: false)
                ?? (fieldName.StartsWith("m_") ? TryGetFieldValueCore("_" + fieldName.Substring(2), ignoreCase: true) : null);
        }

        public ClrFieldValue? TryGetFieldValueCore(string fieldName, bool ignoreCase)
        {
            var propertyName = $"<{fieldName}>k__BackingField";
            var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            foreach (var field in Fields)
            {
                if (comparer.Equals(field.Field.Name, fieldName) || comparer.Equals(field.Field.Name, propertyName))
                {
                    return field;
                }
            }

            return null;
        }

        public bool TryGetFieldValue(string fieldName, [NotNullWhen(true)]out ClrFieldValue? fieldValue)
        {
            fieldValue = TryGetFieldValue(fieldName);
            return fieldValue != null;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToString(registry: null);
        }

        public static readonly Regex AddressRegex = new Regex(@"\(Addr:\d+\)");

        public string ToString(TypesRegistry? registry)
        {
            // For some weird reason String is not an object!
            if (Type?.IsString == true)
            {
                return $"\"{ValueOrDefault}\"";
            }

            if (IsNull)
            {
                return "null";
            }

            if (IsObject)
            {
                string? typeName = registry != null ? Type?.TypeToString(registry) : Type?.Name;
                return $"{typeName} (Addr:{ValueOrDefault})";
            }

            if (ValueOrDefault == null)
            {
                return "<NO VALUE>";
            }

            Contract.AssertNotNull(Type);

            var suffix = string.Empty;
            if (Type.IsOfTypes(typeof(long), typeof(ulong)))
            {
                suffix = "L";
            }
            else if (Type.IsOfTypes(typeof(double), typeof(float)))
            {
                suffix = "f";
            }

            return $"{ValueOrDefault}{suffix}";
        }

        private ClrInstance[] ComputeItems()
        {
            if (!IsArray)
            {
                return Array.Empty<ClrInstance>();
            }

            Contract.AssertNotNull(Type, "Arrays should always have Type != null.");
            Contract.AssertNotNull(ObjectAddress);

            var address = ObjectAddress.Value;
            var length = Type.GetArrayLength(address);
            var items = new ClrInstance[length];
            var elementType = Type.ComponentType;
            for (var i = 0; i < length; i++)
            {
                var tmp = GetElementAt(i);

                if (!(tmp is ClrInstance))
                {
                    tmp = CreateInterior(Heap, tmp, elementType);
                }

                items[i] = (ClrInstance)tmp;
                //var value = Type.GetArrayElementValue(address, i);
                //items[i] = CreateItemInstance(value, elementType, i);
            }

            return items;
        }

        private object GetElementAt(int index)
        {
            Contract.Requires(!IsNull, "!IsNull");
            Contract.AssertNotNull(Type);
            Contract.AssertNotNull(ObjectAddress);

            if (Type.ComponentType.HasSimpleValue)
            {
                var result = Type.GetArrayElementValue(ObjectAddress.Value, index);

                if (IsReference(result, Type.ComponentType))
                {
                    var address = (ulong)result;
                    return CreateInterior(Heap, address, Heap.GetObjectType(address));
                }

                return Type.GetArrayElementValue(ObjectAddress.Value, index);
            }

            return CreateInterior(Heap, Type.GetArrayElementAddress(ObjectAddress.Value, index), Type.ComponentType);
        }

        private static bool IsReference(object result, ClrType type)
        {
            return result != null && !(result is string) && type.IsObjectReference;
        }

        private ClrFieldValue[] ComputeFields()
        {
            if (IsNull || Type?.IsStringOrPrimitive() == true)
            {
                return Array.Empty<ClrFieldValue>();
            }

            var offsets = new HashSet<int>();
            var fields = new List<ClrFieldValue>();

            Contract.AssertNotNull(Type);
            foreach (var typeField in Type.EnumerateBaseTypesAndSelf().SelectMany(t => t.Fields))
            {
                if (typeField.Name == "<>u__1")
                {
                    // TODO: what this is all about?
                    // TODO: Maybe not an issue anymore?
                    //continue;
                }

                if (offsets.Add(typeField.Offset))
                {
                    fields.Add(ClrFieldValue.Create(typeField, this, _interior));
                }
            }

            return fields.Where(f => f != null).ToArray();
        }
    }
}
