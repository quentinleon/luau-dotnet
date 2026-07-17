using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Luau.Unity.Tests
{
    public sealed class LuauCompilationServiceTests
    {
        [TestCase(RuntimePlatform.WindowsEditor, 32, 8L * 1024 * 1024)]
        [TestCase(RuntimePlatform.WindowsPlayer, 32, 8L * 1024 * 1024)]
        [TestCase(RuntimePlatform.Android, 16, 4L * 1024 * 1024)]
        public void RecommendedOptionsMatchMaintainedPlatformPolicy(
            RuntimePlatform platform,
            int queuedRequests,
            long queuedSourceBytes)
        {
            var options = LuauUnity.GetRecommendedCompilationOptions(platform);

            Assert.That(options.WorkerCount, Is.EqualTo(1));
            Assert.That(options.MaxQueuedRequestCount, Is.EqualTo(queuedRequests));
            Assert.That(options.MaxQueuedSourceBytes, Is.EqualTo(queuedSourceBytes));
            Assert.That(options.MaxSourceBytesPerRequest, Is.EqualTo(1024 * 1024));
            Assert.That(options.MaxBytecodeBytesPerResult, Is.EqualTo(4 * 1024 * 1024));
            Assert.That(options.ShutdownTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
        }

        [Test]
        public void RecommendedOptionsRejectUnsupportedUnityPlatforms()
        {
            Assert.Throws<PlatformNotSupportedException>(() =>
                LuauUnity.GetRecommendedCompilationOptions(RuntimePlatform.LinuxEditor));
        }

        [Test]
        public void RecommendedOptionsCanBeReadOnABackgroundThread()
        {
            var options = Task.Run(LuauUnity.GetRecommendedCompilationOptions)
                .GetAwaiter()
                .GetResult();

            Assert.That(options.WorkerCount, Is.EqualTo(1));
        }

        [Test]
        public void SharedFacadeCompilesOnThePackageOwnedLane()
        {
            var result = LuauUnity
                .CompileAsync(Encoding.UTF8.GetBytes("return 42"))
                .AsTask()
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Kind, Is.EqualTo(LuauCompileResultKind.Success));
            Assert.That(result.Output, Is.Not.Null);
        }

        [Test]
        public void CompilerOutputExecutionPostsOperationStartToStateOwner()
        {
            var scheduler = new QueuedOwnerScheduler();
            using var state = LuauState.Create(new LuauStateOptions
            {
                BytecodePolicy = LuauBytecodePolicy.Reject,
                DefaultExecutionOptions = new LuauExecutionOptions
                {
                    ContinuationScheduler = scheduler,
                },
            });
            var output = LuauCompiler.Compile(Encoding.UTF8.GetBytes("return 42"));
            Task<LuauValue[]> execution = null;

            Task.Run(() =>
            {
                execution = state.ExecuteCompilerOutputOnOwnerThreadAsync(
                        output,
                        "@unity/owner-thread-handoff.luau".AsMemory())
                    .AsTask();
            }).GetAwaiter().GetResult();

            Assert.That(
                SpinWait.SpinUntil(() => scheduler.PendingCount == 1, TimeSpan.FromSeconds(1)),
                Is.True,
                "The background caller did not post the VM operation to its owner.");
            Assert.That(execution, Is.Not.Null);
            Assert.That(execution.IsCompleted, Is.False);

            scheduler.RunNext();
            var results = execution.GetAwaiter().GetResult();

            Assert.That(results, Has.Length.EqualTo(1));
            Assert.That(results[0].Read<int>(), Is.EqualTo(42));
            Assert.That(scheduler.PostCount, Is.EqualTo(1));
        }

        [Test]
        public void BackgroundAssetExecutionPreservesTheStateSourceLimit()
        {
            var source = Encoding.UTF8.GetBytes("return 42");
            var asset = ScriptableObject.CreateInstance<LuauAsset>();
            asset.name = "@unity/source-limit.luau";
            asset.SetSource("return 42", source);
            using var state = LuauState.Create(new LuauStateOptions
            {
                MaxSourceBytes = source.Length - 1,
                MaxBytecodeBytes = 1024 * 1024,
            });
            var service = new LuauThreadedCompilationService(
                new LuauThreadedCompilationOptions
                {
                    MaxSourceBytesPerRequest = source.Length,
                });

            try
            {
                var exception = Assert.ThrowsAsync<LuauLoadLimitException>(async () =>
                    await state.ExecuteAsync(asset, service));

                Assert.That(exception.InputKind, Is.EqualTo(LuauLoadInputKind.Source));
                Assert.That(exception.ActualBytes, Is.EqualTo(source.Length));
                Assert.That(exception.LimitBytes, Is.EqualTo(source.Length - 1));
            }
            finally
            {
                service.DisposeAsync().AsTask().GetAwaiter().GetResult();
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        sealed class QueuedOwnerScheduler : ILuauContinuationScheduler
        {
            readonly object gate = new object();
            readonly Queue<Action> pending = new Queue<Action>();
            readonly int ownerThreadId = Environment.CurrentManagedThreadId;

            public int PostCount { get; private set; }

            public int PendingCount
            {
                get
                {
                    lock (gate)
                    {
                        return pending.Count;
                    }
                }
            }

            public bool CheckAccess()
            {
                return Environment.CurrentManagedThreadId == ownerThreadId;
            }

            public void Post(Action continuation)
            {
                lock (gate)
                {
                    pending.Enqueue(continuation);
                    PostCount++;
                }
            }

            public void RunNext()
            {
                Action continuation;
                lock (gate)
                {
                    continuation = pending.Dequeue();
                }

                continuation();
            }
        }

    }
}
