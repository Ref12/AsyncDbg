// --------------------------------------------------------------------
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
using Microsoft.Diagnostics.Runtime;

namespace AsyncCausalityDebuggerNew
{
    internal class ClrInstanceDynamicProxy : DynamicObject
    {
        private readonly ClrInstance _instance;

        /// <nodoc />
        public ClrInstanceDynamicProxy(ClrInstance clrInstance)
        {
            _instance = clrInstance;
        }

        public ClrType Type => _instance.Type;
        public ClrFieldValue[] Fields => _instance.Fields;
        public ClrInstance this[string fieldName] => Fields.FirstOrDefault(f => f.Field.Name == fieldName)?.Instance;

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            if (_instance.TryGetFieldValue(binder.Name, out var field))
            {
                result = field;
                return true;
            }

            result = null;
            return false;
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
                result = _instance.ObjectAddress;
                return true;
            }

            if (binder.ReturnType == typeof(string) && Type.IsString)
            {
                result = _instance.ValueOrDefault;
                return true;
            }

            if (binder.ReturnType.FullName == Type.Name && IsBlittable(binder.ReturnType))
            {
                if (binder.ReturnType.IsArray)
                {
                    result = MarshalToArray(binder.ReturnType, Type);
                    return true;
                }

                result = MarshalToStruct(_instance.AddressWithoutHeader, binder.ReturnType, Type);
                return true;
            }

            IEnumerable<dynamic> Enumerate()
            {
                //var length = Type.GetArrayLength(_instance.ObjectAddress.Value);
                var items = _instance.Items;
                for (int i = 0; i < items.Length; i++)
                {
                    yield return items[i];
                }
            }

            if (binder.ReturnType == typeof(IEnumerable))
            {
                result = Enumerate();
                return true;
            }

            throw new InvalidCastException("Can only cast array and blittable types, or to ulong to retrieve the address");
        }

        public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
        {
            if (!Type.IsArray)
            {
                throw new NotSupportedException($"{Type.Name} is not an array");
            }

            var index = (int)indexes[0];

            result = _instance.Items[index];
            return true;
        }

        private static bool IsReference(object result, ClrType type)
        {
            return result != null && !(result is string) && type.IsObjectReference;
        }

        //private static DynamicProxy GetProxy(ClrHeap heap, ulong address)
        //{
        //    return address == 0 ? null : new DynamicProxy(heap, address);
        //}

        // ReSharper disable once UnusedMember.Local - Used through reflection
        //private static unsafe object Read<T>(byte[] buffer)
        //{
        //    fixed (byte* b = buffer)
        //    {
        //        return Unsafe.Read<T>(b);
        //    }
        //}

        //private object GetElementAt(int index)
        //{
        //    if (Type.ComponentType.HasSimpleValue)
        //    {
        //        var result = Type.GetArrayElementValue(_instance.ObjectAddress, index);

        //        if (IsReference(result, Type.ComponentType))
        //        {
        //            return GetProxy(_instance.Heap, (ulong)result);
        //        }

        //        return Type.GetArrayElementValue(_instance.ObjectAddress, index);
        //    }

        //    var instance = new ClrInstance(value: null, heap: _instance.Heap, address: Type.GetArrayElementAddress(_instance.ObjectAddress, index), type: Type.ComponentType, interior: true);
        //    return new ClrInstanceDynamicProxy(instance);
        //}

        private object MarshalToStruct(ulong address, Type destinationType, ClrType destinationClrType)
        {
            var buffer = new byte[destinationClrType.BaseSize];

            _instance.Heap.ReadMemory(address, buffer, 0, buffer.Length);

            var method = GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Static).First(m => m.Name == "Read").MakeGenericMethod(destinationType);

            return method.Invoke(null, new object[] { buffer });
        }

        private object MarshalToArray(Type arrayType, ClrType arrayClrType)
        {
            var length = Type.GetArrayLength(_instance.ObjectAddress.Value);

            var array = (Array)Activator.CreateInstance(arrayType, length);

            var elementType = arrayType.GetElementType();
            var elementClrType = arrayClrType.ComponentType;

            if (length == 0)
            {
                return array;
            }

            for (int i = 0; i < length; i++)
            {
                var elementAddress = Type.GetArrayElementAddress(_instance.ObjectAddress.Value, i);
                array.SetValue(MarshalToStruct(elementAddress, elementType, elementClrType), i);
            }

            return array;
        }
    }
}
