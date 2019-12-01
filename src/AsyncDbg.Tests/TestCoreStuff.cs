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
            string path = @"F:\Sources\GitHub\AsyncDbg\src\SampleDumps\BasicDatastructures.DMP";
            AsyncDbg.Core.EntryPoint.DoStuff(path);
        }
    }
}
