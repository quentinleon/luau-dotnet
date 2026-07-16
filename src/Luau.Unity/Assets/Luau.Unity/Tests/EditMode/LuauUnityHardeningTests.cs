using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Luau.Unity.Tests
{
    public sealed class LuauUnityHardeningTests
    {
        [Test]
        public void UnityFacadeExportsOnlyApprovedProductTypes()
        {
            var actual = typeof(LuauUnity).Assembly
                .GetExportedTypes()
                .Where(type => type.Namespace != null &&
                    type.Namespace.StartsWith("Luau.Unity", StringComparison.Ordinal))
                .Select(type => type.FullName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(actual, Is.EqualTo(new[]
            {
                "Luau.Unity.AddressablesLuauRequirer",
                "Luau.Unity.LuauAsset",
                "Luau.Unity.LuauStateExtensions",
                "Luau.Unity.LuauUnity",
                "Luau.Unity.LuauUnityOptions",
                "Luau.Unity.ResourcesLuauRequirer",
                "Luau.Unity.Verification.LuauPlayerSmoke",
            }));
        }

        [Test]
        public void OptionsDefaultToHardenedModSettings()
        {
            var options = new LuauUnityOptions();

            Assert.That(options.OpenStandardLibraries, Is.True);
            Assert.That(options.OpenDebugLibrary, Is.False);
            Assert.That(options.EnableRequire, Is.False);
            Assert.That(options.SandboxRoot, Is.True);
            Assert.That(options.CaptureUnitySynchronizationContext, Is.True);
            Assert.That(
                options.MaxPrintArguments,
                Is.EqualTo(LuauUnityOptions.DefaultMaxPrintArguments));
            Assert.That(
                options.MaxPrintUtf8Bytes,
                Is.EqualTo(LuauUnityOptions.DefaultMaxPrintUtf8Bytes));
            Assert.That(options.StateOptions, Is.Not.Null);
            Assert.That(options.StateOptions.BytecodePolicy, Is.EqualTo(LuauBytecodePolicy.Reject));
        }

        [Test]
        public void DefaultPrintBoundsArgumentsAndManagedUtf8Output()
        {
            var messages = new List<string>();
            using var root = LuauUnity.CreateState(new LuauUnityOptions
            {
                MaxPrintArguments = 2,
                MaxPrintUtf8Bytes = 12,
                Log = messages.Add,
            });
            using var script = root.CreateSandboxedThread();

            var results = script.DoString(
                "local calls = 0; " +
                "local value = setmetatable({}, { " +
                "__tostring = function() calls += 1; return string.rep('🐺', 10000) end }); " +
                "print('one', 'two', value); " +
                "print(value); " +
                "return calls",
                "@unity/bounded-print.luau");

            Assert.That(results, Has.Length.EqualTo(1));
            Assert.That(results[0].Read<int>(), Is.EqualTo(1),
                "Values beyond the argument limit must not have their __tostring metamethod invoked.");
            Assert.That(messages, Has.Count.EqualTo(2));
            Assert.That(messages[0], Is.EqualTo("one\ttwo..."));
            Assert.That(messages[1], Is.EqualTo("🐺🐺..."));
            Assert.That(Encoding.UTF8.GetByteCount(messages[0]), Is.LessThanOrEqualTo(12));
            Assert.That(Encoding.UTF8.GetByteCount(messages[1]), Is.LessThanOrEqualTo(12));
        }

        [TestCase(0, 128, "maxArguments")]
        [TestCase(4, 0, "maxUtf8Bytes")]
        public void RegisterPrintRejectsNonPositiveLimits(
            int maxArguments,
            int maxUtf8Bytes,
            string parameterName)
        {
            using var root = LuauState.Create();

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                LuauUnity.RegisterPrint(root, _ => { }, maxArguments, maxUtf8Bytes));

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.ParamName, Is.EqualTo(parameterName));
        }

        [Test]
        public void DefaultStateCapturesUnitySynchronizationContextAndPreservesBudgets()
        {
            var currentContext = SynchronizationContext.Current;
            Assert.That(currentContext, Is.Not.Null,
                "The Unity test runner did not install its main-thread synchronization context.");

            using var root = LuauUnity.CreateState(new LuauUnityOptions
            {
                StateOptions = new LuauStateOptions
                {
                    BytecodePolicy = LuauBytecodePolicy.Reject,
                    DefaultExecutionOptions = new LuauExecutionOptions
                    {
                        WallClockLimit = TimeSpan.FromSeconds(1),
                        InterruptCountLimit = 100,
                        MaxResultCount = 8,
                    },
                },
                Log = _ => { },
            });

            var executionOptions = root.Options.DefaultExecutionOptions;
            var scheduler = executionOptions.ContinuationScheduler
                as LuauSynchronizationContextScheduler;

            Assert.That(scheduler, Is.Not.Null);
            Assert.That(scheduler.SynchronizationContext, Is.SameAs(currentContext));
            Assert.That(scheduler.CheckAccess(), Is.True);
            Assert.That(executionOptions.WallClockLimit, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(executionOptions.InterruptCountLimit, Is.EqualTo(100));
            Assert.That(executionOptions.MaxResultCount, Is.EqualTo(8));
        }

        [Test]
        public void DefaultStateOpensOnlySafeLibrariesAndSandboxesRoot()
        {
            using var root = LuauUnity.CreateState(new LuauUnityOptions
            {
                Log = _ => { },
            });
            using var script = root.CreateSandboxedThread();

            Assert.That(root.IsRootSandboxed, Is.True);

            var results = script.DoString(
                "return " +
                "type(math) == 'table' " +
                "and type(table) == 'table' " +
                "and type(string) == 'table' " +
                "and type(coroutine) == 'table' " +
                "and type(bit32) == 'table' " +
                "and type(utf8) == 'table' " +
                "and type(buffer) == 'table' " +
                "and type(vector) == 'table' " +
                "and type(print) == 'function', " +
                "os == nil " +
                "and debug == nil " +
                "and require == nil " +
                "and getfenv == nil " +
                "and setfenv == nil",
                "@unity/default-facade.luau");

            Assert.That(results, Has.Length.EqualTo(2));
            Assert.That(results[0].Read<bool>(), Is.True, "A safe standard library was unavailable.");
            Assert.That(results[1].Read<bool>(), Is.True, "An unsafe or opt-in global was exposed.");
            Assert.Throws<InvalidOperationException>(() => root["lateHostApi"] = 1);
        }

        [Test]
        public void HostApisAreRegisteredBeforeSandboxAndCannotBeShadowed()
        {
            var configured = false;
            using var root = LuauUnity.CreateState(new LuauUnityOptions
            {
                ConfigureHostApis = state =>
                {
                    configured = true;
                    state["hostAnswer"] = 42;
                },
                Log = _ => { },
            });
            using var script = root.CreateSandboxedThread();

            var results = script.DoString(
                "local ok = pcall(function() hostAnswer = 99 end); " +
                "return not ok, hostAnswer",
                "@unity/protected-host-api.luau");

            Assert.That(configured, Is.True);
            Assert.That(results, Has.Length.EqualTo(2));
            Assert.That(results[0].Read<bool>(), Is.True);
            Assert.That(results[1].Read<int>(), Is.EqualTo(42));
        }

        [Test]
        public void DefaultStateRejectsHostSuppliedBytecodeBeforeNativeLoading()
        {
            using var root = LuauUnity.CreateState(new LuauUnityOptions
            {
                Log = _ => { },
            });

            var exception = Assert.Throws<LuauException>(() => root.Execute(
                new byte[] { 0xff, 0x00, 0x80, 0x01 },
                "@unity/untrusted-bytecode.luau"));

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.Message, Does.Contain("disabled").IgnoreCase);
            Assert.That(exception.ChunkName, Is.EqualTo("@unity/untrusted-bytecode.luau"));
        }

        [Test]
        public void SerializedPrecompiledFlagCannotBypassStateBytecodePolicy()
        {
            var asset = ScriptableObject.CreateInstance<LuauAsset>();
            try
            {
                asset.name = "untrusted-addressable";
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("isPrecompiled").boolValue = true;
                serialized.FindProperty("bytes").arraySize = 4;
                serialized.FindProperty("bytes").GetArrayElementAtIndex(0).intValue = 0xff;
                serialized.FindProperty("bytes").GetArrayElementAtIndex(1).intValue = 0x00;
                serialized.FindProperty("bytes").GetArrayElementAtIndex(2).intValue = 0x80;
                serialized.FindProperty("bytes").GetArrayElementAtIndex(3).intValue = 0x01;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                using var root = LuauUnity.CreateState(new LuauUnityOptions
                {
                    Log = _ => { },
                });
                using var script = root.CreateSandboxedThread();

                var exception = Assert.Throws<LuauException>(() => script.Execute(asset));
                Assert.That(exception, Is.Not.Null);
                Assert.That(exception.Message, Does.Contain("disabled").IgnoreCase);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void TrustedAndUntrustedPrecompiledAssetExecutionRemainDistinct()
        {
            var asset = ScriptableObject.CreateInstance<LuauAsset>();
            try
            {
                asset.name = "bundled-host-script";
                var bytecode = LuauCompiler.Compile(Encoding.UTF8.GetBytes("return 42"));
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("isPrecompiled").boolValue = true;
                serialized.FindProperty("bytes").arraySize = bytecode.Length;
                for (var index = 0; index < bytecode.Length; index++)
                {
                    serialized.FindProperty("bytes")
                        .GetArrayElementAtIndex(index)
                        .intValue = bytecode[index];
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();

                using var root = LuauUnity.CreateState(new LuauUnityOptions
                {
                    Log = _ => { },
                });
                using var script = root.CreateSandboxedThread();

                Assert.Throws<LuauException>(() => script.Execute(asset));

                var results = script.ExecuteTrusted(asset);
                Assert.That(results, Has.Length.EqualTo(1));
                Assert.That(results[0].Read<int>(), Is.EqualTo(42));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
