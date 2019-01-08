// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using Microsoft.Diagnostics.Runtime;

namespace AsyncDbgCore.Core
{
    public class ClrFieldValue
    {
        public ClrInstanceField Field { get; }
        public ClrInstance Instance { get; }

        /// <inheritdoc />
        public ClrFieldValue(ClrInstanceField field, ClrInstance instance)
        {
            Field = field;
            Instance = instance;
        }

        internal static ClrFieldValue Create(ClrInstanceField typeField, ClrInstance instance)
        {
            var getValueTypeField = typeField;
            if (typeField.Type.MetadataToken == 0 && typeField.Type.Name == "ERROR")
            {
                throw new NotSupportedException();
                //if (ClrInstance.BypassFieldsByFieldName.TryGetValue(typeField.Name, out var bypassField))
                //{
                //    getValueTypeField = bypassField;
                //}
            }

            var value = getValueTypeField.GetValue(instance.ObjectAddress);

            //if (IsReference(value, typeField.Type))
            //{
            //    var fieldAddress = (ulong)value;

            //    // Sometimes, ClrMD isn't capable of resolving the property type using the field
            //    // Try again using directly the address, in case we fetch something different
            //    if (fieldAddress != 0)
            //    {
            //        var type2 = instance.Heap.GetObjectType(fieldAddress);
            //        if (type2 != null)
            //        {
            //            var alternativeValue = type2.GetValue(fieldAddress);

            //            if (!(alternativeValue is ulong))
            //            {
            //                value = alternativeValue;
            //            }
            //        }
            //    }

            //    //result = FromAddress(fieldAddress, Heap);
            //}

            var type = getValueTypeField.Type;

            if (!type.IsIntrinsic() && value != null)
            {
                try
                {
                    type = instance.Heap.GetObjectType((ulong)value) ?? type;
                }
                catch (InvalidCastException e)
                {
                    Console.WriteLine(e);
                }
            }

            var address = instance.ObjectAddress + (ulong)typeField.Offset;

            return new ClrFieldValue(typeField, new ClrInstance(value ?? address, address, instance.Heap, type, interior: value != null));
        }

        public override string ToString()
        {
            return $"{Field.Name}: {Instance}";
        }

        private static bool IsReference(object result, ClrType type)
        {
            return result != null && !(result is string) && type.IsObjectReference;
        }
    }
}
