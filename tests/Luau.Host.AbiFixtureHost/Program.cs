using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Luau;

namespace Luau.Host.AbiFixtureHost;

internal static class Program
{
    const string HostLogicalName = "luau_host";

    static readonly FixtureCase[] FixtureCases =
    [
        new("wrong_magic", "ABI magic"),
        new("wrong_major", "host ABI is"),
        new("missing_required_feature", "host-owned compiler buffers"),
        new("wrong_pointer_size", "pointer size"),
        new("wrong_compile_options_size", "compile-options size"),
        new("wrong_callback_table_size", "callback-table size"),
        new("shifted_tags", "nil type tag"),
        new("truncated_record", "ABI record"),
    ];

    static readonly string[] Operations = ["compiler", "state"];

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["--fixtures", var fixtureRoot])
            {
                await RunFixtureSuiteAsync(fixtureRoot).ConfigureAwait(false);
                return 0;
            }

            if (args is ["--verify", var fixtureName, var operation])
            {
                VerifyFixture(fixtureName, operation);
                return 0;
            }

            Console.Error.WriteLine(
                "Usage: Luau.Host.AbiFixtureHost --fixtures <native-fixture-directory>");
            return 64;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    static async Task RunFixtureSuiteAsync(string fixtureRoot)
    {
        fixtureRoot = Path.GetFullPath(fixtureRoot);
        if (!Directory.Exists(fixtureRoot))
        {
            throw new DirectoryNotFoundException(
                $"Invalid ABI fixture directory does not exist: {fixtureRoot}");
        }

        foreach (var fixtureCase in FixtureCases)
        {
            var fixturePath = FindFixture(fixtureRoot, fixtureCase.Name);
            foreach (var operation in Operations)
            {
                await RunIsolatedFixtureAsync(fixtureCase, fixturePath, operation).ConfigureAwait(false);
            }
        }

        Console.WriteLine(
            $"PASS: {FixtureCases.Length} malformed ABI fixtures rejected in " +
            $"{FixtureCases.Length * Operations.Length} isolated processes before compiler/state entry.");
    }

    static string FindFixture(string fixtureRoot, string fixtureName)
    {
        var nativeFileName = GetNativeFileName($"luau_host_invalid_abi_{fixtureName}");
        var matches = Directory.GetFiles(
            fixtureRoot,
            nativeFileName,
            SearchOption.AllDirectories);

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException(
                $"Missing invalid ABI fixture '{nativeFileName}' beneath {fixtureRoot}."),
            _ => throw new InvalidOperationException(
                $"Expected one invalid ABI fixture '{nativeFileName}' beneath {fixtureRoot}, " +
                $"but found: {string.Join(", ", matches)}"),
        };
    }

    static async Task RunIsolatedFixtureAsync(
        FixtureCase fixtureCase,
        string fixturePath,
        string operation)
    {
        var isolatedDirectory = Path.Combine(
            Path.GetTempPath(),
            "luau-host-invalid-abi",
            $"{fixtureCase.Name}-{operation}-{Guid.NewGuid():N}");

        try
        {
            CopyDirectory(AppContext.BaseDirectory, isolatedDirectory);
            InstallFixture(fixturePath, isolatedDirectory);

            using var process = new Process
            {
                StartInfo = CreateChildStartInfo(
                    isolatedDirectory,
                    fixtureCase.Name,
                    operation),
            };

            if (!process.Start())
            {
                throw new InvalidOperationException(
                    $"Failed to start isolated verification for {fixtureCase.Name}/{operation}.");
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException(
                    $"Timed out verifying {fixtureCase.Name}/{operation}.");
            }

            var output = await standardOutput.ConfigureAwait(false);
            var error = await standardError.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Invalid ABI verification failed for {fixtureCase.Name}/{operation} " +
                    $"with exit code {process.ExitCode}.{Environment.NewLine}" +
                    $"stdout:{Environment.NewLine}{output}" +
                    $"stderr:{Environment.NewLine}{error}");
            }

            Console.Write(output);
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.Error.Write(error);
            }
        }
        finally
        {
            TryDeleteDirectory(isolatedDirectory);
        }
    }

    static ProcessStartInfo CreateChildStartInfo(
        string isolatedDirectory,
        string fixtureName,
        string operation)
    {
        var entryAssemblyPath = Path.Combine(
            isolatedDirectory,
            Path.GetFileName(Assembly.GetEntryAssembly()!.Location));
        var currentProcessPath = Environment.ProcessPath ??
            throw new InvalidOperationException("The current process path is unavailable.");
        var currentProcessName = Path.GetFileNameWithoutExtension(currentProcessPath);

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = isolatedDirectory,
        };

        if (string.Equals(currentProcessName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = currentProcessPath;
            startInfo.ArgumentList.Add(entryAssemblyPath);
        }
        else
        {
            var copiedAppHost = Path.Combine(
                isolatedDirectory,
                Path.GetFileName(currentProcessPath));
            if (File.Exists(copiedAppHost))
            {
                startInfo.FileName = copiedAppHost;
            }
            else
            {
                startInfo.FileName = "dotnet";
                startInfo.ArgumentList.Add(entryAssemblyPath);
            }
        }

        startInfo.ArgumentList.Add("--verify");
        startInfo.ArgumentList.Add(fixtureName);
        startInfo.ArgumentList.Add(operation);
        return startInfo;
    }

    static void VerifyFixture(string fixtureName, string operation)
    {
        var fixtureCase = FixtureCases.SingleOrDefault(
            fixture => string.Equals(fixture.Name, fixtureName, StringComparison.Ordinal));
        if (fixtureCase == default)
        {
            throw new ArgumentException($"Unknown invalid ABI fixture: {fixtureName}", nameof(fixtureName));
        }

        var diagnostic = operation switch
        {
            "compiler" => CaptureAbiRejection(
                () => _ = LuauCompiler.Compile("return 42"u8)),
            "state" => CaptureAbiRejection(
                () =>
                {
                    using var state = LuauState.Create();
                }),
            _ => throw new ArgumentException($"Unknown operation: {operation}", nameof(operation)),
        };

        if (!diagnostic.Contains(fixtureCase.DiagnosticFragment, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Fixture {fixtureName}/{operation} rejected with an unexpected diagnostic. " +
                $"Expected '{fixtureCase.DiagnosticFragment}', received: {diagnostic}");
        }

        var hostPath = GetResolverHostPath(AppContext.BaseDirectory);
        var handle = NativeLibrary.Load(hostPath);
        try
        {
            var abiQueries = ReadCounter(handle, "luau_host_fixture_get_abi_query_count");
            var compilerCalls = ReadCounter(handle, "luau_host_fixture_get_compile_count");
            var stateCalls = ReadCounter(handle, "luau_host_fixture_get_state_create_count");

            if (abiQueries == 0)
            {
                throw new InvalidOperationException(
                    $"Fixture {fixtureName}/{operation} was rejected without querying its ABI record.");
            }
            if (compilerCalls != 0 || stateCalls != 0)
            {
                throw new InvalidOperationException(
                    $"Fixture {fixtureName}/{operation} crossed the ABI boundary: " +
                    $"compile calls={compilerCalls}, state-create calls={stateCalls}.");
            }

            Console.WriteLine(
                $"PASS {fixtureName}/{operation}: ABI queries={abiQueries}, " +
                "compile calls=0, state-create calls=0");
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }

    static string CaptureAbiRejection(Action operation)
    {
        try
        {
            operation();
        }
        catch (PlatformNotSupportedException exception)
        {
            return exception.Message;
        }

        throw new InvalidOperationException(
            "The malformed ABI fixture reached a native operation instead of failing its handshake.");
    }

    static uint ReadCounter(nint handle, string exportName)
    {
        var function = NativeLibrary.GetExport(handle, exportName);
        return Marshal.GetDelegateForFunctionPointer<Counter>(function)();
    }

    static void InstallFixture(string fixturePath, string isolatedDirectory)
    {
        var resolverHostPath = GetResolverHostPath(isolatedDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(resolverHostPath)!);
        File.Copy(fixturePath, resolverHostPath, overwrite: true);

        // Debug builds resolve a same-directory native library before the RID
        // folder. Keep both locations byte-identical so the counter handle is
        // always opened against the module selected by the resolver.
        var baseHostPath = Path.Combine(
            isolatedDirectory,
            GetNativeFileName(HostLogicalName));
        File.Copy(fixturePath, baseHostPath, overwrite: true);
    }

    static string GetResolverHostPath(string baseDirectory)
    {
#if DEBUG
        return Path.Combine(baseDirectory, GetNativeFileName(HostLogicalName));
#else
        return Path.Combine(
            baseDirectory,
            "runtimes",
            GetRuntimeIdentifier(),
            "native",
            GetNativeFileName(HostLogicalName));
#endif
    }

    static string GetRuntimeIdentifier()
    {
        var operatingSystem = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            var unsupported => throw new PlatformNotSupportedException(
                $"Unsupported ABI fixture process architecture: {unsupported}"),
        };
        return $"{operatingSystem}-{architecture}";
    }

    static string GetNativeFileName(string logicalName)
    {
        if (OperatingSystem.IsWindows())
        {
            return $"{logicalName}.dll";
        }

        return OperatingSystem.IsMacOS()
            ? $"lib{logicalName}.dylib"
            : $"lib{logicalName}.so";
    }

    static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(
                destinationDirectory,
                Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var destination = Path.Combine(
                destinationDirectory,
                Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate uint Counter();

    readonly record struct FixtureCase(string Name, string DiagnosticFragment);
}
