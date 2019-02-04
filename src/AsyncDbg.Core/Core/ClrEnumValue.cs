// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Diagnostics.Runtime;

namespace AsyncDbgCore.Core
{
    /// <summary>
    /// Represents enum instance at runtime.
    /// </summary>
    public class ClrEnumValue
    {
        /// <nodoc />
        public string Name { get; }

        /// <nodoc />
        public long Value { get; }

        /// <nodoc />
        public ClrEnumValue(string name, long value)
        {
            Name = name;
            Value = value;
        }

        /// <nodoc />
        public ClrEnumValue(long value, List<ClrEnumValue> values)
            : this(string.Join(" | ", values.Select(v => v.Name)), value)
        {
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Name;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is string)
            {
                return string.Equals(Name, (string)obj, StringComparison.OrdinalIgnoreCase);
            }

            return base.Equals(obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hashCode = -244751520;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Name);
            hashCode = hashCode * -1521134295 + Value.GetHashCode();
            return hashCode;
        }
    }

    public class ClrEnumTypeInfo
    {
        private static readonly ConditionalWeakTable<ClrType, ClrEnumTypeInfo> _enumTypeInfos = new ConditionalWeakTable<ClrType, ClrEnumTypeInfo>();

        public Dictionary<string, ClrEnumValue> Values = new Dictionary<string, ClrEnumValue>();
        public Dictionary<object, ClrEnumValue> ValuesByValue = new Dictionary<object, ClrEnumValue>();

        public static ClrEnumTypeInfo GetOrCreateEnumTypeInfo(ClrType type)
        {
            return _enumTypeInfos.GetValue(type, Create);
        }

        public static ClrEnumTypeInfo Create(ClrType type)
        {
            ClrEnumTypeInfo info = new ClrEnumTypeInfo();
            foreach (var enumName in type.GetEnumNames())
            {
                type.TryGetEnumValue(enumName, out object value);
                var intValue = ((IConvertible)value).ToInt32(null);
                var enumValue = new ClrEnumValue(enumName, intValue);

                info.Values[enumName] = enumValue;
                info.ValuesByValue[value] = enumValue;
            }

            return info;
        }
    }
}
