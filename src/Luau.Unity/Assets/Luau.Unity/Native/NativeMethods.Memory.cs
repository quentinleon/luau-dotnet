#pragma warning disable CS8500
#pragma warning disable CS8981

using System;
using System.Runtime.InteropServices;

namespace Luau.Native
{
    // malloc/realloc/free

    unsafe partial class NativeMethods
    {
        const string C_RUNTIME_LIB =
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            "ucrtbase";
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            "libSystem.B.dylib";
#elif UNITY_ANDROID || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX || NET8_0_OR_GREATER
            "libc";
#else
            __DllName;
#endif

        [DllImport(C_RUNTIME_LIB, EntryPoint = "malloc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void* malloc(nuint size);

        [DllImport(C_RUNTIME_LIB, EntryPoint = "realloc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void* realloc(void* block, nuint size);

        [DllImport(C_RUNTIME_LIB, EntryPoint = "free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void free(void* free);
    }
}
