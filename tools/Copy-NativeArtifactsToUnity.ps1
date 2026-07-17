param(
    [ValidateSet("win-x64", "android-arm64", "android-x64")]
    [string[]] $Platform = @("win-x64", "android-arm64", "android-x64"),

    [switch] $Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$artifacts = @{
    "win-x64" = @{
        Source = "native/luau-host/out/install/windows-x64/luau_host.dll"
        Destination = "src/Luau.Unity/Assets/Luau.Unity/Interop/Plugins/win-x64/luau_host.dll"
    }
    "android-arm64" = @{
        Source = "native/luau-host/out/install/android-arm64/libluau_host.so"
        Destination = "src/Luau.Unity/Assets/Luau.Unity/Interop/Plugins/android-arm64/libluau_host.so"
    }
    "android-x64" = @{
        Source = "native/luau-host/out/install/android-x64/libluau_host.so"
        Destination = "src/Luau.Unity/Assets/Luau.Unity/Interop/Plugins/android-x64/libluau_host.so"
    }
}

foreach ($target in $Platform) {
    $artifact = $artifacts[$target]
    $source = Join-Path $root $artifact.Source
    $destination = Join-Path $root $artifact.Destination
    $destinationMeta = "$destination.meta"

    if (!(Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing installed $target host artifact: $source. Build and install its CMake preset first."
    }
    if (!(Test-Path -LiteralPath $destinationMeta -PathType Leaf)) {
        throw "Missing Unity plugin importer metadata: $destinationMeta"
    }

    if ($Check) {
        if (!(Test-Path -LiteralPath $destination -PathType Leaf)) {
            throw "Missing Unity host artifact: $destination"
        }

        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($sourceHash -ne $destinationHash) {
            throw "Stale Unity host artifact: $($artifact.Destination) does not match $($artifact.Source)."
        }

        Write-Host "Current: $($artifact.Destination)"
        continue
    }

    Copy-Item -LiteralPath $source -Destination $destination -Force
    Write-Host "Copied $($artifact.Source) -> $($artifact.Destination)"
}

if ($Check) {
    Write-Host "Selected Unity native host artifacts are current."
} else {
    Write-Host "Selected Unity native host artifacts were refreshed without changing plugin importer metadata."
}
