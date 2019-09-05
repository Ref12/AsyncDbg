using System;
using System.Diagnostics.CodeAnalysis;

#nullable enable

namespace AsyncDbg
{
    public static class Contract
    {
        public static object AssertNotNull([NotNull]object? o)
        {
            if (o == null)
            {
                throw new InvalidOperationException("The value should not be null.");
            }

            return o;
        }

        public static T AssertNotNull<T>([NotNull]T? o) where T : struct
        {
            if (o == null)
            {
                throw new InvalidOperationException("The value should not be null.");
            }

            return o.Value;
        }

        public static void Requires(bool predicate, string message)
        {
            if (!predicate)
            {
                throw new Exception(message);
            }
        }

        public static T AssertNotNull<T>([NotNull]T? value, string message) where T : class
        {
            if (value == null)
            {
                throw new InvalidOperationException($"The value should not be null. {message}.");
            }

            return value;
        }

        public static void Assert(bool condition, string error)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Assertion violation: {error}.");
            }
        }
    }
}
