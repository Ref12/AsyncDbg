// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace AsyncDbgCore.Core
{
    /// <summary>
    /// Represents an object instance with a type and a potential scalar value.
    /// </summary>
    //[DebuggerTypeProxy(typeof(ClrInstanceDynamicProxy))]
    public sealed class ClrInstance
    {
        private readonly Lazy<ClrFieldValue[]> _fields;

        /// <summary>
        /// Optional primitive value.
        /// </summary>
        public object Value { get; }

        /// <summary>
        /// Address of the object in memory.
        /// </summary>
        public ulong ObjectAddress { get; }

        /// <summary>
        /// Address of the object excluding an object header.
        /// </summary>
        /// TODO: but this should be 2 * PointerSize!
        public ulong AddressWithoutHeader => Interior ? ObjectAddress : ObjectAddress + (ulong)Heap.PointerSize;

        /// <nodoc />
        internal ClrHeap Heap { get; }

        /// <summary>
        /// The type of an instance.
        /// </summary>
        public ClrType Type { get; }

        /// <summary>
        /// True if the instance is part of another instance.
        /// </summary>
        public bool Interior { get; } = false;

        /// <summary>
        /// Gets all the instance fields.
        /// </summary>
        public ClrFieldValue[] Fields => _fields.Value;

        /// <summary>
        /// Gets the field instance by <paramref name="fieldName"/>.
        /// </summary>
        public ClrInstance this[string fieldName] => ClrFieldValue.Create(Type.GetFieldByName(fieldName), this).Instance;//Fields.FirstOrDefault(f => f.Field.Name == fieldName)?.Instance;


        public ClrInstance TryGetField(string fieldName)
        {
            var field = Type.GetFieldByName(fieldName);
            if (field == null)
            {
                return null;
            }

            return ClrFieldValue.Create(field, this).Instance;
        }

        /// <summary>
        /// Returns an actual object's value.
        /// </summary>
        public object GetValue()
        {
            return Value ?? ObjectAddress;
        }

        //public bool IsNull => IsObject && ObjectAddress == 0;
        public bool IsNull => ObjectAddress == 0;

        public bool IsObject { get; }

        /// <inheritdoc />
        internal ClrInstance(object value, ulong address, ClrHeap heap, ClrType type, bool interior)
        {

            Value = value ?? address;
            ObjectAddress = address;

            if (value is UInt64 uvalue && uvalue != address)
            {

            }

            Heap = heap ?? throw new ArgumentNullException(nameof(heap));
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Interior = interior;
            _fields = new Lazy<ClrFieldValue[]>(() => Type.Fields.Select(f => ClrFieldValue.Create(f, this)).ToArray());

            IsObject = !(type.IsPrimitive || type.IsString);

            if (type.IsEnum)
            {
                Value = CreateEnumValue(type);
            }
        }

        private ClrEnumValue CreateEnumValue(ClrType type)
        {
            var info = ClrEnumTypeInfo.Create(type);
            if (info.ValuesByValue.TryGetValue(Value, out var e))
            {
                return e;
            }

            var values = new List<ClrEnumValue>();
            long bits = Convert.ToInt64(Value);
            foreach (var enumValue in info.ValuesByValue.Values)
            {
                if ((enumValue.Value & bits) == enumValue.Value)
                {
                    values.Add(enumValue);
                }
            }

            return new ClrEnumValue(bits, values);
        }

        /// <inheritdoc />
        internal ClrInstance(ulong address, ClrHeap heap, ClrType type)
            : this(null, address, heap, type, interior: false)
        {
        }

        public static ClrInstance CreateInterior(ulong address, ClrHeap heap)
        {
            var type = heap.GetObjectType(address);
            if (type == null)
            {
                // TODO: Throw or what?
                return null;
            }

            return new ClrInstance(null, address, heap, type, interior: true);
        }

        public static ClrInstance FromAddress(ulong address, ClrHeap heap)
        {
            var type = heap.GetObjectType(address);
            if (type == null)
            {
                // TODO: Throw or what?
                return null;
            }

            return new ClrInstance(address, heap, type);
        }

        public bool IsOfType(ClrType type)
        {
            // TODO: should be an extension method.
            // And what about subtyping?
            return ClrTypeEqualityComparer.Instance.Equals(Type, type);
        }

        public bool IsArray => Type.IsArray;

        private ClrInstance[] _items;
        public ClrInstance[] Items
        {
            get
            {
                if (_items == null)
                {
                    _items = Type.IsArray ? ComputeItems() : new ClrInstance[0];
                }

                return _items;
            }
        }

        private ClrInstance[] ComputeItems()
        {
            var address = (ulong)Value;
            var length = Type.GetArrayLength(address);
            var items = new ClrInstance[length];
            var elementType = Type.ComponentType;
            for (int i = 0; i < length; i++)
            {
                var value = Type.GetArrayElementValue(address, i);
                items[i] = CreateItemInstance(value, elementType, i);
            }

            return items;
        }

        private ClrInstance CreateItemInstance(object value, ClrType type, int index)
        {
            if (!type.IsIntrinsic() && value != null)
            {
                type = Heap.GetObjectType((ulong)value) ?? type;
            }

            var address = Type.GetArrayElementAddress(ObjectAddress, index);

            //if (value == null)
            //{
            //    value = Type.GetArrayElementAddress(ObjectAddress, index);
            //}

            return new ClrInstance(value, address, Heap, type, interior: false);
        }

        private ClrInstance GetMember(string name)
        {
            if (TryGetMember(name, out var result) && result is ClrInstance clrInstance)
            {
                return clrInstance;
            }

            return null;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (Type.IsString)
            {
                return $"\"{GetValue()}\"";
            }

            return IsObject ? $"{Type?.Name} ({Value ?? ObjectAddress})" : Value?.ToString() ?? "<NO VALUE>";
        }

        public bool TryGetFieldValue(string name, out ClrInstance result)
        {
            if (TryGetMember(name, out var objResult) && objResult is ClrInstance r)
            {
                result = r;
                return true;
            }

            result = null;
            return false;
        }

        public bool TryGetMember(string name, out object result)
        {
            if (name == "Length" && Type.IsArray)
            {
                result = Type.GetArrayLength(ObjectAddress);
                return true;
            }

            var field = Type.GetFieldByName(name);

            if (field == null)
            {
                // The field wasn't found, it could be an autoproperty
                field = Type.GetFieldByName($"<{name}>k__BackingField");

                if (field == null)
                {
                    // Still not found
                    result = null;
                    return false;
                }
            }

            if (!field.HasSimpleValue)
            {
                result = LinkToStruct(field);
                return true;
            }

            result = field.GetValue(ObjectAddress, Interior);

            if (IsReference(result, field.Type))
            {
                var fieldAddress = (ulong)result;

                // Sometimes, ClrMD isn't capable of resolving the property type using the field
                // Try again using directly the address, in case we fetch something different
                if (fieldAddress != 0)
                {
                    var type = Heap.GetObjectType(fieldAddress);

                    var alternativeValue = type.GetValue(fieldAddress);

                    if (!(alternativeValue is ulong))
                    {
                        result = alternativeValue;
                        return true;
                    }
                }

                result = FromAddress(fieldAddress, Heap);
            }
            else
            {
                result = new ClrInstance(result, ObjectAddress, Heap, field.Type, interior: true);
            }

            return true;
        }

        private ClrInstance LinkToStruct(ClrField field)
        {
            var childAddress = AddressWithoutHeader + (ulong)field.Offset;

            return new ClrInstance(childAddress, Heap, field.Type);
        }

        private static bool IsReference(object result, ClrType type)
        {
            return result != null && !(result is string) && type.IsObjectReference;
        }


        private sealed class ClrInstanceDebuggerView
        {
            public ClrInstanceDebuggerView(ClrInstance instance)
            {
                Type = instance.Type;
                Fields = instance.Fields;
                Value = instance.Value;
            }

            public string SomeValue { get; } = "fooBar";
            private object Value { get; }

            private ClrFieldValue[] Fields { get; }

            private ClrType Type { get; }
        }
    }
}
