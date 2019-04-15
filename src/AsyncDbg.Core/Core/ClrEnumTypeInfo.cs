// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Diagnostics.Runtime;

namespace AsyncDbgCore.Core
{
    /// <summary>
    /// Contains metadata for a given enum type represented by <see cref="ClrType"/>.
    /// </summary>
    public class ClrEnumTypeInfo
    {
        /// <summary>
        /// Weak cache of enum types.
        /// </summary>
        private static readonly ConditionalWeakTable<ClrType, ClrEnumTypeInfo> _enumTypeInfos = new ConditionalWeakTable<ClrType, ClrEnumTypeInfo>();

        private readonly Dictionary<string, ClrEnumValue> _enumValuesByName = new Dictionary<string, ClrEnumValue>();
        private readonly Dictionary<object, ClrEnumValue> _enumValuesByValue = new Dictionary<object, ClrEnumValue>();

        /// <nodoc />
        public ClrEnumTypeInfo(ClrType clrType) => ClrType = clrType;

        /// <summary>
        /// Underlying clr type of the enum.
        /// </summary>
        public ClrType ClrType { get; }

        /// <summary>
        /// Enumerates all the values for the current enum type.
        /// </summary>
        public IReadOnlyCollection<ClrEnumValue> Values => _enumValuesByValue.Values;

        public bool TryGetEnumByValue(object value, out ClrEnumValue result) => _enumValuesByValue.TryGetValue(value, out result);

        /// <summary>
        /// Gets or creates <see cref="ClrEnumTypeInfo"/> for the given <paramref name="type"/>.
        /// </summary>
        public static ClrEnumTypeInfo GetOrCreateEnumTypeInfo(ClrType type)
        {
            Contract.Requires(type.IsEnum, "The given type should be an enum type.");
            return _enumTypeInfos.GetValue(type, CreateInstance);
        }

        private static ClrEnumTypeInfo CreateInstance(ClrType type)
        {
            ClrEnumTypeInfo info = new ClrEnumTypeInfo(type);
            foreach (var enumName in type.GetEnumNames())
            {
                type.TryGetEnumValue(enumName, out object value);
                var intValue = ((IConvertible)value).ToInt32(null);
                var enumValue = new ClrEnumValue(enumName, intValue);

                info._enumValuesByName[enumName] = enumValue;
                info._enumValuesByValue[value] = enumValue;
            }

            return info;
        }
    }
}
