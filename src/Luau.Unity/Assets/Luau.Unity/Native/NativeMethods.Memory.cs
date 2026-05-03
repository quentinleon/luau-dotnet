#pragma warning disable CS8500
#pragma warning disable CS8981

using System;
using System.Runtime.InteropServices;

namespace Luau.Native
{
    // malloc/free

    unsafe partial class NativeMethods
    {
        const string C_RUNTIME_LIB =
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            "ucrtbase";
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            "libSystem.B.dylib";
#else
            "libc";
#endif

        [DllImport(C_RUNTIME_LIB, EntryPoint = "malloc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void* malloc(nuint size);

        [DllImport(C_RUNTIME_LIB, EntryPoint = "free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void free(void* free);
    }
}
