using System;
using StoryFlow.Data;

namespace StoryFlow.Tests
{
    // Standalone entry point. The full local harness can include the test file alone,
    // reusing its own partial Program assertions and manager setup instead.
    internal static partial class Program
    {
        private static int failures;

        private static int Main()
        {
            RunLocalizationHardeningTests();
            return failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            var before = failures;
            try { test(); }
            catch (Exception error)
            {
                failures++;
                Console.WriteLine(error);
            }
            Console.WriteLine((before == failures ? "PASS " : "FAIL ") + name);
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (Equals(expected, actual)) return;
            failures++;
            Console.WriteLine($"{message}: expected [{expected}], got [{actual}]");
        }

        private static void AssertTrue(bool value, string message)
        {
            AssertEqual(true, value, message);
        }

        private static StoryFlowManager CreateManager()
        {
            var manager = new StoryFlowManager();
            typeof(StoryFlowManager).GetProperty("Instance").GetSetMethod(true)
                .Invoke(null, new object[] { manager });
            return manager;
        }

        private static void SetManagerProject(StoryFlowProjectAsset project)
        {
            CreateManager().SetProject(project);
        }

        private static void ClearManager()
        {
            typeof(StoryFlowManager).GetProperty("Instance").GetSetMethod(true)
                .Invoke(null, new object[] { null });
        }
    }
}
