// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using AsyncDbg;
using AsyncDbg.Core;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace AsyncDbgCore.Core
{
    public static class ClrTypeExtensions
    {
        public static bool IsClosure(this ClrType? type)
        {
            if (type == null)
            {
                return false;
            }

            return type.Name.Contains("DisplayClass") && type.Name.Contains("<>c__");
        }

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

        public static string? TypeToString(this ClrType? type, TypesRegistry registry) => type == null ? null : new TypeNameHelper(registry).TypeToString(type);
    }
}
