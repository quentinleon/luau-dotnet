using System;
using Luau;
using UnityEngine;

namespace Luau.Unity.PackageConsumerProbe
{
    internal static class RunConsumerProbe
    {
        public const string PassedMarker = "LUAU_PACKAGE_CONSUMER_PASS";
        public const string FailedMarker = "LUAU_PACKAGE_CONSUMER_FAIL";

        public static void Execute()
        {
            try
            {
                Run();
                Debug.Log(PassedMarker);
            }
            catch (Exception exception)
            {
                Debug.LogError(FailedMarker + "\n" + exception);
                throw;
            }
        }

        static void Run()
        {
            var options = ConsumerApiProbe.CreateOptions(state =>
                state.OpenLibrary(ConsumerGeneratedLibrary.CreateGeneratedLibrary()));
            using var root = LuauUnity.CreateState(options);
            using var thread = root.CreateSandboxedThread();

            AssertSingleInteger(
                thread.DoString("return 40 + 2", "@consumer/native-vm.luau"),
                42,
                "Native VM execution");
            AssertSingleInteger(
                thread.DoString("return consumerProbe.addOne(41)", "@consumer/generated-library.luau"),
                42,
                "Generated host-library dispatch");
        }

        static void AssertSingleInteger(LuauValue[] values, int expected, string operation)
        {
            if (values.Length != 1 || values[0].Read<int>() != expected)
            {
                throw new InvalidOperationException(operation + " returned an unexpected result.");
            }
        }
    }
}
