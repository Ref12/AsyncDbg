using Microsoft.Diagnostics.Runtime;
using System.Text.RegularExpressions;

#nullable enable

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
                return GetAsyncMethodNameFromAsyncStateMachine(typeName);
            }

            if (_registry.IsTask(type))
            {
                return
                    typeName
                    .Replace("<VoidTaskResult>", "")
                    .Replace("+WhenAllPromise", ".WhenAll()");
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

        /// <summary>
        /// Makes a name of an async state machine type more readable.
        /// </summary>
        public static string GetAsyncMethodNameFromAsyncStateMachine(string originalTypeName)
        {
            // Possible cases:
            // 1. Async state machine for an instance/static method:
            // like: ManualResetEventSlimOnTheStack.Program+<RunAsync>d__1
            //
            // 2. Async state machine for an async lambda expression:
            // like:
            // ManualResetEventSlimOnTheStack.Program+<>c+<<RunAsync>b__1_0>d -> lambda1
            // ManualResetEventSlimOnTheStack.Program+<>c+<<RunAsync>b__1_1>d -> lambda2
            // 
            // 3. Async state machine for a local async method:
            // like: ManualResetEventSlimOnTheStack.Program+<<RunAsync>g__local|1_2>d

            var regex = new Regex(@"(?<typeName>[^+]+)\+.*?<(?<method>\w+)>(?<suffix>.+)");
            var match = regex.Match(originalTypeName);
            if (match.Success)
            {
                var typeName = match.Groups["typeName"].Value;
                var method = match.Groups["method"].Value;
                var suffix = match.Groups["suffix"].Value;

                string? localName = suffix switch
                {
                    // simple async method: d__1
                    _ when suffix.Contains("d__") => null,

                    // lambda: b__1_0>d
                    _ when suffix.Contains("b__") => "lambda" + ExtractFrom(suffix, "b__1_", ">"),

                    // g__local|1_2>d
                    _ when suffix.Contains("g__") => ExtractFrom(suffix, "g__", "|"),
                    _ => suffix,
                };

                var result = $"{typeName}.{method}";
                if (localName != null)
                {
                    result += "." + localName;
                }

                return result;
            }

            System.Console.WriteLine($"Can't match the state machine's name '{originalTypeName}'.");
            return originalTypeName;
        }

        private static string ExtractFrom(string text, string prefix, string suffix)
        {
            var prefixEndIdx = text.IndexOf(prefix);
            if (prefixEndIdx == -1)
            {
                return text;
            }

            // IndexOf returns a starting index, need to move to the end of the prefix
            prefixEndIdx += prefix.Length;

            var startSuffixIdx = text.IndexOf(suffix, prefixEndIdx);
            if (startSuffixIdx == -1)
            {
                return text.Substring(prefixEndIdx);
            }

            int length = startSuffixIdx - prefixEndIdx;
            return text.Substring(prefixEndIdx, length);
        }
    }
}
