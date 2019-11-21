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

namespace AsyncDbg.Core
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
            if (type.Name == actualType.FullName)
            {
                // Full name match
                return true;
            }

            if (actualType.IsGenericType)
            {
                // type.Name can be something like AsyncMethodBuilder<System.__Canon>. In this case
                // just replace the first generic argument by `1.
                // Need to support generics with any number of arguments.
                return type.Name.Replace("<System.__Canon>", "`1") == actualType.FullName;
            }

            return false;
        }

        public static bool IsOfTypes(this ClrType type, params Type[] actualTypes)
        {
            return actualTypes.Any(t => t.FullName == type.Name);
        }

        public static string? TypeToString(this ClrType? type, TypesRegistry registry) => type == null ? null : new TypeNameHelper(registry).TypeToString(type);
    }
}
