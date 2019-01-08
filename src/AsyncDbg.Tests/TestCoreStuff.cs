// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using NUnit.Framework;

namespace Test.AsyncCausalityInspector
{
    [TestFixture]
    public class TestCoreStuff
    {
        [Test]
        public void LookAtArraysAtRuntime()
        {
            string path = @"F:\Sources\AsyncDbg\SampleDumps\BasicDatastructures.DMP";
            AsyncDbgCore.New.EntryPoint.DoStuff(path);
        }
    }
}