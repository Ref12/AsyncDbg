using System.Diagnostics;
using System.Runtime.CompilerServices;

#nullable enable

namespace AsyncDbgCore
{
    public static class Contract
    {
        public static void AssertNotNull([EnsuresNotNull]object? o)
        {
            if (o == null)
            {
                throw new System.InvalidOperationException("The value should not be null.");
            }
        }

        public static void AssertNotNull<T>([EnsuresNotNull]T? o) where T : struct
        {
            if (o == null)
            {
                throw new System.InvalidOperationException("The value should not be null.");
            }
        }

        public static void Requires(bool predicate, string message)
        {
            if (!predicate)
            {
                throw new System.Exception(message);
            }
        }

        public static T AssertNotNull<T>([EnsuresNotNull]T? value, string message) where T : class
        {
            if (value == null)
            {
                throw new System.InvalidOperationException($"The value should not be null. {message}.");
            }

            return value;
        }
    }
}
