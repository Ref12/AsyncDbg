using Microsoft.Diagnostics.Runtime;

namespace AsyncDbg.Core
{
    public struct TypeNameHelper
    {
        private readonly TypesRegistry _registry;

        public TypeNameHelper(TypesRegistry registry) => _registry = registry;

        public string TypeToString(ClrType type)
        {
            var typeName = SimplifyWhellKnownTypes(type.Name);

            if (_registry.IsAsyncStateMachine(type))
            {
                return GetAsyncMethodNameFromAsyncStateMachine(type, typeName);
            }

            if (_registry.IsTask(type))
            {
                return
                    typeName
                    .Replace("<VoidTaskResult>", "")
                    .Replace("+WhenAllPromise", ".WhenAll()")
                    ;
            }

            return type.ToString();
        }

        public static string SimplifyWhellKnownTypes(string typeName)
        {
            typeName = typeName.Replace("System.Threading.Tasks.", "");
            typeName = typeName.Replace("System.Collections.Generic.", "");
            typeName = typeName.Replace("System.Collections.Concurrent.", "");

            typeName = typeName.Replace("System.Object", "object");
            return typeName;
        }

        private string GetAsyncMethodNameFromAsyncStateMachine(ClrType type, string typeName)
        {
            // The type name format is NamespaceName.TypeName+<MethodName>d__x
            var plusIndex = typeName.IndexOf("+");
            Contract.Assert(plusIndex != -1, $"{typeName} should have '+' sign in it.");

            var greaterIndex = typeName.IndexOf(">", plusIndex);
            Contract.Assert(greaterIndex != -1, $"{typeName} should have '>' sign in it.");

            var methodName = typeName.Substring(plusIndex + 2, greaterIndex - plusIndex - 2);
            var resultingTypeName = typeName.Substring(0, plusIndex);

            return $"{resultingTypeName}.{methodName}";
        }
    }
}
