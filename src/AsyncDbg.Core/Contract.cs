using System;
using System.Runtime.CompilerServices;

#nullable enable

namespace AsyncDbg
{
    public static class Contract
    {
        public static void AssertNotNull([EnsuresNotNull]object? o)
        {
            if (o == null)
            {
                throw new InvalidOperationException("The value should not be null.");
            }
        }

        public static void AssertNotNull<T>([EnsuresNotNull]T? o) where T : struct
        {
            if (o == null)
            {
                throw new InvalidOperationException("The value should not be null.");
            }
        }

        public static void Requires(bool predicate, string message)
        {
            if (!predicate)
            {
                throw new Exception(message);
            }
        }

        public static T AssertNotNull<T>([EnsuresNotNull]T? value, string message) where T : class
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
