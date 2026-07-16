<#
.SYNOPSIS
Writes the reviewed luau_host artifact and ABI manifest used by CI.

.DESCRIPTION
Derives ABI metadata from luau_host.h, the native layout assertions, the
pinned Luau headers, and the exact source hashes used by CMake. The script
fails instead of publishing an artifact when those values drift from the
approved Release fingerprint or the managed verifier constants.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BinaryPath,

    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64", "android-arm64", "android-x64")]
    [string] $Platform,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [ValidateSet("Release")]
    [string] $Configuration = "Release",

    [int] $AndroidApi = 0,

    [string] $AndroidNdk = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ApprovedBuildInputSha256 = "ac6eeae2677fe19905718a4cfe5b6bf2709920cdae78e0ba035d88d6ff2c7b0e"
$ApprovedUpstreamRevisionHash = [Convert]::ToUInt64("c45f010aabf167ac", 16)
$ApprovedHostBuildFingerprint = [Convert]::ToUInt64("105716f226c3f69f", 16)
$ApprovedFeatureFlags = [uint32]0x1ff
$ApprovedExportCount = 85

function Get-RequiredMatchValue {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Text,
        [Parameter(Mandatory = $true)]
        [string] $Pattern,
        [Parameter(Mandatory = $true)]
        [string] $Description,
        [string] $Group = "1"
    )

    $match = [regex]::Match($Text, $Pattern)
    if (!$match.Success) {
        throw "Unable to derive $Description."
    }

    return $match.Groups[$Group].Value
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Description,
        [Parameter(Mandatory = $true)]
        [object] $Actual,
        [Parameter(Mandatory = $true)]
        [object] $Expected
    )

    if ($Actual -ne $Expected) {
        throw "$Description is '$Actual'; expected '$Expected'."
    }
}

function Format-Hex32 {
    param([uint32] $Value)
    return "0x{0:x8}" -f $Value
}

function Format-Hex64 {
    param([uint64] $Value)
    return "0x{0:x16}" -f $Value
}

