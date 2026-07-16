param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [switch] $Check
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$runtimeDir = Join-Path $root "src/Luau.Unity/Assets/Luau.Unity/Runtime"

$artifacts = @(
    @{
        Source = "src/Luau/bin/$Configuration/netstandard2.1/Luau.dll"
        Destination = "src/Luau.Unity/Assets/Luau.Unity/Runtime/Luau.dll"
    },
    @{
        Source = "src/Luau.SourceGenerator/bin/$Configuration/netstandard2.0/Luau.SourceGenerator.dll"
        Destination = "src/Luau.Unity/Assets/Luau.Unity/Runtime/Luau.SourceGenerator.dll"
    }
)

foreach ($artifact in $artifacts) {
    $source = Join-Path $root $artifact.Source
    $destination = Join-Path $root $artifact.Destination

    if (!(Test-Path -LiteralPath $source)) {
        throw "Missing artifact: $source. Build the relevant .NET project first."
    }

    if ($Check) {
        if (!(Test-Path -LiteralPath $destination -PathType Leaf)) {
            throw "Missing Unity artifact: $destination. Refresh the managed package artifacts."
        }

        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($sourceHash -ne $destinationHash) {
            throw "Stale Unity artifact: $($artifact.Destination) does not match $($artifact.Source)."
        }

        Write-Host "Current: $($artifact.Destination)"
    }
    else {
        Copy-Item -LiteralPath $source -Destination $destination -Force
        Write-Host "Copied $($artifact.Source) -> $($artifact.Destination)"
    }
}

if ($Check) {
    Write-Host "Unity managed artifacts are current."
}
else {
    Write-Host "Unity package artifacts updated in $runtimeDir."
}
Write-Host "Native luau_host plugins are built and installed separately through native/luau-host CMake presets."
