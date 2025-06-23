#pragma warning disable CS8500
#pragma warning disable CS8981

using System;
using System.Runtime.InteropServices;

namespace Luau.Native
{
    // malloc/free

    unsafe partial class NativeMethods
    {
        [DllImport(__DllName, EntryPoint = "malloc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void* malloc(nuint size);

        [DllImport(__DllName, EntryPoint = "free", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void* free(void* free);
    }
}