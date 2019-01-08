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

        [Conditional("Unknown")]
        public static void NotNull([EnsuresNotNull]object? o)
        {
            
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
