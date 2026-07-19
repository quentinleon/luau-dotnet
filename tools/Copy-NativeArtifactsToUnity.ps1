param(
    [ValidateSet("win-x64", "android-arm64", "android-x64")]
    [string[]] $Platform = @("win-x64", "android-arm64", "android-x64"),

    [switch] $Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$packageRoot = Join-Path $root "Luau.Unity"
$pluginsRoot = Join-Path $packageRoot "Runtime/Plugins"
$artifacts = @{
    "win-x64" = @{
        Source = "native/luau-host/out/install/windows-x64/luau_host.dll"
        Destination = "win-x64/luau_host.dll"
    }
    "android-arm64" = @{
        Source = "native/luau-host/out/install/android-arm64/libluau_host.so"
        Destination = "android-arm64/libluau_host.so"
    }
    "android-x64" = @{
        Source = "native/luau-host/out/install/android-x64/libluau_host.so"
        Destination = "android-x64/libluau_host.so"
    }
}

foreach ($target in $Platform) {
    $artifact = $artifacts[$target]
    $source = Join-Path $root $artifact.Source
    $destination = Join-Path $pluginsRoot $artifact.Destination
    $destinationMeta = "$destination.meta"

    if (!(Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing installed $target host artifact: $source. Build and install its CMake preset first."
    }
    if (!(Test-Path -LiteralPath $destinationMeta -PathType Leaf)) {
        throw "Missing Unity plugin importer metadata: $destinationMeta"
    }

    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ($Check) {
        if (!(Test-Path -LiteralPath $destination -PathType Leaf)) {
            throw "Missing Unity host artifact: $destination"
        }

        $destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($sourceHash -ne $destinationHash) {
            throw "Stale Unity host artifact: $destination does not match $source."
        }

        Write-Host "Current: $destination (SHA256=$destinationHash)"
        continue
    }

    Copy-Item -LiteralPath $source -Destination $destination -Force
    $destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    if ($sourceHash -ne $destinationHash) {
        throw "Copied Unity host artifact failed SHA256 verification: $destination"
    }

    Write-Host "Copied $source -> $destination (SHA256=$destinationHash)"
}

if ($Check) {
    Write-Host "Selected Unity native host artifacts are current."
} else {
    Write-Host "Selected Unity native host artifacts were refreshed without changing plugin importer metadata."
}
