param(
    [ValidateSet("win-x64", "android-arm64", "android-x64")]
    [string[]] $Platform = @("win-x64", "android-arm64", "android-x64"),

    [switch] $Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$packageRoot = Join-Path $root "Luau.Unity"
$pluginsRoot = Join-Path $packageRoot "Runtime/Plugins"
$hostRoot = Join-Path $root "native/luau-host"
$outRoot = Join-Path $hostRoot "out"
$policyPath = Join-Path $PSScriptRoot "UnityPackageReleasePolicy.json"
$manifestScript = Join-Path $hostRoot "cmake/Write-ArtifactManifest.ps1"
$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
$sourceCommit = (& git -C $root rev-parse HEAD 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "Unable to resolve the source commit for release artifact manifests: $sourceCommit"
}
$sourceCommit = $sourceCommit.ToLowerInvariant()

$artifacts = @{
    "win-x64" = @{
        Preset = "windows-x64"
        Source = "native/luau-host/out/install/windows-x64/luau_host.dll"
        Destination = "win-x64/luau_host.dll"
        Strip = $false
        AndroidApi = 0
    }
    "android-arm64" = @{
        Preset = "android-arm64"
        Source = "native/luau-host/out/install/android-arm64/libluau_host.so"
        Destination = "android-arm64/libluau_host.so"
        Strip = $true
        AndroidApi = 26
    }
    "android-x64" = @{
        Preset = "android-x64"
        Source = "native/luau-host/out/install/android-x64/libluau_host.so"
        Destination = "android-x64/libluau_host.so"
        Strip = $true
        AndroidApi = 26
    }
}

function Get-CMakeCacheValue([string] $CachePath, [string] $Name) {
    if (!(Test-Path -LiteralPath $CachePath -PathType Leaf)) {
        throw "Missing configured CMake cache: $CachePath"
    }
    $cache = Get-Content -LiteralPath $CachePath -Raw
    $match = [Regex]::Match($cache, "(?m)^" + [Regex]::Escape($Name) + ":[^=]+=(.+)$")
    if (!$match.Success) {
        throw "CMake cache '$CachePath' does not define $Name."
    }
    return $match.Groups[1].Value.Trim()
}

function Get-PolicyBudget([string] $RelativePath) {
    $entry = @($policy.artifacts | Where-Object { $_.path -ceq $RelativePath })
    if ($entry.Count -ne 1) {
        throw "Release policy must contain exactly one budget for '$RelativePath'."
    }
    return [long]$entry[0].maximumBytes
}

function Assert-FileHashEqual(
    [string] $Description,
    [string] $ActualPath,
    [string] $ExpectedPath) {
    if (!(Test-Path -LiteralPath $ActualPath -PathType Leaf)) {
        throw "$Description is missing: $ActualPath"
    }
    $actualHash = (Get-FileHash -LiteralPath $ActualPath -Algorithm SHA256).Hash
    $expectedHash = (Get-FileHash -LiteralPath $ExpectedPath -Algorithm SHA256).Hash
    if ($actualHash -cne $expectedHash) {
        throw "$Description is stale. Expected SHA256 $expectedHash, found $actualHash."
    }
    return $actualHash
}

function Assert-NdkRevision([string] $NdkRoot) {
    $sourceProperties = Join-Path $NdkRoot "source.properties"
    if (!(Test-Path -LiteralPath $sourceProperties -PathType Leaf)) {
        throw "Android NDK source.properties is missing: $sourceProperties"
    }
    $text = Get-Content -LiteralPath $sourceProperties -Raw
    $match = [Regex]::Match($text, '(?m)^Pkg\.Revision\s*=\s*([^\r\n]+)\s*$')
    if (!$match.Success) {
        throw "Unable to read the Android NDK revision from $sourceProperties"
    }
    $actual = $match.Groups[1].Value.Trim()
    if ($actual -cne [string]$policy.androidNdkRevision) {
        throw "Android NDK revision is '$actual'; expected '$($policy.androidNdkRevision)'."
    }
}

function Assert-AndroidElfHardening([string] $ReadElf, [string] $BinaryPath) {
    $output = (& $ReadElf --program-headers --dynamic $BinaryPath 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "llvm-readelf failed for $BinaryPath`n$output"
    }
    if ($output -notmatch '(?m)^\s*GNU_RELRO\s') {
        throw "Android shipping artifact is missing GNU_RELRO: $BinaryPath"
    }
    $stack = [Regex]::Match($output, '(?m)^\s*GNU_STACK\s+.*$')
    if (!$stack.Success -or $stack.Value -notmatch '\sRW\s' -or $stack.Value -match '\sRWE\s') {
        throw "Android shipping artifact does not have a non-executable GNU_STACK: $BinaryPath"
    }
    if ($output -notmatch '(?m)\(FLAGS\).*BIND_NOW|\(FLAGS_1\).*\bNOW\b') {
        throw "Android shipping artifact is missing immediate binding: $BinaryPath"
    }
}

function Write-ShippingManifest(
    [string] $Target,
    [string] $SourcePath,
    [string] $ShippingPath,
    [string] $ImporterMetaPath,
    [string] $AuditManifestPath,
    [string] $OutputPath,
    [bool] $WasStripped,
    [long] $MaximumBytes,
    [int] $AndroidApi) {
    $audit = Get-Content -LiteralPath $AuditManifestPath -Raw | ConvertFrom-Json
    if ($audit.schema_version -ne 3 -or
        $audit.source_commit -cne $sourceCommit -or
        $null -eq $audit.toolchain) {
        throw "The $Target audit manifest is missing synchronized source/toolchain provenance."
    }
    $record = [ordered]@{
        schema_version = 2
        platform = $Target
        source_commit = $sourceCommit
        source_tree_clean = [bool]$audit.source_tree_clean
        toolchain = $audit.toolchain
        deterministic_transform = if ($WasStripped) {
            "llvm-strip --strip-unneeded --remove-section=.comment"
        } else {
            "identity-copy"
        }
        unstripped_input = [ordered]@{
            file = [IO.Path]::GetFileName($SourcePath)
            bytes = (Get-Item -LiteralPath $SourcePath).Length
            sha256 = (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        shipping_output = [ordered]@{
            file = [IO.Path]::GetFileName($ShippingPath)
            bytes = (Get-Item -LiteralPath $ShippingPath).Length
            maximum_bytes = $MaximumBytes
            sha256 = (Get-FileHash -LiteralPath $ShippingPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        unity_importer_meta_sha256 = (
            Get-FileHash -LiteralPath $ImporterMetaPath -Algorithm SHA256).Hash.ToLowerInvariant()
        audited_manifest_sha256 = (
            Get-FileHash -LiteralPath $AuditManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        audited_manifest = $audit
    }
    if ($WasStripped) {
        $record["android_api"] = $AndroidApi
        $record["android_ndk_revision"] = [string]$policy.androidNdkRevision
    }
    [IO.File]::WriteAllText(
        $OutputPath,
        ($record | ConvertTo-Json -Depth 14 -Compress) + "`n",
        [Text.UTF8Encoding]::new($false))
}

function Write-SymbolManifest(
    [string] $Target,
    [string] $SourcePath,
    [string] $ShippingPath,
    [string] $AuditManifestPath,
    [object[]] $SymbolFiles,
    [string] $OutputPath) {
    $symbols = @(
        foreach ($symbolFile in $SymbolFiles) {
            [ordered]@{
                file = [IO.Path]::GetFileName($symbolFile)
                bytes = (Get-Item -LiteralPath $symbolFile).Length
                sha256 = (Get-FileHash -LiteralPath $symbolFile -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    )
    $audit = Get-Content -LiteralPath $AuditManifestPath -Raw | ConvertFrom-Json
    if ($audit.schema_version -ne 3 -or
        $audit.source_commit -cne $sourceCommit -or
        $null -eq $audit.toolchain) {
        throw "The $Target audit manifest is missing synchronized source/toolchain provenance."
    }
    $record = [ordered]@{
        schema_version = 2
        platform = $Target
        source_commit = $sourceCommit
        source_tree_clean = [bool]$audit.source_tree_clean
        toolchain = $audit.toolchain
        unstripped_input = [ordered]@{
            file = [IO.Path]::GetFileName($SourcePath)
            bytes = (Get-Item -LiteralPath $SourcePath).Length
            sha256 = (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        shipping_output = [ordered]@{
            file = [IO.Path]::GetFileName($ShippingPath)
            bytes = (Get-Item -LiteralPath $ShippingPath).Length
            sha256 = (Get-FileHash -LiteralPath $ShippingPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        audited_manifest_sha256 = (
            Get-FileHash -LiteralPath $AuditManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        symbols = $symbols
    }
    [IO.File]::WriteAllText(
        $OutputPath,
        ($record | ConvertTo-Json -Depth 8 -Compress) + "`n",
        [Text.UTF8Encoding]::new($false))
}

foreach ($target in $Platform) {
    $artifact = $artifacts[$target]
    $source = Join-Path $root $artifact.Source
    $destination = Join-Path $pluginsRoot $artifact.Destination
    $destinationMeta = "$destination.meta"
    $packageRelativePath = "Runtime/Plugins/" + $artifact.Destination
    $budget = Get-PolicyBudget $packageRelativePath

    if (!(Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing installed $target host artifact: $source. Build and install its CMake preset first."
    }
    if (!(Test-Path -LiteralPath $destinationMeta -PathType Leaf)) {
        throw "Missing Unity plugin importer metadata: $destinationMeta"
    }

    $shippingRootName = if ($Check) { "shipping-check" } else { "shipping" }
    $shippingDirectory = Join-Path $outRoot "$shippingRootName/$target"
    [IO.Directory]::CreateDirectory($shippingDirectory) | Out-Null
    $shippingPath = Join-Path $shippingDirectory ([IO.Path]::GetFileName($destination))
    $shippingManifest = Join-Path $shippingDirectory "luau_host.shipping.manifest.json"
    $auditManifest = Join-Path $shippingDirectory "luau_host.audit.manifest.json"

    if ($artifact.Strip) {
        $cachePath = Join-Path $hostRoot "out/build/$($artifact.Preset)/CMakeCache.txt"
        $strip = Get-CMakeCacheValue $cachePath "CMAKE_STRIP"
        $objcopy = Get-CMakeCacheValue $cachePath "CMAKE_OBJCOPY"
        $readelf = Get-CMakeCacheValue $cachePath "CMAKE_READELF"
        $ndkRoot = Get-CMakeCacheValue $cachePath "CMAKE_ANDROID_NDK"
        foreach ($tool in @($strip, $objcopy, $readelf)) {
            if (!(Test-Path -LiteralPath $tool -PathType Leaf)) {
                throw "Pinned NDK tool is missing: $tool"
            }
        }
        Assert-NdkRevision $ndkRoot

        $stripOutput = (& $strip --strip-unneeded --remove-section=.comment -o $shippingPath $source 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to strip $target shipping artifact.`n$stripOutput"
        }
        $determinismPath = $shippingPath + ".determinism-check"
        try {
            $secondStripOutput = (& $strip --strip-unneeded --remove-section=.comment -o $determinismPath $source 2>&1 | Out-String)
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to repeat $target stripping for determinism.`n$secondStripOutput"
            }
            Assert-FileHashEqual "Deterministic $target strip output" $determinismPath $shippingPath | Out-Null
        }
        finally {
            if (Test-Path -LiteralPath $determinismPath -PathType Leaf) {
                [IO.File]::Delete($determinismPath)
            }
        }
        Assert-AndroidElfHardening $readelf $shippingPath

        if (!$Check) {
            $symbolsDirectory = Join-Path $outRoot "symbols/$target"
            [IO.Directory]::CreateDirectory($symbolsDirectory) | Out-Null
            $unstrippedPath = Join-Path $symbolsDirectory "libluau_host.unstripped.so"
            $debugPath = Join-Path $symbolsDirectory "libluau_host.so.debug"
            Copy-Item -LiteralPath $source -Destination $unstrippedPath -Force
            $objcopyOutput = (& $objcopy --only-keep-debug $source $debugPath 2>&1 | Out-String)
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to extract $target debug symbols.`n$objcopyOutput"
            }
        }

        & $manifestScript `
            -BinaryPath $shippingPath `
            -Platform $target `
            -OutputPath $auditManifest `
            -Configuration Release `
            -AndroidApi ([int]$artifact.AndroidApi) `
            -AndroidNdk ([string]$policy.androidNdkRevision)
        if ($LASTEXITCODE -ne 0) {
            throw "Shipping artifact ABI/export/identity audit failed for $target."
        }
    }
    else {
        Copy-Item -LiteralPath $source -Destination $shippingPath -Force
        & $manifestScript `
            -BinaryPath $shippingPath `
            -Platform $target `
            -OutputPath $auditManifest `
            -Configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw "Shipping artifact ABI/export/identity audit failed for $target."
        }

        if (!$Check) {
            $pdbPath = Join-Path $hostRoot "out/build/windows-x64/Release/luau_host.pdb"
            if (!(Test-Path -LiteralPath $pdbPath -PathType Leaf)) {
                throw "Windows Release symbols are missing: $pdbPath"
            }
            $symbolsDirectory = Join-Path $outRoot "symbols/$target"
            [IO.Directory]::CreateDirectory($symbolsDirectory) | Out-Null
            Copy-Item -LiteralPath $pdbPath -Destination (
                Join-Path $symbolsDirectory "luau_host.pdb") -Force
        }
    }

    Write-ShippingManifest `
        -Target $target `
        -SourcePath $source `
        -ShippingPath $shippingPath `
        -ImporterMetaPath $destinationMeta `
        -AuditManifestPath $auditManifest `
        -OutputPath $shippingManifest `
        -WasStripped ([bool]$artifact.Strip) `
        -MaximumBytes $budget `
        -AndroidApi ([int]$artifact.AndroidApi)

    if (!$Check) {
        $symbolsDirectory = Join-Path $outRoot "symbols/$target"
        $symbolFiles = if ($artifact.Strip) {
            @(
                (Join-Path $symbolsDirectory "libluau_host.unstripped.so"),
                (Join-Path $symbolsDirectory "libluau_host.so.debug")
            )
        }
        else {
            @((Join-Path $symbolsDirectory "luau_host.pdb"))
        }
        Write-SymbolManifest `
            -Target $target `
            -SourcePath $source `
            -ShippingPath $shippingPath `
            -AuditManifestPath $auditManifest `
            -SymbolFiles $symbolFiles `
            -OutputPath (Join-Path $symbolsDirectory "luau_host.symbols.manifest.json")
    }

    $shippingLength = (Get-Item -LiteralPath $shippingPath).Length
    if ($shippingLength -gt $budget) {
        throw "$target shipping artifact is $shippingLength bytes; reviewed budget is $budget."
    }

    if ($Check) {
        $hash = Assert-FileHashEqual "Unity $target shipping artifact" $destination $shippingPath
        Write-Host "Current stripped/audited artifact: $destination (bytes=$shippingLength, SHA256=$hash)"
        continue
    }

    $shippingHash = (Get-FileHash -LiteralPath $shippingPath -Algorithm SHA256).Hash
    $destinationHash = if (Test-Path -LiteralPath $destination -PathType Leaf) {
        (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    } else {
        ""
    }
    if ($destinationHash -cne $shippingHash) {
        Copy-Item -LiteralPath $shippingPath -Destination $destination -Force
    }
    $hash = Assert-FileHashEqual "Copied Unity $target shipping artifact" $destination $shippingPath
    Write-Host "Copied audited shipping artifact -> $destination (bytes=$shippingLength, SHA256=$hash)"
}

if ($Check) {
    Write-Host "Selected Unity native shipping artifacts are current, within budgets, and independently audited."
}
else {
    Write-Host "Selected Unity native shipping artifacts were refreshed without changing plugin importer metadata."
    Write-Host "Unstripped Android libraries and extracted symbols are under native/luau-host/out/symbols."
}
