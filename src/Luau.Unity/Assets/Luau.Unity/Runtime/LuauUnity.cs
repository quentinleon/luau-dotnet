using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Luau.Unity
{
    public sealed class LuauUnityOptions
    {
        public bool OpenStandardLibraries { get; set; } = true;
        public bool OpenDebugLibrary { get; set; }
        public bool EnableRequire { get; set; } = true;
        public LuauRequirer Requirer { get; set; }
        public Action<string> Log { get; set; }
    }

    public static class LuauUnity
    {
        public static LuauState CreateState(LuauUnityOptions options = null)
        {
            options = options ?? new LuauUnityOptions();

            var state = LuauState.Create();
            try
            {
                if (options.OpenStandardLibraries)
                {
                    OpenUnityStandardLibraries(state);
                }

                if (options.OpenDebugLibrary)
                {
                    state.OpenDebugLibrary();
                }

                RegisterPrint(state, options.Log);

                if (options.EnableRequire)
                {
                    state.OpenRequireLibrary(options.Requirer ?? ResourcesLuauRequirer.Default);
                }

                return state;
            }
            catch
            {
                state.Dispose();
                throw;
            }
        }

        public static void RegisterPrint(LuauState state, Action<string> log = null)
        {
            log = log ?? Debug.Log;

            state["print"] = state.CreateFunction(l =>
            {
                var top = l.GetTop();
                if (top == 0)
                {
                    log(string.Empty);
                    return 0;
                }

                var parts = new string[top];
                for (var i = 1; i <= top; i++)
                {
                    parts[i - 1] = ToDisplayString(l, i);
                }

                log(string.Join("\t", parts));
                return 0;
            });
        }

        static void OpenUnityStandardLibraries(LuauState state)
        {
            state.OpenBaseLibrary();
            state.OpenMathLibrary();
            state.OpenTableLibrary();
            state.OpenStringLibrary();
            state.OpenCoroutineLibrary();
            state.OpenBit32Library();
            state.OpenUtf8Library();
            state.OpenBufferLibrary();
            state.OpenVectorLibrary();
        }

        static unsafe string ToDisplayString(LuauState state, int index)
        {
            var ptr = Luau.Native.NativeMethods.luaL_tolstring(state.AsPointer(), index, null);
            try
            {
                return Marshal.PtrToStringAnsi((IntPtr)ptr) ?? string.Empty;
            }
            finally
            {
                state.Pop();
            }
        }
    }
}
