#if NET8_0_OR_GREATER

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Luau.Native;

internal static class NativeMethodsDllImportResolver
{
    // https://docs.microsoft.com/en-us/dotnet/standard/native-interop/cross-platform
    // Library path will search
    // win => __DllName, __DllName.dll
    // linux, osx => __DllName.so, __DllName.dylib

    const string LogicalLibraryName = "luau_host";

    internal static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == LogicalLibraryName)
        {
            var physicalFileName = GetPhysicalFileName(libraryName);
#if DEBUG
            var combinedPath = Path.Combine(AppContext.BaseDirectory, physicalFileName);
            if (File.Exists(combinedPath))
            {
                return NativeLibrary.Load(combinedPath, assembly, searchPath);
            }
#endif

            var path = "runtimes/";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                path += "win-";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                path += "osx-";
            }
            else
            {
                path += "linux-";
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

            path += "/native/" + physicalFileName;

            var fullPath = Path.Combine(AppContext.BaseDirectory, path);

            if (File.Exists(fullPath))
            {
                return NativeLibrary.Load(fullPath, assembly, searchPath);
            }

            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    static string GetPhysicalFileName(string libraryName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return libraryName + ".dll";
        }

        var prefix = libraryName.StartsWith("lib", StringComparison.Ordinal) ? "" : "lib";
        var extension = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so";
        return prefix + libraryName + extension;
    }
}

internal static class NativeMethodsModuleInitializer
{
#pragma warning disable CA2255 // The harness library owns resolution of its bundled native runtime artifacts.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(NativeMethods).Assembly,
            NativeMethodsDllImportResolver.Resolve);
    }
}

#endif
