#if NET8_0_OR_GREATER

using System.Reflection;
using System.Runtime.InteropServices;

namespace Luau.Native;

public partial class NativeMethods
{
    // https://docs.microsoft.com/en-us/dotnet/standard/native-interop/cross-platform
    // Library path will search
    // win => __DllName, __DllName.dll
    // linux, osx => __DllName.so, __DllName.dylib

    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, DllImportResolver);
    }

    static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == __DllName)
        {
#if DEBUG
            var combinedPath = Path.Combine(AppContext.BaseDirectory, libraryName);
            if (File.Exists(combinedPath) || File.Exists(combinedPath + ".dll"))
            {
                return NativeLibrary.Load(combinedPath, assembly, searchPath);
            }
#endif

            var path = "runtimes/";
            var extension = "";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                path += "win-";
                extension = ".dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                path += "osx-";
                extension = ".dylib";
            }
            else
            {
                path += "linux-";
                extension = ".so";
            }

            if (RuntimeInformation.OSArchitecture == Architecture.X86)
            {
                path += "x86";
            }
            else if (RuntimeInformation.OSArchitecture == Architecture.X64)
            {
                path += "x64";
            }
            else if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            {
                path += "arm64";
            }

            path += "/native/" + __DllName + extension;

            return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, path), assembly, searchPath);
        }
        else if (string.Equals(libraryName, "libc", StringComparison.OrdinalIgnoreCase))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (NativeLibrary.TryLoad("ucrtbase.dll", assembly, searchPath, out var handle) ||
                    NativeLibrary.TryLoad("msvcrt.dll", assembly, searchPath, out handle))
                {
                    return handle;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return NativeLibrary.Load("libSystem.B.dylib", assembly, searchPath);
            }
            else
            {
                if (NativeLibrary.TryLoad("libc.so.6", assembly, searchPath, out var handle) ||
                    NativeLibrary.TryLoad("libc.so", assembly, searchPath, out handle))
                {
                    return handle;
                }
            }
        }

        return IntPtr.Zero;
    }
}

#endif
