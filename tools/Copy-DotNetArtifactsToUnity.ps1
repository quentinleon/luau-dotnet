param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$runtimeDir = Join-Path $root "src/Luau.Unity/Assets/Luau.Unity/Runtime"
$nativeDir = Join-Path $root "src/Luau.Unity/Assets/Luau.Unity/Native"

$artifacts = @(
    @{
        Source = "src/Luau/bin/$Configuration/netstandard2.1/Luau.dll"
        Destination = "src/Luau.Unity/Assets/Luau.Unity/Runtime/Luau.dll"
    },
    @{
        Source = "src/Luau.SourceGenerator/bin/$Configuration/netstandard2.0/Luau.SourceGenerator.dll"
        Destination = "src/Luau.Unity/Assets/Luau.Unity/Runtime/Luau.SourceGenerator.dll"
    },
    @{
        Source = "src/Luau.Native/Delegates.cs"
        Destination = "src/Luau.Unity/Assets/Luau.Unity/Native/Delegates.cs"
    },
    @{
        Source = "src/Luau.Native/Enums.cs"
        Destination = "src/Luau.Unity/Assets/Luau.Unity/Native/Enums.cs"
    },
    @{
        Source = "src/Luau.Native/NativeMethods.Macros.cs"
        Destination = "src/Luau.Unity/Assets/Luau.Unity/Native/NativeMethods.Macros.cs"
    },
    @{
        Source = "src/Luau.Native/NativeMethods.Memory.cs"
        Destination = "src/Luau.Unity/Assets/Luau.Unity/Native/NativeMethods.Memory.cs"
    }
)

foreach ($artifact in $artifacts) {
    $source = Join-Path $root $artifact.Source
    $destination = Join-Path $root $artifact.Destination

    if (!(Test-Path -LiteralPath $source)) {
        throw "Missing artifact: $source. Build the relevant .NET project first."
    }

    Copy-Item -LiteralPath $source -Destination $destination -Force
    Write-Host "Copied $($artifact.Source) -> $($artifact.Destination)"
}

Write-Host "Unity package artifacts updated in $runtimeDir and $nativeDir."
Write-Host "Generated Unity native bindings are emitted by native/luau-ffi/build.rs and are not overwritten by this script."
