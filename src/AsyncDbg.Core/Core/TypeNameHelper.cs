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

        public static string TrySimplifyMethodSignature(string fullMethodSignature)
        {
            if (!IsGenerated(fullMethodSignature))
            {
                return fullMethodSignature;
            }

            string parameters = "(" + ExtractFrom(fullMethodSignature, "(", ")") + ")";
            string fullName = fullMethodSignature.Replace(parameters, string.Empty);
            string fullMethodName = GetAsyncMethodNameFromAsyncStateMachine(fullName);

            return $"{fullMethodName}{parameters}";
        }

        public static bool IsGenerated(string fullMethodName) => fullMethodName.Contains("+<");

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
            // ManualResetEventSlimOnTheStack.Program+<>c+<<RunAsync>b__1_0>d -> staticLambda1
            // ManualResetEventSlimOnTheStack.Program+<>c+<<RunAsync>b__2_1>d -> staticLambda2
            // Program+<>c__DisplayClass1_0.<<SetCompletion>b__0>d 
            // WaitingOnTaskCompletionSource.Program+<>c__DisplayClass1_0.<SetCompletion>b__1 -> instanceLambda2
            // 
            // 3. Async state machine for a local async method:
            // like: ManualResetEventSlimOnTheStack.Program+<<RunAsync>g__local|1_2>d
            //
            // 4. Move next methods
            // like: AsyncReaderWriterLockDeadlock.Program+<Main>d__1.MoveNext()

            var regex = new Regex(@"(?<typeName>[^+]+)\+.*?<(?<asyncMethod>\w+)>(?<suffix>[^\.]+)(?<method>\.\w+\(.*?\))?");
            var match = regex.Match(originalTypeName);
            if (match.Success)
            {
                var typeName = match.Groups["typeName"].Value;
                var asyncMethod = match.Groups["asyncMethod"].Value;
                var method = match.Groups["method"].Value;
                var suffix = match.Groups["suffix"].Value;

                var lambdaKind = originalTypeName.Contains("+<>c+") ? "static" : originalTypeName.Contains("c__DisplayClass") ? "instance" : null;

                string? localName = suffix switch
                {
                    // simple async method: d__1
                    _ when suffix.Contains("d__") => null,

                    // lambda: b__1_0>d
                    // Need to add 1 to get a meaningful number of a lambda expression within the method.
                    _ when suffix.Contains("b__") => $"{lambdaKind}Lambda" + (ExtractSuffixAsNumber(suffix, @"b__(?<result>\d).*")),

                    // g__local|1_2>d
                    _ when suffix.Contains("g__") => Extract(suffix, @"g__(?<result>\w+)|.+?"),
                    _ => suffix,
                };

                var result = $"{typeName}.{asyncMethod}{method}";
                if (localName != null)
                {
                    result += "." + localName;
                }

                return result;
            }

            System.Console.WriteLine($"Can't match the state machine's name '{originalTypeName}'.");
            return originalTypeName;
        }

        private static int ExtractSuffixAsNumber(string text, string regex) => int.Parse(Extract(text, regex));

        private static string Extract(string text, string regex) => Extract(text, new Regex(regex));

        private static string Extract(string text, Regex regex)
        {
            var match = regex.Match(text);
            if (match.Success)
            {
                return match.Groups["result"].Value;
            }

            return text;
        }

        private static int ExtractSuffixAsNumber(string text, string prefix, string suffix) => int.Parse(ExtractFrom(text, prefix, suffix));

        public static string ExtractFrom(string text, string prefix, string suffix)
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
