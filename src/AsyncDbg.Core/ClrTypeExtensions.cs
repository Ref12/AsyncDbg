// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace AsyncDbgCore.Core
{
    public static class ClrTypeExtensions
    {
        public static bool IsIntrinsic(this ClrType type)
        {
            return (type.IsPrimitive || type.IsString);
        }

        public static bool IsStringOrPrimitive(this ClrType type)
        {
            return (type.IsPrimitive || type.IsString);
        }

        public static IEnumerable<ClrType> EnumerateBaseTypesAndSelf(this ClrType type)
        {
            while (type != null)
            {
                yield return type;
                type = type.BaseType;
            }
        }

        public static bool IsOfType(this ClrType type, Type actualType)
        {
            return type.Name == actualType.FullName;
        }

        public static bool IsOfTypes(this ClrType type, params Type[] actualTypes)
        {
            return actualTypes.Any(t => t.FullName == type.Name);
        }
    }
}