function Get-LowerFileSha256 {
    param([string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-TextSha256 {
    param([string] $Text)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        return [BitConverter]::ToString($sha256.ComputeHash($bytes)).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-Fnv1a64 {
    param([string] $Text)

    [System.Numerics.BigInteger] $value = [uint64]14695981039346656037
    [System.Numerics.BigInteger] $prime = [uint64]1099511628211
    [System.Numerics.BigInteger] $modulus = [System.Numerics.BigInteger]::Pow(2, 64)

    foreach ($byte in [Text.Encoding]::UTF8.GetBytes($Text)) {
        $value = (($value -bxor [System.Numerics.BigInteger]$byte) * $prime) % $modulus
    }

    return [uint64]$value
}

function Get-EnumValues {
    param([string] $Body)

    $values = @{}
    $current = -1
    $withoutComments = [regex]::Replace($Body, "//[^`r`n]*", "")

    foreach ($entry in ($withoutComments -split ",")) {
        $match = [regex]::Match(
            $entry,
            "(?<name>LUA_T[A-Z0-9_]+)\s*(?:=\s*(?<assigned>LUA_T[A-Z0-9_]+|-?\d+))?")
        if (!$match.Success) {
            continue
        }

        $assigned = $match.Groups["assigned"].Value
        if ([string]::IsNullOrEmpty($assigned)) {
            $current++
        }
        elseif ($assigned -match "^-?\d+$") {
            $current = [int]$assigned
        }
        elseif ($values.ContainsKey($assigned)) {
            $current = [int]$values[$assigned]
        }
        else {
            throw "Enum value '$($match.Groups['name'].Value)' references unknown value '$assigned'."
        }

        $values[$match.Groups["name"].Value] = $current
    }

    return $values
}

$hostRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $hostRoot "../.."))
$headerPath = Join-Path $hostRoot "include/luau_host.h"
$sourcePath = Join-Path $hostRoot "src/luau_host.cpp"
$exportsPath = Join-Path $hostRoot "exports/luau_host.exports"
$cmakePath = Join-Path $hostRoot "CMakeLists.txt"
$luauHeaderPath = Join-Path $repositoryRoot "luau/VM/include/lua.h"
$managedProtectionPath = Join-Path $repositoryRoot "src/Luau/Internal/LuauNativeProtection.cs"
$managedTypesPath = Join-Path $repositoryRoot "src/Luau.Unity/Assets/Luau.Unity/Interop/NativeTypes.cs"

foreach ($requiredPath in @(
    $headerPath,
    $sourcePath,
    $exportsPath,
    $cmakePath,
    $luauHeaderPath,
    $managedProtectionPath,
    $managedTypesPath)) {
    if (!(Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required manifest input does not exist: $requiredPath"
    }
}

$resolvedBinary = [IO.Path]::GetFullPath($BinaryPath)
if (!(Test-Path -LiteralPath $resolvedBinary -PathType Leaf)) {
    throw "Host artifact does not exist: $resolvedBinary"
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
if (![string]::IsNullOrEmpty($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

if ($Platform.StartsWith("android-", [StringComparison]::Ordinal)) {
    if ($AndroidApi -le 0) {
        throw "Android manifests require a positive -AndroidApi."
    }
    if ([string]::IsNullOrWhiteSpace($AndroidNdk)) {
        throw "Android manifests require -AndroidNdk."
    }
}
elseif ($AndroidApi -ne 0 -or ![string]::IsNullOrWhiteSpace($AndroidNdk)) {
    throw "Android metadata may only be supplied for an Android platform."
}

$header = Get-Content -LiteralPath $headerPath -Raw
$source = Get-Content -LiteralPath $sourcePath -Raw
$cmake = Get-Content -LiteralPath $cmakePath -Raw
$luauHeader = Get-Content -LiteralPath $luauHeaderPath -Raw
$managedProtection = Get-Content -LiteralPath $managedProtectionPath -Raw
$managedTypes = Get-Content -LiteralPath $managedTypesPath -Raw

$abiMagic = [Convert]::ToUInt32((Get-RequiredMatchValue $header "LUAU_HOST_ABI_MAGIC\s*=\s*0x([0-9a-fA-F]+)U" "ABI magic"), 16)
$abiMajor = [uint16](Get-RequiredMatchValue $header "LUAU_HOST_ABI_MAJOR\s*=\s*(\d+)" "ABI major")
$abiMinor = [uint16](Get-RequiredMatchValue $header "LUAU_HOST_ABI_MINOR\s*=\s*(\d+)" "ABI minor")
$pack = [uint32](Get-RequiredMatchValue $header "#pragma\s+pack\(push,\s*(\d+)\)" "ABI packing")

$recordVersions = [ordered]@{
    compile_options = [uint16](Get-RequiredMatchValue $header "LUAU_HOST_COMPILE_OPTIONS_VERSION\s*=\s*(\d+)" "compile-options version")
    callback_table = [uint16](Get-RequiredMatchValue $header "LUAU_HOST_CALLBACK_TABLE_VERSION\s*=\s*(\d+)" "callback-table version")
    state_options = [uint16](Get-RequiredMatchValue $header "LUAU_HOST_STATE_OPTIONS_VERSION\s*=\s*(\d+)" "state-options version")
}

$featureRecords = [Collections.Generic.List[object]]::new()
$featureBits = @{}
[uint32] $featureFlags = 0
$featureMatches = [regex]::Matches(
    $header,
    "LUAU_HOST_FEATURE_([A-Z0-9_]+)\s*=\s*1U\s*<<\s*(\d+)")
foreach ($match in $featureMatches) {
    $nativeName = $match.Groups[1].Value
    $normalizedName = $nativeName.Replace("_", "").ToLowerInvariant()
    $bit = [int]$match.Groups[2].Value
    [uint32] $flag = 1 -shl $bit
    $featureFlags = $featureFlags -bor $flag
    $featureBits[$normalizedName] = $bit
    $featureRecords.Add([ordered]@{
        name = $nativeName.ToLowerInvariant()
        bit = $bit
        flag = Format-Hex32 $flag
    })
}
Assert-Equal "Required feature flags derived from luau_host.h" $featureFlags $ApprovedFeatureFlags

$managedFeatureBody = Get-RequiredMatchValue `
    -Text $managedTypes `
    -Pattern "(?s)internal enum LuauHostFeature\s*:\s*uint\s*\{(.*?)\}" `
    -Description "managed host feature enum"
$managedFeatureMatches = [regex]::Matches(
    $managedFeatureBody,
    "([A-Za-z][A-Za-z0-9]*)\s*=\s*1U\s*<<\s*(\d+)")
Assert-Equal "Managed/native required feature count" $managedFeatureMatches.Count $featureMatches.Count
foreach ($match in $managedFeatureMatches) {
    $normalizedName = $match.Groups[1].Value.ToLowerInvariant()
    if (!$featureBits.ContainsKey($normalizedName)) {
        throw "Managed feature '$($match.Groups[1].Value)' has no native luau_host.h equivalent."
    }
    Assert-Equal `
        -Description "Feature bit for $($match.Groups[1].Value)" `
        -Actual ([int]$match.Groups[2].Value) `
        -Expected ([int]$featureBits[$normalizedName])
}

$nativeTypeBody = Get-RequiredMatchValue `
    -Text $luauHeader `
    -Pattern "(?s)\{\s*(LUA_TNIL\s*=.*?LUA_T_COUNT\s*=\s*LUA_TDEADKEY.*?)\};" `
    -Description "upstream Luau value-tag enum"
$managedTypeBody = Get-RequiredMatchValue `
    -Text $managedTypes `
    -Pattern "(?s)internal enum lua_Type\s*\{(.*?)\}" `
    -Description "managed Luau value-tag enum"
$nativeTypeValues = Get-EnumValues $nativeTypeBody
$managedTypeValues = Get-EnumValues $managedTypeBody
$requiredTypes = [ordered]@{
    nil = "LUA_TNIL"
    boolean = "LUA_TBOOLEAN"
    lightuserdata = "LUA_TLIGHTUSERDATA"
    number = "LUA_TNUMBER"
    integer = "LUA_TINTEGER"
    vector = "LUA_TVECTOR"
    string = "LUA_TSTRING"
    table = "LUA_TTABLE"
    function = "LUA_TFUNCTION"
    userdata = "LUA_TUSERDATA"
    thread = "LUA_TTHREAD"
    buffer = "LUA_TBUFFER"
    class = "LUA_TCLASS"
    object = "LUA_TOBJECT"
}
$typeTags = [ordered]@{}
foreach ($entry in $requiredTypes.GetEnumerator()) {
    if (!$nativeTypeValues.ContainsKey($entry.Value)) {
        throw "Upstream Luau value-tag enum is missing $($entry.Value)."
    }
    if (!$managedTypeValues.ContainsKey($entry.Value)) {
        throw "Managed Luau value-tag enum is missing $($entry.Value)."
    }
    Assert-Equal `
        -Description "Managed/native value tag $($entry.Value)" `
        -Actual ([int]$managedTypeValues[$entry.Value]) `
        -Expected ([int]$nativeTypeValues[$entry.Value])
    $typeTags[$entry.Key] = [int]$nativeTypeValues[$entry.Value]
}

function Get-NativeRecordSize {
    param([string] $NativeType)
    $escapedType = [regex]::Escape($NativeType)
    return [uint32](Get-RequiredMatchValue `
        -Text $source `
        -Pattern "static_assert\s*\(\s*sizeof\s*\(\s*$escapedType\s*\)\s*==\s*(\d+)" `
        -Description "$NativeType size assertion")
}

$abiInfoSize = Get-NativeRecordSize "luau_host_abi_info"
$compileOptionsSize = Get-NativeRecordSize "luau_host_compile_options"
$callbackTableSize = Get-NativeRecordSize "luau_host_callback_table"
$stateOptionsSize = Get-NativeRecordSize "luau_host_state_options"
$memoryInfoSize = Get-NativeRecordSize "luau_host_memory_info"
$bufferSize = Get-NativeRecordSize "luau_host_buffer"

$abiInfoOffsets = [ordered]@{}
$offsetMatches = [regex]::Matches(
    $source,
    "static_assert\s*\(\s*offsetof\s*\(\s*luau_host_abi_info\s*,\s*([A-Za-z0-9_]+)\s*\)\s*==\s*(\d+)")
foreach ($match in $offsetMatches) {
    $abiInfoOffsets[$match.Groups[1].Value] = [uint32]$match.Groups[2].Value
}
if ($abiInfoOffsets.Count -eq 0) {
    throw "No luau_host_abi_info offset assertions were found."
}

$upstreamRevision = Get-RequiredMatchValue `
    -Text $cmake `
    -Pattern 'set\(LUAU_HOST_UPSTREAM_REVISION\s+"([0-9a-fA-F]{40})"\)' `
    -Description "approved Luau revision"
$actualUpstreamRevision = (& git -C (Join-Path $repositoryRoot "luau") rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read the checked-out Luau revision."
}
Assert-Equal "Checked-out Luau revision" $actualUpstreamRevision $upstreamRevision

$headerSha256 = Get-LowerFileSha256 $headerPath
$sourceSha256 = Get-LowerFileSha256 $sourcePath
$exportsSha256 = Get-LowerFileSha256 $exportsPath
$buildInputDescriptor =
    "abi=$abiMajor.$abiMinor;upstream=$upstreamRevision;header=$headerSha256;source=$sourceSha256;exports=$exportsSha256"
$buildInputSha256 = Get-TextSha256 $buildInputDescriptor
$upstreamRevisionHash = Get-Fnv1a64 $upstreamRevision
$hostBuildFingerprint = Get-Fnv1a64 "luau-host-inputs;$buildInputSha256;$Configuration"

Assert-Equal "Approved build-input SHA256" $buildInputSha256 $ApprovedBuildInputSha256
Assert-Equal "Approved upstream revision hash" $upstreamRevisionHash $ApprovedUpstreamRevisionHash
Assert-Equal "Approved Release host fingerprint" $hostBuildFingerprint $ApprovedHostBuildFingerprint

$managedAbiMagic = [Convert]::ToUInt32((Get-RequiredMatchValue $managedProtection "ExpectedAbiMagic\s*=\s*0x([0-9a-fA-F]+)U" "managed ABI magic"), 16)
$managedAbiMajor = [uint16](Get-RequiredMatchValue $managedProtection "ExpectedAbiMajor\s*=\s*(\d+)" "managed ABI major")
$managedAbiMinor = [uint16](Get-RequiredMatchValue $managedProtection "MinimumAbiMinor\s*=\s*(\d+)" "managed minimum ABI minor")
$managedAbiRecordSize = [uint32](Get-RequiredMatchValue $managedProtection "ExpectedAbiRecordSize\s*=\s*(\d+)" "managed ABI record size")
$managedFeatureFlags = [Convert]::ToUInt32((Get-RequiredMatchValue $managedProtection "ExpectedFeatureFlags\s*=\s*0x([0-9a-fA-F]+)U" "managed feature flags"), 16)
$managedUpstreamRevisionHash = [Convert]::ToUInt64((Get-RequiredMatchValue $managedProtection "ExpectedUpstreamRevisionHash\s*=\s*0x([0-9a-fA-F]+)UL" "managed upstream revision hash"), 16)
$managedHostBuildFingerprint = [Convert]::ToUInt64((Get-RequiredMatchValue $managedProtection "ExpectedHostBuildFingerprint\s*=\s*0x([0-9a-fA-F]+)UL" "managed host build fingerprint"), 16)

Assert-Equal "Managed/native ABI magic" $managedAbiMagic $abiMagic
Assert-Equal "Managed/native ABI major" $managedAbiMajor $abiMajor
Assert-Equal "Managed/native ABI minor" $managedAbiMinor $abiMinor
Assert-Equal "Managed/native ABI record size" $managedAbiRecordSize $abiInfoSize
Assert-Equal "Managed/native feature flags" $managedFeatureFlags $featureFlags
Assert-Equal "Managed/native upstream revision hash" $managedUpstreamRevisionHash $upstreamRevisionHash
Assert-Equal "Managed/native host build fingerprint" $managedHostBuildFingerprint $hostBuildFingerprint

$approvedExports = @(
    Get-Content -LiteralPath $exportsPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and !$_.StartsWith("#", [StringComparison]::Ordinal) }
)
Assert-Equal "Approved export count" $approvedExports.Count $ApprovedExportCount
if (($approvedExports | Select-Object -Unique).Count -ne $approvedExports.Count) {
    throw "The luau_host export allowlist contains duplicate symbols."
}
foreach ($export in $approvedExports) {
    if (!$export.StartsWith("luau_host_", [StringComparison]::Ordinal)) {
        throw "The export allowlist contains a non-host symbol: $export"
    }
}

$platformMetadata = switch ($Platform) {
    "win-x64" { [ordered]@{ os = "windows"; architecture = "x64" } }
    "android-arm64" { [ordered]@{ os = "android"; architecture = "arm64" } }
    "android-x64" { [ordered]@{ os = "android"; architecture = "x64" } }
}

$abi = [ordered]@{
    magic = Format-Hex32 $abiMagic
    version = "$abiMajor.$abiMinor"
    major = $abiMajor
    minor = $abiMinor
    pointer_size = 8
    size_t_size = 8
    little_endian = $true
    features = [ordered]@{
        flags = Format-Hex32 $featureFlags
        required = @($featureRecords)
    }
    record_versions = $recordVersions
    layouts = [ordered]@{
        packing = $pack
        abi_info = [ordered]@{
            size = $abiInfoSize
            offsets = $abiInfoOffsets
        }
        compile_options = [ordered]@{ size = $compileOptionsSize }
        callback_table = [ordered]@{ size = $callbackTableSize }
        state_options = [ordered]@{ size = $stateOptionsSize }
        memory_info = [ordered]@{ size = $memoryInfoSize }
        buffer = [ordered]@{ size = $bufferSize }
    }
    type_tags = $typeTags
    upstream_revision_hash = Format-Hex64 $upstreamRevisionHash
    host_build_fingerprint = Format-Hex64 $hostBuildFingerprint
}

$binary = Get-Item -LiteralPath $resolvedBinary
$manifest = [ordered]@{
    schema_version = 2
    artifact = $binary.Name
    platform = $Platform
    platform_metadata = $platformMetadata
    sha256 = Get-LowerFileSha256 $resolvedBinary
    bytes = $binary.Length
    upstream_revision = $upstreamRevision
    upstream_revision_hash = Format-Hex64 $upstreamRevisionHash
    build_configuration = $Configuration
    build_input_sha256 = $buildInputSha256
    host_build_fingerprint = Format-Hex64 $hostBuildFingerprint
    build_inputs = [ordered]@{
        header_sha256 = $headerSha256
        source_sha256 = $sourceSha256
        exports_sha256 = $exportsSha256
        aggregate_sha256 = $buildInputSha256
    }
    abi = $abi
    approved_export_count = $approvedExports.Count
    approved_exports = $approvedExports
}

if ($Platform.StartsWith("android-", [StringComparison]::Ordinal)) {
    $manifest["android_api"] = $AndroidApi
    $manifest["android_ndk"] = $AndroidNdk
}

$json = $manifest | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText(
    $resolvedOutput,
    $json + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

Write-Host "Validated luau_host ABI manifest: $resolvedOutput"
Write-Host "  ABI: $abiMajor.$abiMinor; features: $(Format-Hex32 $featureFlags); exports: $($approvedExports.Count)"
Write-Host "  Upstream: $(Format-Hex64 $upstreamRevisionHash); host: $(Format-Hex64 $hostBuildFingerprint)"
Write-Host "  Build inputs: $buildInputSha256"
