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

$ApprovedBuildInputSha256 = "2ac37a6c388dd1ed39a450d2eebaad679d8bb38ca752623069eecc48d38d79dc"
$ApprovedUpstreamRevisionHash = [Convert]::ToUInt64("c45f010aabf167ac", 16)
$ApprovedHostBuildFingerprint = [Convert]::ToUInt64("e22f181ac247f52a", 16)
$ApprovedFeatureFlags = [uint32]0xfff
$ApprovedExportCount = 80

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

function Get-CMakeCacheValue {
    param(
        [string] $Cache,
        [string] $Name
    )

    $escapedName = [regex]::Escape($Name)
    return (Get-RequiredMatchValue `
        -Text $Cache `
        -Pattern "(?m)^${escapedName}:[^=]+=(.+)$" `
        -Description "CMake cache value $Name").Trim()
}

function Get-CMakeSetValue {
    param(
        [string] $Text,
        [string] $Name
    )

    $escapedName = [regex]::Escape($Name)
    $match = [regex]::Match(
        $Text,
        "(?m)^set\(${escapedName}\s+(?:`"(?<quoted>[^`"]*)`"|(?<plain>[^\)\r\n]*))\)")
    if (!$match.Success) {
        throw "Unable to derive generated CMake value $Name."
    }

    return $(if ($match.Groups["quoted"].Success) {
        $match.Groups["quoted"].Value
    }
    else {
        $match.Groups["plain"].Value.Trim()
    })
}

function Get-ToolFileRecord {
    param(
        [string] $Path,
        [string] $Identity,
        [string] $Version
    )

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Configured tool does not exist: $Path"
    }

    return [ordered]@{
        file = [IO.Path]::GetFileName($Path)
        identity = $Identity
        version = $Version
        sha256 = Get-LowerFileSha256 $Path
    }
}

function Get-ToolchainMetadata {
    param([string] $ArtifactPlatform)

    $preset = switch ($ArtifactPlatform) {
        "win-x64" { "windows-x64" }
        "android-arm64" { "android-arm64" }
        "android-x64" { "android-x64" }
        default { throw "Unsupported artifact platform: $ArtifactPlatform" }
    }
    $buildDirectory = Join-Path $hostRoot "out/build/$preset"
    $cachePath = Join-Path $buildDirectory "CMakeCache.txt"
    if (!(Test-Path -LiteralPath $cachePath -PathType Leaf)) {
        throw "Toolchain provenance requires the configured CMake cache: $cachePath"
    }
    $cache = Get-Content -LiteralPath $cachePath -Raw
    $cmakeCommand = Get-CMakeCacheValue $cache "CMAKE_COMMAND"
    $generator = Get-CMakeCacheValue $cache "CMAKE_GENERATOR"
    $cmakeVersion = "{0}.{1}.{2}" -f `
        (Get-CMakeCacheValue $cache "CMAKE_CACHE_MAJOR_VERSION"), `
        (Get-CMakeCacheValue $cache "CMAKE_CACHE_MINOR_VERSION"), `
        (Get-CMakeCacheValue $cache "CMAKE_CACHE_PATCH_VERSION")
    $cmakeVersionOutput = @(& $cmakeCommand --version 2>&1)
    $cmakeVersionExitCode = $LASTEXITCODE
    $reportedCmakeVersion = ($cmakeVersionOutput | Select-Object -First 1 | Out-String).Trim()
    if ($cmakeVersionExitCode -ne 0 -or !$reportedCmakeVersion.StartsWith("cmake version ", [StringComparison]::Ordinal)) {
        throw "Unable to query configured CMake version from $cmakeCommand."
    }

    $compilerDescriptors = @(
        Get-ChildItem `
            -Path (Join-Path $buildDirectory "CMakeFiles/$cmakeVersion*/CMakeCXXCompiler.cmake") `
            -File `
            -ErrorAction Stop)
    if ($compilerDescriptors.Count -ne 1) {
        throw "Expected one CMake C++ compiler descriptor for $preset/$cmakeVersion; found $($compilerDescriptors.Count). Use a clean configured build tree."
    }
    $compilerDescriptor = Get-Content -LiteralPath $compilerDescriptors[0].FullName -Raw
    $compilerPath = Get-CMakeSetValue $compilerDescriptor "CMAKE_CXX_COMPILER"
    $compilerId = Get-CMakeSetValue $compilerDescriptor "CMAKE_CXX_COMPILER_ID"
    $compilerVersion = Get-CMakeSetValue $compilerDescriptor "CMAKE_CXX_COMPILER_VERSION"
    # The Android compiler descriptor may preserve an 8.3/no-extension linker
    # spelling. The cache carries the directly invokable linker file.
    $linkerPath = Get-CMakeCacheValue $cache "CMAKE_LINKER"
    $linkerId = Get-CMakeSetValue $compilerDescriptor "CMAKE_CXX_COMPILER_LINKER_ID"
    $linkerVersion = Get-CMakeSetValue $compilerDescriptor "CMAKE_CXX_COMPILER_LINKER_VERSION"

    $makeProgramRecord = $null
    $makeProgramMatch = [regex]::Match($cache, "(?m)^CMAKE_MAKE_PROGRAM:[^=]+=(.+)$")
    if ($makeProgramMatch.Success) {
        $makeProgram = $makeProgramMatch.Groups[1].Value.Trim()
        if (!(Test-Path -LiteralPath $makeProgram -PathType Leaf)) {
            throw "Configured build tool does not exist: $makeProgram"
        }
        $makeProgramVersionOutput = @(& $makeProgram --version 2>&1)
        $makeProgramVersionExitCode = $LASTEXITCODE
        $makeProgramVersion = ($makeProgramVersionOutput | Select-Object -First 1 | Out-String).Trim()
        if ($makeProgramVersionExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($makeProgramVersion)) {
            throw "Unable to query configured build-tool version from $makeProgram."
        }
        $makeProgramRecord = [ordered]@{
            file = [IO.Path]::GetFileName($makeProgram)
            version = $makeProgramVersion
            sha256 = Get-LowerFileSha256 $makeProgram
        }
    }

    $metadata = [ordered]@{
        cmake = [ordered]@{
            version = $reportedCmakeVersion.Substring("cmake version ".Length)
            generator = $generator
            executable_sha256 = Get-LowerFileSha256 $cmakeCommand
        }
        compiler = Get-ToolFileRecord $compilerPath $compilerId $compilerVersion
        linker = Get-ToolFileRecord $linkerPath $linkerId $linkerVersion
        build_tool = $makeProgramRecord
        build_host = [ordered]@{
            os = [Environment]::OSVersion.VersionString
            architecture = [string]$env:PROCESSOR_ARCHITECTURE
            ci_image_os = [string]$env:ImageOS
            ci_image_version = [string]$env:ImageVersion
        }
    }
    if ($ArtifactPlatform -eq "win-x64") {
        $sdkVersion = Get-CMakeCacheValue $cache "CMAKE_SYSTEM_VERSION"
        $sdkRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits/10"
        $windowsHeader = Join-Path $sdkRoot "Include/$sdkVersion/um/Windows.h"
        $runtimeHeader = Join-Path $sdkRoot "Include/$sdkVersion/ucrt/corecrt.h"
        $kernelLibrary = Join-Path $sdkRoot "Lib/$sdkVersion/um/x64/kernel32.lib"
        foreach ($requiredSdkFile in @($windowsHeader, $runtimeHeader, $kernelLibrary)) {
            if (!(Test-Path -LiteralPath $requiredSdkFile -PathType Leaf)) {
                throw "Configured Windows SDK $sdkVersion is incomplete: $requiredSdkFile"
            }
        }
        $metadata["windows_sdk"] = [ordered]@{
            version = $sdkVersion
            windows_header_sha256 = Get-LowerFileSha256 $windowsHeader
            runtime_header_sha256 = Get-LowerFileSha256 $runtimeHeader
            kernel_library_sha256 = Get-LowerFileSha256 $kernelLibrary
        }
    }

    return $metadata
}

function Get-BinaryArchitecture {
    param(
        [byte[]] $Bytes,
        [string] $ExpectedPlatform
    )

    if ($ExpectedPlatform -eq "win-x64") {
        if ($Bytes.Length -lt 64 -or $Bytes[0] -ne 0x4d -or $Bytes[1] -ne 0x5a) {
            throw "The Windows host artifact is not a PE binary."
        }

        $peOffset = [BitConverter]::ToInt32($Bytes, 0x3c)
        if ($peOffset -lt 0 -or $peOffset + 26 -gt $Bytes.Length -or
            $Bytes[$peOffset] -ne 0x50 -or $Bytes[$peOffset + 1] -ne 0x45 -or
            $Bytes[$peOffset + 2] -ne 0 -or $Bytes[$peOffset + 3] -ne 0) {
            throw "The Windows host artifact has an invalid PE header."
        }

        $machine = [BitConverter]::ToUInt16($Bytes, $peOffset + 4)
        $optionalMagic = [BitConverter]::ToUInt16($Bytes, $peOffset + 24)
        Assert-Equal "Windows host machine" $machine ([uint16]0x8664)
        Assert-Equal "Windows host optional-header format" $optionalMagic ([uint16]0x020b)
        return [ordered]@{ format = "PE32+"; machine = "x86_64"; pointer_bits = 64 }
    }

    if ($Bytes.Length -lt 20 -or
        $Bytes[0] -ne 0x7f -or $Bytes[1] -ne 0x45 -or
        $Bytes[2] -ne 0x4c -or $Bytes[3] -ne 0x46) {
        throw "The Android host artifact is not an ELF binary."
    }

    Assert-Equal "Android ELF class" $Bytes[4] ([byte]2)
    Assert-Equal "Android ELF byte order" $Bytes[5] ([byte]1)
    $machine = [BitConverter]::ToUInt16($Bytes, 18)
    if ($ExpectedPlatform -eq "android-arm64") {
        Assert-Equal "Android ARM64 ELF machine" $machine ([uint16]183)
        return [ordered]@{ format = "ELF64"; machine = "aarch64"; pointer_bits = 64 }
    }
    if ($ExpectedPlatform -eq "android-x64") {
        Assert-Equal "Android x64 ELF machine" $machine ([uint16]62)
        return [ordered]@{ format = "ELF64"; machine = "x86_64"; pointer_bits = 64 }
    }

    throw "Unsupported artifact platform: $ExpectedPlatform"
}

function Get-BinaryIdentity {
    param([byte[]] $Bytes)

    $marker = "LUAUHABI-PROBE1"
    $binaryText = [Text.Encoding]::ASCII.GetString($Bytes)
    $offset = $binaryText.IndexOf($marker, [StringComparison]::Ordinal)
    if ($offset -lt 0) {
        throw "The host artifact does not contain the required binary-identity record."
    }
    if ($binaryText.IndexOf($marker, $offset + 1, [StringComparison]::Ordinal) -ge 0) {
        throw "The host artifact contains more than one binary-identity record."
    }

    $recordSize = [BitConverter]::ToUInt32($Bytes, $offset + 16)
    if ($recordSize -ne 149 -or $offset + $recordSize -gt $Bytes.Length) {
        throw "The host artifact contains an invalid binary-identity record size."
    }

    $inputLength = [Array]::IndexOf($Bytes, [byte]0, $offset + 52, 65) - ($offset + 52)
    $configurationLength = [Array]::IndexOf($Bytes, [byte]0, $offset + 117, 32) - ($offset + 117)
    if ($inputLength -lt 0 -or $configurationLength -lt 0) {
        throw "The host artifact contains unterminated binary-identity text."
    }

    return [ordered]@{
        record_size = $recordSize
        abi_magic = [BitConverter]::ToUInt32($Bytes, $offset + 20)
        abi_major = [BitConverter]::ToUInt16($Bytes, $offset + 24)
        abi_minor = [BitConverter]::ToUInt16($Bytes, $offset + 26)
        feature_flags = [BitConverter]::ToUInt32($Bytes, $offset + 28)
        pointer_size = $Bytes[$offset + 32]
        size_t_size = $Bytes[$offset + 33]
        little_endian = $Bytes[$offset + 34]
        reserved = $Bytes[$offset + 35]
        upstream_revision_hash = [BitConverter]::ToUInt64($Bytes, $offset + 36)
        host_build_fingerprint = [BitConverter]::ToUInt64($Bytes, $offset + 44)
        build_input_sha256 = [Text.Encoding]::ASCII.GetString($Bytes, $offset + 52, $inputLength)
        build_configuration = [Text.Encoding]::ASCII.GetString($Bytes, $offset + 117, $configurationLength)
    }
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

function Get-ManagedEnumValues {
    param([string] $Body)

    $values = @{}
    $current = -1
    $withoutComments = [regex]::Replace($Body, "//[^`r`n]*", "")

    foreach ($entry in ($withoutComments -split ",")) {
        $match = [regex]::Match(
            $entry,
            "(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=\s*(?<assigned>[A-Za-z_][A-Za-z0-9_]*|-?\d+))?")
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
$upstreamRoot = Join-Path $repositoryRoot "native/luau"
$packageRoot = Join-Path $repositoryRoot "Luau.Unity"
$headerPath = Join-Path $hostRoot "include/luau_host.h"
$sourcePath = Join-Path $hostRoot "src/luau_host.cpp"
$referenceTokensPath = Join-Path $hostRoot "src/reference_tokens.h"
$allocatorPath = Join-Path $hostRoot "src/tracked_allocation.h"
$exportsPath = Join-Path $hostRoot "exports/luau_host.exports"
$cmakePath = Join-Path $hostRoot "CMakeLists.txt"
$exportAuditPath = Join-Path $hostRoot "cmake/AuditExports.cmake"
$luauHeaderPath = Join-Path $upstreamRoot "VM/include/lua.h"
$managedProtectionPath = Join-Path $repositoryRoot "src/Luau/Internal/LuauNativeProtection.cs"
$managedTypesPath = Join-Path $packageRoot "Runtime/Interop/NativeTypes.cs"

foreach ($requiredPath in @(
    $headerPath,
    $sourcePath,
    $referenceTokensPath,
    $allocatorPath,
    $exportsPath,
    $cmakePath,
    $exportAuditPath,
    $luauHeaderPath,
    $managedProtectionPath,
    $managedTypesPath)) {
    if (!(Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required manifest input does not exist: $requiredPath"
    }
}

function Invoke-BinaryExportAudit {
    param(
        [string] $ArtifactPath,
        [string] $ArtifactPlatform
    )

    $preset = switch ($ArtifactPlatform) {
        "win-x64" { "windows-x64" }
        "android-arm64" { "android-arm64" }
        "android-x64" { "android-x64" }
        default { throw "Unsupported artifact platform: $ArtifactPlatform" }
    }
    $cachePath = Join-Path $hostRoot "out/build/$preset/CMakeCache.txt"
    if (!(Test-Path -LiteralPath $cachePath -PathType Leaf)) {
        throw "The binary export audit requires its configured build cache: $cachePath"
    }

    $cache = Get-Content -LiteralPath $cachePath -Raw
    $cmakeCommand = Get-CMakeCacheValue $cache "CMAKE_COMMAND"
    if ($ArtifactPlatform -eq "win-x64") {
        $linker = Get-CMakeCacheValue $cache "CMAKE_LINKER"
        $exportTool = Join-Path (Split-Path -Parent $linker) "dumpbin.exe"
        $exportToolKind = "MSVC"
    }
    else {
        $exportTool = Get-CMakeCacheValue $cache "CMAKE_NM"
        $exportToolKind = "NM"
    }

    foreach ($tool in @($cmakeCommand, $exportTool)) {
        if (!(Test-Path -LiteralPath $tool -PathType Leaf)) {
            throw "Required binary-audit tool does not exist: $tool"
        }
    }

    & $cmakeCommand `
        "-DBINARY=$ArtifactPath" `
        "-DALLOWLIST=$exportsPath" `
        "-DEXPORT_TOOL=$exportTool" `
        "-DEXPORT_TOOL_KIND=$exportToolKind" `
        -P $exportAuditPath
    if ($LASTEXITCODE -ne 0) {
        throw "The binary export audit failed for $ArtifactPath."
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
    -Pattern "(?s)internal enum LuauHostType\s*\{(.*?)\}" `
    -Description "managed Luau value-tag enum"
$nativeTypeValues = Get-EnumValues $nativeTypeBody
$managedTypeValues = Get-ManagedEnumValues $managedTypeBody
$requiredTypes = [ordered]@{
    nil = [ordered]@{ native = "LUA_TNIL"; managed = "Nil" }
    boolean = [ordered]@{ native = "LUA_TBOOLEAN"; managed = "Boolean" }
    lightuserdata = [ordered]@{ native = "LUA_TLIGHTUSERDATA"; managed = "LightUserdata" }
    number = [ordered]@{ native = "LUA_TNUMBER"; managed = "Number" }
    integer = [ordered]@{ native = "LUA_TINTEGER"; managed = "Integer" }
    vector = [ordered]@{ native = "LUA_TVECTOR"; managed = "Vector" }
    string = [ordered]@{ native = "LUA_TSTRING"; managed = "String" }
    table = [ordered]@{ native = "LUA_TTABLE"; managed = "Table" }
    function = [ordered]@{ native = "LUA_TFUNCTION"; managed = "Function" }
    userdata = [ordered]@{ native = "LUA_TUSERDATA"; managed = "Userdata" }
    thread = [ordered]@{ native = "LUA_TTHREAD"; managed = "Thread" }
    buffer = [ordered]@{ native = "LUA_TBUFFER"; managed = "Buffer" }
    class = [ordered]@{ native = "LUA_TCLASS"; managed = "Class" }
    object = [ordered]@{ native = "LUA_TOBJECT"; managed = "Object" }
}
$typeTags = [ordered]@{}
foreach ($entry in $requiredTypes.GetEnumerator()) {
    if (!$nativeTypeValues.ContainsKey($entry.Value.native)) {
        throw "Upstream Luau value-tag enum is missing $($entry.Value.native)."
    }
    if (!$managedTypeValues.ContainsKey($entry.Value.managed)) {
        throw "Managed Luau value-tag enum is missing $($entry.Value.managed)."
    }
    Assert-Equal `
        -Description "Managed/native value tag $($entry.Value.native)" `
        -Actual ([int]$managedTypeValues[$entry.Value.managed]) `
        -Expected ([int]$nativeTypeValues[$entry.Value.native])
    $typeTags[$entry.Key] = [int]$nativeTypeValues[$entry.Value.native]
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
$actualUpstreamRevision = (& git -C $upstreamRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read the checked-out Luau revision."
}
Assert-Equal "Checked-out Luau revision" $actualUpstreamRevision $upstreamRevision

$headerSha256 = Get-LowerFileSha256 $headerPath
$sourceSha256 = Get-LowerFileSha256 $sourcePath
$referenceTokensSha256 = Get-LowerFileSha256 $referenceTokensPath
$allocatorSha256 = Get-LowerFileSha256 $allocatorPath
$exportsSha256 = Get-LowerFileSha256 $exportsPath
$buildInputDescriptor =
    "abi=$abiMajor.$abiMinor;upstream=$upstreamRevision;header=$headerSha256;source=$sourceSha256;references=$referenceTokensSha256;allocator=$allocatorSha256;exports=$exportsSha256"
$buildInputSha256 = Get-TextSha256 $buildInputDescriptor
$upstreamRevisionHash = Get-Fnv1a64 $upstreamRevision
$hostBuildFingerprint = Get-Fnv1a64 "luau-host-inputs;$buildInputSha256;$Configuration"

$binaryBytes = [IO.File]::ReadAllBytes($resolvedBinary)
$binaryArchitecture = Get-BinaryArchitecture $binaryBytes $Platform
$binaryIdentity = Get-BinaryIdentity $binaryBytes
Assert-Equal "Binary/source ABI magic" $binaryIdentity.abi_magic $abiMagic
Assert-Equal "Binary/source ABI major" $binaryIdentity.abi_major $abiMajor
Assert-Equal "Binary/source ABI minor" $binaryIdentity.abi_minor $abiMinor
Assert-Equal "Binary/source feature flags" $binaryIdentity.feature_flags $featureFlags
Assert-Equal "Binary pointer size" $binaryIdentity.pointer_size ([byte]8)
Assert-Equal "Binary size_t size" $binaryIdentity.size_t_size ([byte]8)
Assert-Equal "Binary byte order" $binaryIdentity.little_endian ([byte]1)
Assert-Equal "Binary identity reserved byte" $binaryIdentity.reserved ([byte]0)
Assert-Equal "Binary/source upstream revision hash" $binaryIdentity.upstream_revision_hash $upstreamRevisionHash
Assert-Equal "Binary/source host fingerprint" $binaryIdentity.host_build_fingerprint $hostBuildFingerprint
Assert-Equal "Binary/source build-input SHA256" $binaryIdentity.build_input_sha256 $buildInputSha256
Assert-Equal "Binary/source build configuration" $binaryIdentity.build_configuration $Configuration

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
Invoke-BinaryExportAudit $resolvedBinary $Platform

$sourceCommit = (& git -C $repositoryRoot rev-parse HEAD 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "Unable to resolve the source commit for native artifact provenance: $sourceCommit"
}
$sourceCommit = $sourceCommit.ToLowerInvariant()
$sourceStatus = (& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect the source tree for native artifact provenance: $sourceStatus"
}
$sourceTreeClean = [string]::IsNullOrWhiteSpace($sourceStatus)
$toolchain = Get-ToolchainMetadata $Platform
if ($Platform.StartsWith("android-", [StringComparison]::Ordinal)) {
    $toolchain["android"] = [ordered]@{
        api = $AndroidApi
        ndk_revision = $AndroidNdk
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
    schema_version = 3
    artifact = $binary.Name
    platform = $Platform
    source_commit = $sourceCommit
    source_tree_clean = $sourceTreeClean
    platform_metadata = $platformMetadata
    toolchain = $toolchain
    binary_architecture = $binaryArchitecture
    binary_identity = [ordered]@{
        record_size = $binaryIdentity.record_size
        abi_magic = Format-Hex32 $binaryIdentity.abi_magic
        abi_version = "$($binaryIdentity.abi_major).$($binaryIdentity.abi_minor)"
        feature_flags = Format-Hex32 $binaryIdentity.feature_flags
        pointer_size = $binaryIdentity.pointer_size
        size_t_size = $binaryIdentity.size_t_size
        little_endian = ($binaryIdentity.little_endian -eq 1)
        upstream_revision_hash = Format-Hex64 $binaryIdentity.upstream_revision_hash
        host_build_fingerprint = Format-Hex64 $binaryIdentity.host_build_fingerprint
        build_input_sha256 = $binaryIdentity.build_input_sha256
        build_configuration = $binaryIdentity.build_configuration
    }
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
        reference_tokens_sha256 = $referenceTokensSha256
        allocator_sha256 = $allocatorSha256
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
Write-Host "  Toolchain: $($toolchain.compiler.identity) $($toolchain.compiler.version); $($toolchain.linker.identity) $($toolchain.linker.version); $($toolchain.cmake.version)"
Write-Host "  Source: $sourceCommit; clean tree: $sourceTreeClean"
