﻿// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AsyncDbgCore;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace AsyncDbg.Extensions
{
    internal class DynamicProxy : DynamicObject
    {
        private readonly ClrHeap _heap;
        private readonly ulong _address;

        private readonly bool _interior;
        private ClrType? _type;

        /// <nodoc />
        public DynamicProxy(AsyncDbgCore.Core.ClrInstance clrInstance)
        : this(clrInstance.Heap, clrInstance.ObjectAddress, clrInstance.Type)
        {
        }

        public DynamicProxy(ClrHeap heap, ClrObject clrObject)
        : this(heap, clrObject.Address, clrObject.Type)
        {
        }

        public DynamicProxy(ClrHeap heap, ulong address)
        {
            _heap = heap;
            _address = address;
        }

        private DynamicProxy(ClrHeap heap, ulong address, ClrType overrideType)
            : this(heap, address)
        {
            _type = overrideType;
            _interior = true;
        }

        protected ClrType Type
        {
            get
            {
                if (_type == null)
                {
                    _type = _heap.GetObjectType(_address);
                    Contract.AssertNotNull(_type);
                }

                return _type;
            }
        }

        protected ulong AddressWithoutHeader => _interior ? _address : _address + (ulong)_heap.PointerSize;

        public override bool TryGetMember(GetMemberBinder binder, [NotNullWhenTrue]out object? result)
        {
            if (binder.Name == "Length" && Type.IsArray)
            {
                result = Type.GetArrayLength(_address);
                return true;
            }

            var field = Type.GetFieldByName(binder.Name);

            if (field == null)
            {
                // The field wasn't found, it could be an autoproperty
                field = Type.GetFieldByName($"<{binder.Name}>k__BackingField");

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

            result = field.GetValue(_address, _interior);
            Contract.AssertNotNull(result);

            if (IsReference(result, field.Type))
            {
                var fieldAddress = (ulong)result;

                // Sometimes, ClrMD isn't capable of resolving the property type using the field
                // Try again using directly the address, in case we fetch something different
                if (fieldAddress != 0)
                {
                    var type = _heap.GetObjectType(fieldAddress);

                    var alternativeValue = type.GetValue(fieldAddress);

                    if (!(alternativeValue is ulong))
                    {
                        result = alternativeValue;
                        return true;
                    }
                }

                result = GetProxy(_heap, fieldAddress);
            }

            return true;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            if (binder.Name == "GetClrType")
            {
                result = Type;
                return true;
            }

            if (binder.Name == "Is")
            {
                if (args.Length != 1)
                {
                    throw new ArgumentException("Missing argument 'type'");
                }

                if (!(args[0] is string expectedType))
                {
                    throw new ArgumentException("The 'type' argument must be a string");
                }

                result = Type.Name == expectedType;
                return true;
            }

            return base.TryInvokeMember(binder, args, out result);
        }

        public static bool IsBlittable(Type type, bool allowArrays = true)
        {
            if (type.IsArray)
            {
                return allowArrays && IsBlittable(type.GetElementType());
            }

            if (!type.IsValueType)
            {
                return false;
            }

            if (type.IsPrimitive)
            {
                return true;
            }

            return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .All(f => IsBlittable(f.FieldType, false));
        }

        public override bool TryConvert(ConvertBinder binder, out object result)
        {
            if (binder.ReturnType == typeof(ulong))
            {
                result = _address;
                return true;
            }

            if (binder.ReturnType == typeof(string) && Type.IsString)
            {
                result = Type.GetValue(_address);
                return true;
            }

            if (binder.ReturnType.FullName == Type.Name && IsBlittable(binder.ReturnType))
            {
                if (binder.ReturnType.IsArray)
                {
                    result = MarshalToArray(binder.ReturnType, Type);
                    return true;
                }

                result = MarshalToStruct(AddressWithoutHeader, binder.ReturnType, Type);
                return true;
            }

            IEnumerable<dynamic> Enumerate()
            {
                var length = Type.GetArrayLength(_address);

                for (int i = 0; i < length; i++)
                {
                    yield return GetElementAt(i);
                }
            }

            if (binder.ReturnType == typeof(IEnumerable))
            {
                result = Enumerate();
                return true;
            }

            throw new InvalidCastException("Can only cast array and blittable types, or to ulong to retrieve the address");
        }

        public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, [NotNullWhenTrue]out object? result)
        {
            if (!Type.IsArray)
            {
                throw new NotSupportedException($"{Type.Name} is not an array");
            }

            var index = (int)indexes[0];

            result = GetElementAt(index);
            return result != null;
        }

        private static bool IsReference(object result, ClrType type)
        {
            //return result != null && !(result is string) && type.IsObjectReference;
            return !(result is string) && type.IsObjectReference;
        }

        private static DynamicProxy? GetProxy(ClrHeap heap, ulong address)
        {
            return address == 0 ? null : new DynamicProxy(heap, address);
        }

        // ReSharper disable once UnusedMember.Local - Used through reflection
        //private static unsafe object Read<T>(byte[] buffer)
        //{
        //    fixed (byte* b = buffer)
        //    {
        //        return Unsafe.Read<T>(b);
        //    }
        //}

        private DynamicProxy LinkToStruct(ClrField field)
        {
            var childAddress = AddressWithoutHeader + (ulong)field.Offset;

            return new DynamicProxy(_heap, childAddress, field.Type);
        }

        private object? GetElementAt(int index)
        {
            if (Type.ComponentType.HasSimpleValue)
            {
                var result = Type.GetArrayElementValue(_address, index);

                if (IsReference(result, Type.ComponentType))
                {
                    var proxy = GetProxy(_heap, (ulong)result);
                    Contract.AssertNotNull(proxy);
                    return proxy;
                }

                // GetArrayElementValue can return null.
                return Type.GetArrayElementValue(_address, index);
            }

            return new DynamicProxy(_heap, Type.GetArrayElementAddress(_address, index), Type.ComponentType);
        }

        private object MarshalToStruct(ulong address, Type destinationType, ClrType destinationClrType)
        {
            var buffer = new byte[destinationClrType.BaseSize];

            _heap.ReadMemory(address, buffer, 0, buffer.Length);

            var method = GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Static).First(m => m.Name == "Read").MakeGenericMethod(destinationType);

            return method.Invoke(null, new object[] { buffer });
        }

        private object MarshalToArray(Type arrayType, ClrType arrayClrType)
        {
            var length = Type.GetArrayLength(_address);

            var array = (Array)Activator.CreateInstance(arrayType, length);

            var elementType = arrayType.GetElementType();
            var elementClrType = arrayClrType.ComponentType;

            if (length == 0)
            {
                return array;
            }

            for (int i = 0; i < length; i++)
            {
                var elementAddress = Type.GetArrayElementAddress(_address, i);
                array.SetValue(MarshalToStruct(elementAddress, elementType, elementClrType), i);
            }

            return array;
        }
    }
}
