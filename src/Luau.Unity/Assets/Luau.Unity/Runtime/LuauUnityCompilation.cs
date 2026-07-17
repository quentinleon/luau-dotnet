using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Luau.Unity
{
    public static partial class LuauUnity
    {
        internal static readonly TimeSpan CompilationServiceDrainTimeout =
            TimeSpan.FromSeconds(5);

        static readonly object CompilationServiceGate = new object();
        static LuauThreadedCompilationService compilationService;
        static bool compilationServiceStopping;

        /// <summary>
        /// Compiles UTF-8 Luau source on the package-owned, bounded background
        /// compilation lane. The lane is shared by all callers and is drained
        /// automatically during Unity shutdown and Editor assembly reload.
        /// </summary>
        public static ValueTask<LuauCompileResult> CompileAsync(
            ReadOnlyMemory<byte> utf8Source,
            LuauCompileOptions options = null,
            CancellationToken cancellationToken = default)
        {
            lock (CompilationServiceGate)
            {
                if (compilationServiceStopping)
                {
                    throw new ObjectDisposedException(
                        nameof(LuauUnity),
                        "The shared Luau compilation service is shutting down.");
                }

                if (compilationService == null)
                {
                    compilationService = new LuauThreadedCompilationService(
                        GetRecommendedCompilationOptions());
                }

                // CompileAsync performs admission and takes its owned source
                // snapshot synchronously. Holding the lifecycle lock makes
                // admission linear with package shutdown.
                return compilationService.CompileAsync(
                    utf8Source,
                    options,
                    cancellationToken);
            }
        }

        /// <summary>
        /// Creates the recommended finite compilation policy for the current
        /// maintained Unity platform. Use this when constructing an advanced,
        /// caller-owned <see cref="LuauThreadedCompilationService"/>.
        /// </summary>
        public static LuauThreadedCompilationOptions GetRecommendedCompilationOptions()
        {
#if UNITY_EDITOR_WIN
            return GetRecommendedCompilationOptions(RuntimePlatform.WindowsEditor);
#elif UNITY_STANDALONE_WIN
            return GetRecommendedCompilationOptions(RuntimePlatform.WindowsPlayer);
#elif UNITY_ANDROID
            return GetRecommendedCompilationOptions(RuntimePlatform.Android);
#else
            throw new PlatformNotSupportedException(
                "Background Luau compilation is maintained for Windows x64 and Android ARM64/x64 only; " +
                "the current Unity target is not maintained.");
#endif
        }

        internal static LuauThreadedCompilationOptions GetRecommendedCompilationOptions(
            RuntimePlatform platform)
        {
            var windows = platform == RuntimePlatform.WindowsEditor ||
                platform == RuntimePlatform.WindowsPlayer;
            if (!windows && platform != RuntimePlatform.Android)
            {
                throw new PlatformNotSupportedException(
                    "Background Luau compilation is maintained for Windows x64 and Android ARM64/x64 only; " +
                    "the current Unity platform is " + platform + ".");
            }

            return new LuauThreadedCompilationOptions
            {
                WorkerCount = 1,
                MaxQueuedRequestCount = windows ? 32 : 16,
                MaxQueuedSourceBytes = windows ? 8L * 1024 * 1024 : 4L * 1024 * 1024,
                MaxSourceBytesPerRequest = 1024 * 1024,
                MaxBytecodeBytesPerResult = 4 * 1024 * 1024,
                ShutdownTimeout = CompilationServiceDrainTimeout,
            };
        }

        internal static async Task DrainCompilationServiceAsync(
            Action<LuauCompilationShutdownException> timeoutObserver = null)
        {
            LuauThreadedCompilationService service;
            lock (CompilationServiceGate)
            {
                compilationServiceStopping = true;
                service = compilationService;
            }

            if (service == null)
            {
                return;
            }

            // A native compile cannot be aborted safely. A lifecycle drain may
            // report finite progress intervals, but it must not allow Unity to
            // unload the managed assembly until the worker has actually exited.
            while (true)
            {
                try
                {
                    await service.DisposeAsync().ConfigureAwait(false);
                    return;
                }
                catch (LuauCompilationShutdownException exception)
                {
                    timeoutObserver?.Invoke(exception);
                }
            }
        }

#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RegisterCompilationServicePlayerLifetime()
        {
            Application.quitting -= DrainCompilationServiceForPlayerQuit;
            Application.quitting += DrainCompilationServiceForPlayerQuit;
        }

        static void DrainCompilationServiceForPlayerQuit()
        {
            try
            {
                DrainCompilationServiceAsync(exception =>
                    Debug.LogWarning(
                        "Luau background compilation is still draining during player shutdown.\n" +
                        exception))
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "The shared Luau compilation service could not be drained during player shutdown.\n" +
                    exception);
            }
        }
#endif
    }
}
