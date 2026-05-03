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
#if NET8_0_OR_GREATER
            "libc";
#else
            __DllName;
#endif

        [DllImport(C_RUNTIME_LIB, EntryPoint = "malloc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void* malloc(nuint size);

        [DllImport(C_RUNTIME_LIB, EntryPoint = "free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void free(void* free);
    }
}
