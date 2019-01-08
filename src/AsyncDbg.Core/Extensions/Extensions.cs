﻿// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using Microsoft.Diagnostics.Runtime;

namespace AsyncDbg.Extensions
{
    public static class Extensions
    {
        public static dynamic GetProxy(this ClrHeap heap, ulong address)
        {
            if (address == 0)
            {
                return null;
            }

            return new DynamicProxy(heap, address);
        }

        public static IEnumerable<dynamic> GetProxies<T>(this ClrHeap heap)
        {
            return GetProxies(heap, typeof(T).FullName);
        }

        public static IEnumerable<dynamic> GetProxies(this ClrHeap heap, string typeName)
        {
            typeName = FixTypeName(typeName);

            foreach (var address in heap.EnumerateObjectAddresses())
            {
                if (heap.GetObjectType(address)?.Name == typeName)
                {
                    yield return heap.GetProxy(address);
                }
            }
        }

        public static dynamic AsDynamic(this ClrObject clrObject)
        {
            var heap = clrObject.Type.Heap;

            return heap.GetProxy(clrObject.Address);
        }

        public static string FixTypeName(string typeName)
        {
            if (!typeName.Contains("`"))
            {
                return typeName;
            }

            var sb = new StringBuilder();

            FixGenericsWorker(typeName, 0, typeName.Length, sb);

            return sb.ToString();
        }

        /// <summary>
        /// A messy version with better performance that doesn't use regular expression.
        /// </summary>
        private static int FixGenericsWorker(string name, int start, int end, StringBuilder sb)
        {
            int num1 = 0;
            for (; start < end; ++start)
            {
                char ch = name[start];
                if (ch != '`')
                {
                    if (ch == '[')
                    {
                        ++num1;
                    }

                    if (ch == ']')
                    {
                        --num1;
                    }

                    if (num1 < 0)
                    {
                        return start + 1;
                    }

                    if (ch == ',' && num1 == 0)
                    {
                        return start;
                    }

                    sb.Append(ch);
                }
                else
                {
                    break;
                }
            }
            if (start >= end)
            {
                return start;
            }

            ++start;
            int num2 = 0;
            bool flag1;
            do
            {
                int num3 = 0;
                flag1 = false;
                for (; start < end; ++start)
                {
                    char ch = name[start];
                    if (ch >= '0' && ch <= '9')
                    {
                        num3 = num3 * 10 + (int)ch - 48;
                    }
                    else
                    {
                        break;
                    }
                }
                num2 += num3;
                if (start >= end)
                {
                    return start;
                }

                if (name[start] == '+')
                {
                    for (; start < end && name[start] != '['; ++start)
                    {
                        if (name[start] == '`')
                        {
                            ++start;
                            flag1 = true;
                            break;
                        }
                        sb.Append(name[start]);
                    }
                    if (start >= end)
                    {
                        return start;
                    }
                }
            }
            while (flag1);
            if (name[start] == '[')
            {
                sb.Append('<');
                ++start;
                while (num2-- > 0)
                {
                    if (start >= end)
                    {
                        return start;
                    }

                    bool flag2 = false;
                    if (name[start] == '[')
                    {
                        flag2 = true;
                        ++start;
                    }
                    start = FixGenericsWorker(name, start, end, sb);
                    if (start < end && name[start] == '[')
                    {
                        ++start;
                        if (start >= end)
                        {
                            return start;
                        }

                        sb.Append('[');
                        for (; start < end && name[start] == ','; ++start)
                        {
                            sb.Append(',');
                        }

                        if (start >= end)
                        {
                            return start;
                        }

                        if (name[start] == ']')
                        {
                            sb.Append(']');
                            ++start;
                        }
                    }
                    if (flag2)
                    {
                        while (start < end && name[start] != ']')
                        {
                            ++start;
                        }

                        ++start;
                    }
                    if (num2 > 0)
                    {
                        if (start >= end)
                        {
                            return start;
                        }

                        sb.Append(',');
                        ++start;
                        if (start >= end)
                        {
                            return start;
                        }

                        if (name[start] == ' ')
                        {
                            ++start;
                        }
                    }
                }
                sb.Append('>');
                ++start;
            }
            if (start + 1 >= end || (name[start] != '[' || name[start + 1] != ']'))
            {
                return start;
            }

            sb.Append("[]");
            return start;
        }
    }
}