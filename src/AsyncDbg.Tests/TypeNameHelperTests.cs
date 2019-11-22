// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using AsyncDbg.Core;
using NUnit.Framework;

namespace Test.AsyncCausalityInspector
{
    [TestFixture]
    public class TypeNameHelperTests
    {
        [TestCase("ManualResetEventSlimOnTheStack.Program+<RunAsync>d__1", "ManualResetEventSlimOnTheStack.Program.RunAsync")]
        [TestCase("ManualResetEventSlimOnTheStack.Program+<>c+<<RunAsync>b__1_0>d", "ManualResetEventSlimOnTheStack.Program.RunAsync.lambda1")]
        [TestCase("ManualResetEventSlimOnTheStack.Program+<>c+<<RunAsync>b__1_1>d", "ManualResetEventSlimOnTheStack.Program.RunAsync.lambda2")]
        [TestCase("ManualResetEventSlimOnTheStack.Program+<<RunAsync>g__local|1_2>d", "ManualResetEventSlimOnTheStack.Program.RunAsync.local")]
        public void TestTypeNameSimplification(string typeName, string expected)
        {
            var simplifiedTypeName = TypeNameHelper.GetAsyncMethodNameFromAsyncStateMachine(typeName);
            System.Console.WriteLine("Original type name: " + typeName);
            System.Console.WriteLine("Simplified type name: " + simplifiedTypeName);
            Assert.AreEqual(expected, simplifiedTypeName);
        }
    }
}
