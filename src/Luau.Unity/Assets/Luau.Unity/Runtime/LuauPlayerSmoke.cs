using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace Luau.Unity.Verification
{
    /// <summary>
    /// Runs a minimal native-plugin and sandbox smoke test inside a player.
    /// The pass/fail markers are intentionally stable for log-based CI checks.
    /// </summary>
    [AddComponentMenu("Luau/Verification/Luau Player Smoke")]
    [DisallowMultipleComponent]
    public sealed class LuauPlayerSmoke : MonoBehaviour
    {
        public const string PassedMarker = "LUAU_PLAYER_SMOKE_PASS";
        public const string FailedMarker = "LUAU_PLAYER_SMOKE_FAIL";

        [SerializeField] bool quitOnCompletion = true;

        public bool QuitOnCompletion
        {
            get => quitOnCompletion;
            set => quitOnCompletion = value;
        }

        IEnumerator Start()
        {
            var smoke = RunSmokeAsync();
            while (!smoke.IsCompleted)
            {
                yield return null;
            }

            var exitCode = smoke.GetAwaiter().GetResult() ? 0 : 1;
            if (quitOnCompletion)
            {
                yield return QuitAfterLogFlush(exitCode);
            }
        }

        public static async Task<bool> RunSmokeAsync()
        {
            try
            {
                var unityThreadId = System.Environment.CurrentManagedThreadId;
                using var root = LuauUnity.CreateState(new LuauUnityOptions
                {
                    StateOptions = new LuauStateOptions
                    {
                        MemoryLimitBytes = 32 * 1024 * 1024,
                        BytecodePolicy = LuauBytecodePolicy.Reject,
                    },
                    ConfigureHostApis = state =>
                    {
                        state["hostAnswer"] = 42;
                        state["hostAddOne"] = state.CreateFunction(
                            "hostAddOne",
                            callbackState =>
                            {
                                callbackState.PushInteger(callbackState.ToInteger(1) + 1);
                                return 1;
                            });
                        state["hostAsyncAnswer"] = state.CreateFunction(
                            "hostAsyncAnswer",
                            async (callbackState, cancellationToken) =>
                            {
                                await Task.Delay(1, cancellationToken);
                                if (System.Environment.CurrentManagedThreadId != unityThreadId)
                                {
                                    throw new InvalidOperationException(
                                        "The asynchronous host callback left the Unity main thread.");
                                }

                                callbackState.PushInteger(77);
                                return 1;
                            });
                        state["hostAssertUnityThread"] = state.CreateFunction(
                            "hostAssertUnityThread",
                            callbackState =>
                            {
                                if (System.Environment.CurrentManagedThreadId != unityThreadId)
                                {
                                    throw new InvalidOperationException(
                                        "Luau resumed off the Unity main thread after an asynchronous callback.");
                                }

                                callbackState.PushBoolean(true);
                                return 1;
                            });
                    },
                    Log = message => Debug.Log("[Luau] " + message),
                });
                using var first = root.CreateSandboxedThread();
                using var second = root.CreateSandboxedThread();

                var firstResult = first.DoString(
                    "scriptLocal = 123; " +
                    "local protected = not pcall(function() hostAnswer = 99 end); " +
                    "return math.floor(41.9) == 41 " +
                    "and hostAnswer == 42 " +
                    "and hostAddOne(41) == 42 " +
                    "and protected " +
                    "and os == nil " +
                    "and debug == nil " +
                    "and require == nil " +
                    "and getfenv == nil " +
                    "and setfenv == nil",
                    "@unity/player-smoke-first.luau");
                var secondResult = second.DoString(
                    "return scriptLocal == nil and hostAnswer == 42",
                    "@unity/player-smoke-second.luau");
                var asyncResult = await first.DoStringAsync(
                    "local answer = hostAsyncAnswer(); return answer, hostAssertUnityThread()",
                    "@unity/player-smoke-async.luau");

                if (firstResult.Length != 1 || !firstResult[0].Read<bool>())
                {
                    throw new LuauException("The primary sandbox smoke script returned an unexpected result.");
                }

                if (secondResult.Length != 1 || !secondResult[0].Read<bool>())
                {
                    throw new LuauException("Sandboxed script globals leaked between sibling threads.");
                }

                if (asyncResult.Length != 2 ||
                    asyncResult[0].Read<int>() != 77 ||
                    !asyncResult[1].Read<bool>())
                {
                    throw new LuauException(
                        "The asynchronous managed callback or Unity-thread-affinity smoke test failed.");
                }

                if (!root.MemoryUsage.IsLimited || root.MemoryUsage.PeakBytes <= 0)
                {
                    throw new LuauException("The tracked native allocator did not report usage.");
                }

                Debug.Log(PassedMarker + " platform=" + Application.platform);
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogError(FailedMarker + "\n" + exception);
                return false;
            }
        }

        static IEnumerator QuitAfterLogFlush(int exitCode)
        {
            yield return null;
            Application.Quit(exitCode);
        }
    }
}
