using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Luau;
using UnityEditor.PackageManager;
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
            ValidateXmlIntelliSense();

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

        static void ValidateXmlIntelliSense()
        {
            var package = PackageInfo.FindForAssembly(typeof(LuauState).Assembly)
                ?? throw new InvalidOperationException(
                    "Unity did not resolve the Luau assembly from a package.");
            var xmlPath = Path.Combine(package.resolvedPath, "Runtime", "Luau.xml");
            if (!File.Exists(xmlPath))
            {
                throw new InvalidOperationException(
                    "The resolved package is missing Runtime/Luau.xml IntelliSense documentation.");
            }

            var document = XDocument.Load(xmlPath, LoadOptions.None);
            var assemblyName = document.Root?
                .Element("assembly")?
                .Element("name")?
                .Value;
            if (!string.Equals(assemblyName, "Luau", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Runtime/Luau.xml does not describe the shipped Luau assembly.");
            }

            var documentedMembers = document.Root?
                .Element("members")?
                .Elements("member")
                .Select(element => (string)element.Attribute("name"))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var requiredMember in new[]
            {
                "T:Luau.LuauState",
                "T:Luau.LuauResultScope",
                "M:Luau.LuauCallContext.Read``1(System.Int32)",
            })
            {
                if (documentedMembers == null || !documentedMembers.Contains(requiredMember))
                {
                    throw new InvalidOperationException(
                        "Runtime/Luau.xml is missing IntelliSense for " + requiredMember + ".");
                }
            }
        }

        static void AssertSingleInteger(LuauResultScope values, int expected, string operation)
        {
            using (values)
            {
                if (values.Length != 1 || values[0].Read<int>() != expected)
                {
                    throw new InvalidOperationException(operation + " returned an unexpected result.");
                }
            }
        }
    }
}
