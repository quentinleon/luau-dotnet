Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$packageRoot = Join-Path $repositoryRoot "Luau.Unity"
$integrationProjectRoot = Join-Path $repositoryRoot "tests/Luau.Unity.Integration"
$upstreamRoot = Join-Path $repositoryRoot "native/luau"
$hostRoot = Join-Path $repositoryRoot "native/luau-host"

function Get-RequiredFile {
    param(
        [string] $Root,
        [string] $RelativePath,
        [string] $Description
    )

    $path = Join-Path $Root $RelativePath
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "$Description is missing: $path"
    }

    return $path
}

function Get-RepositoryFile {
    param([string] $RelativePath)

    return Get-RequiredFile $repositoryRoot $RelativePath "Required repository file"
}

function Get-PackageFile {
    param([string] $RelativePath)

    return Get-RequiredFile $packageRoot $RelativePath "Required package file"
}

function Get-IntegrationFile {
    param([string] $RelativePath)

    return Get-RequiredFile $integrationProjectRoot $RelativePath "Required integration-project file"
}

function Read-RepositoryText {
    param([string] $RelativePath)

    return [IO.File]::ReadAllText((Get-RepositoryFile $RelativePath))
}

function Read-RepositoryJson {
    param([string] $RelativePath)

    return Read-RepositoryText $RelativePath | ConvertFrom-Json
}

function Read-PackageText {
    param([string] $RelativePath)

    return [IO.File]::ReadAllText((Get-PackageFile $RelativePath))
}

function Read-PackageJson {
    param([string] $RelativePath)

    return Read-PackageText $RelativePath | ConvertFrom-Json
}

function Read-IntegrationText {
    param([string] $RelativePath)

    return [IO.File]::ReadAllText((Get-IntegrationFile $RelativePath))
}

function Read-IntegrationJson {
    param([string] $RelativePath)

    return Read-IntegrationText $RelativePath | ConvertFrom-Json
}

function Assert-Equal {
    param(
        [string] $Description,
        $Actual,
        $Expected
    )

    if ($Actual -ne $Expected) {
        throw "$Description mismatch. Expected '$Expected', found '$Actual'."
    }
}

function Assert-SequenceEqual {
    param(
        [string] $Description,
        [object[]] $Actual,
        [object[]] $Expected
    )

    $actualValues = @($Actual)
    $expectedValues = @($Expected)
    if ($actualValues.Count -ne $expectedValues.Count) {
        throw "$Description mismatch. Expected $($expectedValues.Count) value(s), found $($actualValues.Count)."
    }

    for ($index = 0; $index -lt $expectedValues.Count; $index++) {
        if ($actualValues[$index] -ne $expectedValues[$index]) {
            throw "$Description mismatch at index $index. Expected '$($expectedValues[$index])', found '$($actualValues[$index])'."
        }
    }
}

function Assert-ContainsLiteral {
    param(
        [string] $Description,
        [string] $Text,
        [string] $Literal
    )

    if ($Text.IndexOf($Literal, [StringComparison]::Ordinal) -lt 0) {
        throw "$Description is missing required text: $Literal"
    }
}

function Assert-NotContainsLiteral {
    param(
        [string] $Description,
        [string] $Text,
        [string] $Literal
    )

    if ($Text.IndexOf($Literal, [StringComparison]::Ordinal) -ge 0) {
        throw "$Description contains forbidden text: $Literal"
    }
}

function Assert-LiteralCount {
    param(
        [string] $Description,
        [string] $Text,
        [string] $Literal,
        [int] $Expected
    )

    $count = 0
    $offset = 0
    while (($offset = $Text.IndexOf($Literal, $offset, [StringComparison]::Ordinal)) -ge 0) {
        $count++
        $offset += $Literal.Length
    }

    if ($count -ne $Expected) {
        throw "$Description mismatch. Expected '$Literal' $Expected time(s), found $count."
    }
}

function Assert-LiteralOrder {
    param(
        [string] $Description,
        [string] $Text,
        [string[]] $Literals
    )

    $offset = 0
    foreach ($literal in $Literals) {
        $index = $Text.IndexOf($literal, $offset, [StringComparison]::Ordinal)
        if ($index -lt 0) {
            throw "$Description is missing or misorders required text: $literal"
        }
        $offset = $index + $literal.Length
    }
}

function Assert-PathAbsent {
    param([string] $RelativePath)

    if (Test-Path -LiteralPath (Join-Path $repositoryRoot $RelativePath)) {
        throw "Retired package surface must remain absent: $RelativePath"
    }
}

function Assert-PackagePathAbsent {
    param([string] $RelativePath)

    if (Test-Path -LiteralPath (Join-Path $packageRoot $RelativePath)) {
        throw "Retired or non-package surface must remain absent from the package: $RelativePath"
    }
}

function Assert-FileHash {
    param(
        [string] $Description,
        [string] $Path,
        [string] $ExpectedSha256
    )

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (!$actual.Equals($ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description SHA256 mismatch. Expected '$ExpectedSha256', found '$actual'."
    }
}

function Assert-FileLength {
    param(
        [string] $Description,
        [string] $Path,
        [long] $ExpectedLength
    )

    $actual = (Get-Item -LiteralPath $Path).Length
    if ($actual -ne $ExpectedLength) {
        throw "$Description length mismatch. Expected '$ExpectedLength', found '$actual'."
    }
}

function Get-MetaGuid {
    param(
        [string] $Description,
        [string] $Path
    )

    $text = [IO.File]::ReadAllText($Path)
    $matches = [Regex]::Matches($text, '(?m)^guid:\s*([0-9a-fA-F]{32})\s*$')
    if ($matches.Count -ne 1) {
        throw "$Description must contain exactly one 32-digit Unity GUID; found $($matches.Count)."
    }

    return $matches[0].Groups[1].Value.ToLowerInvariant()
}

function Assert-MetaGuid {
    param(
        [string] $Description,
        [string] $Path,
        [string] $ExpectedGuid
    )

    Assert-Equal $Description (Get-MetaGuid $Description $Path) $ExpectedGuid
}

function Get-PackageRelativePath {
    param([string] $Path)

    $relative = [IO.Path]::GetFullPath($Path).Substring($packageRoot.Length)
    return $relative.TrimStart([char[]]@('\', '/')).Replace('\', '/')
}

function Get-ImporterPlatformSection {
    param(
        [string] $Description,
        [string] $ImporterText,
        [string] $Platform
    )

    $pattern = '(?ms)^    ' + [Regex]::Escape($Platform) +
        ':\r?\n(?<section>.*?)(?=^    [A-Za-z0-9]+:\r?$|^  userData:)'
    $match = [Regex]::Match($ImporterText, $pattern)
    if (!$match.Success) {
        throw "$Description is missing the $Platform platform importer block."
    }

    return $match.Groups['section'].Value
}

function Assert-ImporterPlatform {
    param(
        [string] $Description,
        [string] $ImporterText,
        [string] $Platform,
        [bool] $Enabled,
        [string[]] $RequiredSettings = @()
    )

    $section = Get-ImporterPlatformSection $Description $ImporterText $Platform
    $expectedEnabled = if ($Enabled) { '1' } else { '0' }
    Assert-ContainsLiteral "$Description $Platform state" $section "enabled: $expectedEnabled"
    foreach ($setting in $RequiredSettings) {
        Assert-ContainsLiteral "$Description $Platform settings" $section $setting
    }
}

if (!(Test-Path -LiteralPath $packageRoot -PathType Container)) {
    throw "The standalone UPM package root is missing: $packageRoot"
}
if (!(Test-Path -LiteralPath $integrationProjectRoot -PathType Container)) {
    throw "The Unity integration project is missing: $integrationProjectRoot"
}

$packageTopLevel = @(
    Get-ChildItem -LiteralPath $packageRoot -Force |
        ForEach-Object Name |
        Sort-Object
)
Assert-SequenceEqual `
    "Stage 5 package top-level content" `
    $packageTopLevel `
    @("Editor", "Editor.meta", "package.json", "Runtime", "Runtime.meta", "Tests", "Tests.meta")
Assert-PackagePathAbsent "package.json.meta"
Write-Host "PASS: Luau.Unity is a standalone package with the exact Stage 5 top-level allowlist."

$package = Read-PackageJson "package.json"
Assert-Equal "Unity package name" $package.name "com.nuskey.luau.unity"
Assert-Equal "Unity package version" $package.version "0.1.6"
Assert-Equal "Unity package display name" $package.displayName "Luau.Unity"
Assert-Equal `
    "Unity package description" `
    $package.description `
    "Unity-first Luau runtime with managed APIs and native plugins"
Assert-Equal "Unity package author" $package.author.name "nuskey"
$dependencyProperty = $package.PSObject.Properties["dependencies"]
if ($null -ne $dependencyProperty -and @($dependencyProperty.Value.PSObject.Properties).Count -ne 0) {
    throw "The Unity package must not rely on development-project dependencies."
}
Write-Host "PASS: package identity and preview version are unchanged and declare no dependencies."

$forbiddenDirectoryNames = @(
    "Assets",
    "Packages",
    "ProjectSettings",
    "Library",
    "Temp",
    "Logs",
    "Builds",
    "UserSettings",
    "bin",
    "obj",
    "Sandbox",
    "URP",
    "Verification",
    "Documentation~",
    "Samples~"
)
foreach ($directory in Get-ChildItem -LiteralPath $packageRoot -Recurse -Force -Directory) {
    if ($forbiddenDirectoryNames -contains $directory.Name) {
        throw "Development, generated, project-only, or post-Stage-5 directory ships in the package: $(Get-PackageRelativePath $directory.FullName)"
    }
}

foreach ($item in Get-ChildItem -LiteralPath $packageRoot -Recurse -Force) {
    if ($item.Name.EndsWith(".meta", [StringComparison]::OrdinalIgnoreCase)) {
        continue
    }

    $relativePath = Get-PackageRelativePath $item.FullName
    if (!$item.PSIsContainer -and $relativePath -eq "package.json") {
        continue
    }

    if (!(Test-Path -LiteralPath ($item.FullName + ".meta") -PathType Leaf)) {
        throw "Package asset is missing its Unity metadata: $relativePath"
    }
}

$guidOwners = @{}
foreach ($metaFile in Get-ChildItem -LiteralPath $packageRoot -Recurse -Force -File -Filter "*.meta") {
    $assetPath = $metaFile.FullName.Substring(0, $metaFile.FullName.Length - ".meta".Length)
    $relativeMetaPath = Get-PackageRelativePath $metaFile.FullName
    if (!(Test-Path -LiteralPath $assetPath)) {
        throw "Orphan Unity metadata has no corresponding package asset: $relativeMetaPath"
    }

    $guid = Get-MetaGuid "Package metadata $relativeMetaPath" $metaFile.FullName
    if ($guidOwners.ContainsKey($guid)) {
        throw "Duplicate Unity GUID '$guid' appears in '$($guidOwners[$guid])' and '$relativeMetaPath'."
    }
    $guidOwners[$guid] = $relativeMetaPath
}
Write-Host "PASS: every package asset has metadata, every metadata file has an asset, and all package GUIDs are unique."

$allowedBinaryPaths = @(
    "Runtime/Luau.dll",
    "Runtime/Luau.SourceGenerator.dll",
    "Runtime/Plugins/android-arm64/libluau_host.so",
    "Runtime/Plugins/android-x64/libluau_host.so",
    "Runtime/Plugins/win-x64/luau_host.dll"
)
$binaryExtensions = @(".a", ".aar", ".bundle", ".dll", ".dylib", ".exe", ".jar", ".lib", ".mdb", ".pdb", ".so", ".xml")
$actualBinaryPaths = @(
    Get-ChildItem -LiteralPath $packageRoot -Recurse -Force -File |
        Where-Object { $binaryExtensions -contains $_.Extension.ToLowerInvariant() } |
        ForEach-Object { Get-PackageRelativePath $_.FullName } |
        Sort-Object
)
Assert-SequenceEqual "Stage 5 managed/native binary inventory" $actualBinaryPaths $allowedBinaryPaths
Write-Host "PASS: the package contains only the five approved Stage 5 managed/native artifacts."

$artifactBaselines = @(
    @{ Path = "Runtime/Luau.dll"; Length = 220160L; Sha256 = "1561DA495CCD7F6EB1C731E4AF4ADC6D3FBB03F55BA9A21A7210432C5E4D3620" },
    @{ Path = "Runtime/Luau.SourceGenerator.dll"; Length = 62976L; Sha256 = "A85C30B3DC6C6B174C6412FD8E92E072ADC4639EA5CFDE93349645BF77E0F24E" },
    @{ Path = "Runtime/Plugins/win-x64/luau_host.dll"; Length = 716800L; Sha256 = "0BEB9381E900228C46F2AF97E4394C3F46A22740624E0C02675BEBE0A134B637" },
    @{ Path = "Runtime/Plugins/android-arm64/libluau_host.so"; Length = 9738896L; Sha256 = "DCA28A83E732D494E5F6C5C5BA590A0BF428A261B0805CE29D79B3F1986A3F4A" },
    @{ Path = "Runtime/Plugins/android-x64/libluau_host.so"; Length = 9491432L; Sha256 = "03D7E02A2C58C5E92760697122A8880D5727894DE45095505D9CAF0515C00729" }
)
foreach ($artifact in $artifactBaselines) {
    $artifactPath = Get-PackageFile $artifact.Path
    Assert-FileLength "Checked-in artifact $($artifact.Path)" $artifactPath $artifact.Length
    Assert-FileHash "Checked-in artifact $($artifact.Path)" $artifactPath $artifact.Sha256
}

$importerMetaBaselines = @(
    @{ Path = "Runtime/Luau.dll.meta"; Sha256 = "15053472E460343CF4695A7233307CFC03962D1C3CD1D3E73A29179A33FB11D8" },
    @{ Path = "Runtime/Luau.SourceGenerator.dll.meta"; Sha256 = "AAB0C496B4CE44B75F1D5259F73995B3A686FEDA1626EF8D658FA489EA1AE199" },
    @{ Path = "Runtime/Plugins/win-x64/luau_host.dll.meta"; Sha256 = "699FB21B9A924DB6EFE360450AD8CBD620CB980B8001F5F4D026FAA383F3E24D" },
    @{ Path = "Runtime/Plugins/android-arm64/libluau_host.so.meta"; Sha256 = "C13B52911F78424D6F9B2C6FD86BF29F8B7680AFDE5F04B51664A06F117B1D01" },
    @{ Path = "Runtime/Plugins/android-x64/libluau_host.so.meta"; Sha256 = "618EF509865F2810495805919BF0611968B0AFF751AEE052AC2C05E3F8681C92" }
)
foreach ($meta in $importerMetaBaselines) {
    Assert-FileHash "Checked-in importer metadata $($meta.Path)" (Get-PackageFile $meta.Path) $meta.Sha256
}
Write-Host "PASS: managed/analyzer/native binaries and their importer metadata match the immutable pre-move hashes."

$managedImporter = Read-PackageText "Runtime/Luau.dll.meta"
Assert-ContainsLiteral "Managed runtime importer" $managedImporter "isExplicitlyReferenced: 0"
Assert-ContainsLiteral "Managed runtime importer" $managedImporter "validateReferences: 1"
Assert-ImporterPlatform "Managed runtime importer" $managedImporter "Any" $true @("Exclude Android: 0", "Exclude Editor: 0", "Exclude Win64: 0")

$analyzerImporter = Read-PackageText "Runtime/Luau.SourceGenerator.dll.meta"
Assert-ContainsLiteral "Source generator importer" $analyzerImporter "labels:`n- RoslynAnalyzer"
Assert-ContainsLiteral "Source generator importer" $analyzerImporter "isExplicitlyReferenced: 1"
Assert-ContainsLiteral "Source generator importer" $analyzerImporter "validateReferences: 0"
foreach ($platform in @("Android", "Any", "Editor", "Linux64", "OSXUniversal", "Win", "Win64", "WindowsStoreApps", "iOS")) {
    Assert-ImporterPlatform "Source generator importer" $analyzerImporter $platform $false
}
Assert-ContainsLiteral "Source generator importer Any exclusions" (Get-ImporterPlatformSection "Source generator importer" $analyzerImporter "Any") "Exclude WebGL: 1"

$windowsImporter = Read-PackageText "Runtime/Plugins/win-x64/luau_host.dll.meta"
Assert-ImporterPlatform "Windows native importer" $windowsImporter "Any" $false @("Exclude Android: 1", "Exclude Editor: 0", "Exclude Win64: 0")
Assert-ImporterPlatform "Windows native importer" $windowsImporter "Editor" $true @("CPU: x86_64", "OS: Windows")
Assert-ImporterPlatform "Windows native importer" $windowsImporter "Win64" $true @("CPU: x86_64")
foreach ($platform in @("Android", "Linux64", "OSXUniversal", "Win", "WindowsStoreApps", "iOS")) {
    Assert-ImporterPlatform "Windows native importer" $windowsImporter $platform $false
}

$androidArm64Importer = Read-PackageText "Runtime/Plugins/android-arm64/libluau_host.so.meta"
Assert-ImporterPlatform "Android ARM64 native importer" $androidArm64Importer "Android" $true @("CPU: ARM64")
Assert-ImporterPlatform "Android ARM64 native importer" $androidArm64Importer "Any" $false @("Exclude Android: 0", "Exclude Editor: 1", "Exclude Win64: 1")
foreach ($platform in @("Editor", "Linux64", "OSXUniversal", "Win", "Win64", "iOS")) {
    Assert-ImporterPlatform "Android ARM64 native importer" $androidArm64Importer $platform $false
}

$androidX64Importer = Read-PackageText "Runtime/Plugins/android-x64/libluau_host.so.meta"
Assert-ImporterPlatform "Android x86_64 native importer" $androidX64Importer "Android" $true @("CPU: X86_64")
Assert-ImporterPlatform "Android x86_64 native importer" $androidX64Importer "Any" $false @("Exclude Android: 0", "Exclude Editor: 1", "Exclude Win64: 1")
foreach ($platform in @("Editor", "Linux64", "OSXUniversal", "Win", "Win64", "iOS")) {
    Assert-ImporterPlatform "Android x86_64 native importer" $androidX64Importer $platform $false
}
Write-Host "PASS: analyzer and native importer matrices exactly select analyzer-only, Windows x86_64, Android ARM64, and Android x86_64 targets."


$runtimeAsmdef = Read-PackageJson "Runtime/Luau.Unity.asmdef"
Assert-Equal "Runtime assembly name" $runtimeAsmdef.name "Luau.Unity"
Assert-Equal "Runtime root namespace" $runtimeAsmdef.rootNamespace ""
Assert-SequenceEqual `
    "Runtime assembly references" `
    @($runtimeAsmdef.references) `
    @("GUID:d61c9d398cb91443ea08b5afda6f6d75")
Assert-Equal "Runtime unsafe-code policy" ([bool]$runtimeAsmdef.allowUnsafeCode) $false
Assert-Equal "Runtime override-references policy" ([bool]$runtimeAsmdef.overrideReferences) $false
Assert-Equal "Runtime auto-reference policy" ([bool]$runtimeAsmdef.autoReferenced) $true
Assert-Equal "Runtime engine-reference policy" ([bool]$runtimeAsmdef.noEngineReferences) $false
Assert-SequenceEqual "Runtime included platforms" @($runtimeAsmdef.includePlatforms) @()
Assert-SequenceEqual "Runtime excluded platforms" @($runtimeAsmdef.excludePlatforms) @()
Assert-SequenceEqual "Runtime precompiled references" @($runtimeAsmdef.precompiledReferences) @()
Assert-SequenceEqual "Runtime define constraints" @($runtimeAsmdef.defineConstraints) @()
Assert-SequenceEqual "Runtime version defines" @($runtimeAsmdef.versionDefines) @()
Assert-MetaGuid `
    "Runtime assembly GUID" `
    (Get-PackageFile "Runtime/Luau.Unity.asmdef.meta") `
    "c727d2ef8dd2e4846ab81fbe6ca1f508"
Write-Host "PASS: runtime assembly references only package-owned interop and enables no unsafe code."

$interopAsmdef = Read-PackageJson "Runtime/Interop/Luau.Interop.asmdef"
Assert-Equal "Interop assembly name" $interopAsmdef.name "Luau.Interop"
Assert-Equal "Interop root namespace" $interopAsmdef.rootNamespace ""
Assert-SequenceEqual "Interop assembly references" @($interopAsmdef.references) @()
Assert-Equal "Interop unsafe-code policy" ([bool]$interopAsmdef.allowUnsafeCode) $true
Assert-Equal "Interop override-references policy" ([bool]$interopAsmdef.overrideReferences) $false
Assert-Equal "Interop auto-reference policy" ([bool]$interopAsmdef.autoReferenced) $false
Assert-Equal "Interop engine-reference policy" ([bool]$interopAsmdef.noEngineReferences) $true
Assert-SequenceEqual "Interop included platforms" @($interopAsmdef.includePlatforms) @()
Assert-SequenceEqual "Interop excluded platforms" @($interopAsmdef.excludePlatforms) @()
Assert-SequenceEqual "Interop precompiled references" @($interopAsmdef.precompiledReferences) @()
Assert-SequenceEqual "Interop define constraints" @($interopAsmdef.defineConstraints) @()
Assert-SequenceEqual "Interop version defines" @($interopAsmdef.versionDefines) @()
Assert-MetaGuid `
    "Interop assembly GUID" `
    (Get-PackageFile "Runtime/Interop/Luau.Interop.asmdef.meta") `
    "d61c9d398cb91443ea08b5afda6f6d75"
$interopSourceInventory = @(
    Get-ChildItem -LiteralPath (Join-Path $packageRoot "Runtime/Interop") -File -Filter "*.cs" |
        ForEach-Object Name |
        Sort-Object
)
Assert-SequenceEqual `
    "Package-owned interop source inventory" `
    $interopSourceInventory `
    @("AssemblyInfo.cs", "NativeMethods.cs", "NativeTypes.cs")
Write-Host "PASS: package-owned interop remains isolated and is the sole unsafe source assembly."

$editorAsmdef = Read-PackageJson "Editor/Luau.Unity.Editor.asmdef"
Assert-Equal "Editor assembly name" $editorAsmdef.name "Luau.Unity.Editor"
Assert-Equal "Editor root namespace" $editorAsmdef.rootNamespace ""
Assert-SequenceEqual `
    "Editor assembly references" `
    @($editorAsmdef.references) `
    @("GUID:c727d2ef8dd2e4846ab81fbe6ca1f508")
Assert-SequenceEqual "Editor platforms" @($editorAsmdef.includePlatforms) @("Editor")
Assert-Equal "Editor unsafe-code policy" ([bool]$editorAsmdef.allowUnsafeCode) $false
Assert-Equal "Editor override-references policy" ([bool]$editorAsmdef.overrideReferences) $true
Assert-Equal "Editor auto-reference policy" ([bool]$editorAsmdef.autoReferenced) $true
Assert-Equal "Editor engine-reference policy" ([bool]$editorAsmdef.noEngineReferences) $false
Assert-SequenceEqual "Editor excluded platforms" @($editorAsmdef.excludePlatforms) @()
Assert-SequenceEqual "Editor precompiled references" @($editorAsmdef.precompiledReferences) @("Luau.dll")
Assert-SequenceEqual "Editor define constraints" @($editorAsmdef.defineConstraints) @()
Assert-SequenceEqual "Editor version defines" @($editorAsmdef.versionDefines) @()
Assert-MetaGuid `
    "Editor assembly GUID" `
    (Get-PackageFile "Editor/Luau.Unity.Editor.asmdef.meta") `
    "827e3872b9ef548b0b8e2ab0ad7f3ff4"
Write-Host "PASS: editor code references only the package runtime and shipped managed artifact."

$testAsmdef = Read-PackageJson "Tests/EditMode/Luau.Unity.EditModeTests.asmdef"
Assert-Equal "EditMode test assembly name" $testAsmdef.name "Luau.Unity.EditModeTests"
Assert-Equal "EditMode test root namespace" $testAsmdef.rootNamespace "Luau.Unity.Tests"
Assert-SequenceEqual `
    "EditMode test assembly references" `
    @($testAsmdef.references) `
    @(
        "GUID:c727d2ef8dd2e4846ab81fbe6ca1f508",
        "GUID:827e3872b9ef548b0b8e2ab0ad7f3ff4"
    )
Assert-SequenceEqual "EditMode test platforms" @($testAsmdef.includePlatforms) @("Editor")
Assert-Equal "EditMode test unsafe-code policy" ([bool]$testAsmdef.allowUnsafeCode) $false
Assert-Equal "EditMode test override-references policy" ([bool]$testAsmdef.overrideReferences) $true
Assert-Equal "EditMode test auto-reference policy" ([bool]$testAsmdef.autoReferenced) $false
Assert-Equal "EditMode test engine-reference policy" ([bool]$testAsmdef.noEngineReferences) $false
Assert-SequenceEqual "EditMode test excluded platforms" @($testAsmdef.excludePlatforms) @()
Assert-SequenceEqual "EditMode test precompiled references" @($testAsmdef.precompiledReferences) @("Luau.dll")
Assert-SequenceEqual "EditMode test define constraints" @($testAsmdef.defineConstraints) @()
Assert-SequenceEqual "EditMode test version defines" @($testAsmdef.versionDefines) @()
Assert-SequenceEqual "EditMode test optional references" @($testAsmdef.optionalUnityReferences) @("TestAssemblies")
Assert-MetaGuid `
    "EditMode test assembly GUID" `
    (Get-PackageFile "Tests/EditMode/Luau.Unity.EditModeTests.asmdef.meta") `
    "272c0a43a4974766a1266dfbf3844615"

$assemblyNames = @(
    Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Filter "*.asmdef" |
        ForEach-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).name } |
        Sort-Object
)
Assert-SequenceEqual `
    "Package assembly inventory" `
    $assemblyNames `
    @("Luau.Interop", "Luau.Unity", "Luau.Unity.EditModeTests", "Luau.Unity.Editor")
Write-Host "PASS: all package assemblies have explicit package-local dependency boundaries."

$verificationRuntimeAsmdef = Read-IntegrationJson "Assets/Verification/Runtime/Luau.Unity.Verification.asmdef"
Assert-Equal "Verification Runtime assembly name" $verificationRuntimeAsmdef.name "Luau.Unity.Verification"
Assert-Equal "Verification Runtime root namespace" $verificationRuntimeAsmdef.rootNamespace ""
Assert-SequenceEqual `
    "Verification Runtime references" `
    @($verificationRuntimeAsmdef.references) `
    @("GUID:c727d2ef8dd2e4846ab81fbe6ca1f508")
Assert-SequenceEqual "Verification Runtime included platforms" @($verificationRuntimeAsmdef.includePlatforms) @()
Assert-SequenceEqual "Verification Runtime excluded platforms" @($verificationRuntimeAsmdef.excludePlatforms) @()
Assert-Equal "Verification Runtime unsafe-code policy" ([bool]$verificationRuntimeAsmdef.allowUnsafeCode) $false
Assert-Equal "Verification Runtime override-references policy" ([bool]$verificationRuntimeAsmdef.overrideReferences) $false
Assert-Equal "Verification Runtime auto-reference policy" ([bool]$verificationRuntimeAsmdef.autoReferenced) $true
Assert-Equal "Verification Runtime engine-reference policy" ([bool]$verificationRuntimeAsmdef.noEngineReferences) $false
Assert-SequenceEqual "Verification Runtime precompiled references" @($verificationRuntimeAsmdef.precompiledReferences) @()
Assert-SequenceEqual "Verification Runtime define constraints" @($verificationRuntimeAsmdef.defineConstraints) @()
Assert-SequenceEqual "Verification Runtime version defines" @($verificationRuntimeAsmdef.versionDefines) @()
Assert-MetaGuid `
    "Verification Runtime assembly GUID" `
    (Get-IntegrationFile "Assets/Verification/Runtime/Luau.Unity.Verification.asmdef.meta") `
    "ae747f3b7ca5411e97eb0bc61ae3f38e"

$verificationEditorAsmdef = Read-IntegrationJson "Assets/Verification/Editor/Luau.Unity.Verification.Editor.asmdef"
Assert-Equal "Verification Editor assembly name" $verificationEditorAsmdef.name "Luau.Unity.Verification.Editor"
Assert-Equal "Verification Editor root namespace" $verificationEditorAsmdef.rootNamespace ""
Assert-SequenceEqual `
    "Verification Editor references" `
    @($verificationEditorAsmdef.references) `
    @(
        "GUID:ae747f3b7ca5411e97eb0bc61ae3f38e",
        "GUID:c727d2ef8dd2e4846ab81fbe6ca1f508"
    )
Assert-SequenceEqual "Verification Editor included platforms" @($verificationEditorAsmdef.includePlatforms) @("Editor")
Assert-SequenceEqual "Verification Editor excluded platforms" @($verificationEditorAsmdef.excludePlatforms) @()
Assert-Equal "Verification Editor unsafe-code policy" ([bool]$verificationEditorAsmdef.allowUnsafeCode) $false
Assert-Equal "Verification Editor override-references policy" ([bool]$verificationEditorAsmdef.overrideReferences) $false
Assert-Equal "Verification Editor auto-reference policy" ([bool]$verificationEditorAsmdef.autoReferenced) $true
Assert-Equal "Verification Editor engine-reference policy" ([bool]$verificationEditorAsmdef.noEngineReferences) $false
Assert-SequenceEqual "Verification Editor precompiled references" @($verificationEditorAsmdef.precompiledReferences) @()
Assert-SequenceEqual "Verification Editor define constraints" @($verificationEditorAsmdef.defineConstraints) @()
Assert-SequenceEqual "Verification Editor version defines" @($verificationEditorAsmdef.versionDefines) @()
Assert-MetaGuid `
    "Verification Editor assembly GUID" `
    (Get-IntegrationFile "Assets/Verification/Editor/Luau.Unity.Verification.Editor.asmdef.meta") `
    "4159a2e1300c42ac934cc6c5c933451f"

Assert-MetaGuid `
    "Player smoke script GUID" `
    (Get-IntegrationFile "Assets/Verification/Runtime/LuauPlayerSmoke.cs.meta") `
    "36da335a91e048ebb78746c6113e4ef6"
Assert-MetaGuid `
    "Player smoke build script GUID" `
    (Get-IntegrationFile "Assets/Verification/Editor/LuauPlayerSmokeBuild.cs.meta") `
    "c170ae794b1f4e88a75d2674259b80c8"

$packageProductText = [string]::Join(
    "`n",
    @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
        Where-Object { @(".asmdef", ".cs", ".json") -contains $_.Extension.ToLowerInvariant() } |
        Sort-Object FullName |
        ForEach-Object { [IO.File]::ReadAllText($_.FullName) }))
foreach ($forbiddenVerificationReference in @(
    "Luau.Unity.Verification",
    "ae747f3b7ca5411e97eb0bc61ae3f38e",
    "4159a2e1300c42ac934cc6c5c933451f",
    "LuauPlayerSmoke"
)) {
    Assert-NotContainsLiteral `
        "Standalone package product assemblies" `
        $packageProductText `
        $forbiddenVerificationReference
}
Write-Host "PASS: player smoke verification lives only in explicit integration-project assemblies with preserved GUIDs and no product coupling."

foreach ($retiredPath in @(
    "Runtime/AddressablesLuauRequirer.cs",
    "Runtime/AddressablesLuauRequirer.cs.meta",
    "Runtime/ResourcesLuauRequirer.cs",
    "Runtime/ResourcesLuauRequirer.cs.meta"
)) {
    Assert-PackagePathAbsent $retiredPath
}
Write-Host "PASS: retired global Resources and Addressables resolvers remain absent."

$importer = Read-PackageText "Editor/LuauImporter.cs"
Assert-ContainsLiteral "Luau importer" $importer '[ScriptedImporter(3, "luau")]'
foreach ($required in @(
    "compilerOutput = LuauCompiler.Compile(source);",
    "catch (LuauCompilationException exception)",
    "asset.SetSource(text, source);",
    "LuauAssetImportSettings.ImportPolicy ==",
    "LuauAssetImportPolicy.AllowFirstPartyPrecompile &&",
    "precompile;"
)) {
    Assert-ContainsLiteral "Luau importer" $importer $required
}
Assert-LiteralOrder `
    "Luau importer compiler-identity dependency ordering" `
    $importer `
    @(
        "LuauCompilerIdentityDependency.DependsOn(ctx);",
        "compilerOutput = LuauCompiler.Compile(source);",
        "LuauCompilerIdentityDependency.ScheduleRegistration(compilerOutput);"
    )

$compilerIdentityDependency = Read-PackageText "Editor/LuauCompilerIdentityDependency.cs"
Get-PackageFile "Editor/LuauCompilerIdentityDependency.cs.meta" |
    Out-Null
foreach ($required in @(
    'internal const string Name = "Luau.Unity.CompilerIdentity";',
    "context.DependsOnCustomDependency(Name);",
    "var identity = string.Join(",
    "LuauBytecodeArtifact.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)",
    "options.OptimizationLevel.ToString(CultureInfo.InvariantCulture)",
    "options.DebugLevel.ToString(CultureInfo.InvariantCulture)",
    "options.TypeInfoLevel.ToString(CultureInfo.InvariantCulture)",
    "options.CoverageLevel.ToString(CultureInfo.InvariantCulture)",
    "internal static Hash128 ComputeHash(LuauCompilerOutput output)",
    'output.UpstreamRevisionHash.ToString("x16", CultureInfo.InvariantCulture)',
    'output.HostBuildFingerprint.ToString("x16", CultureInfo.InvariantCulture)',
    "return Hash128.Compute(identity);",
    "EditorApplication.delayCall += RegisterPending;",
    "AssetDatabase.RegisterCustomDependency(Name, hash);",
    "[InitializeOnLoadMethod]",
    "EditorApplication.delayCall += RegisterAtEditorReady;",
    "ComputeHash(LuauCompiler.Compile(ReadOnlySpan<byte>.Empty))"
)) {
    Assert-ContainsLiteral "Luau compiler identity dependency" $compilerIdentityDependency $required
}
$importerPolicyTests = Read-PackageText "Tests/EditMode/LuauImporterPolicyTests.cs"
foreach ($required in @(
    "CompilerIdentityTracksCoverageInstrumentation",
    "CoverageLevel = 2",
    "coverageOutput.BytecodeSha256",
    "LuauCompilerIdentityDependency.ComputeHash(coverageOutput)",
    "LuauCompilerIdentityDependency.ComputeHash(defaultOutput)"
)) {
    Assert-ContainsLiteral "Luau compiler coverage identity test" $importerPolicyTests $required
}
Write-Host "PASS: importer invalidation tracks compiler schema, options, and exact native identity before persistent artifact creation."
Write-Host "PASS: SourceOnly imports compile transiently for diagnostics but persist source."

$projectSettings = Read-PackageText "Editor/LuauUnityProjectSettings.cs"
foreach ($required in @(
    "SourceOnly = 0",
    "AllowFirstPartyPrecompile = 1",
    "LuauAssetImportPolicy importPolicy = LuauAssetImportPolicy.SourceOnly;"
)) {
    Assert-ContainsLiteral "Luau project import policy" $projectSettings $required
}

$checkedInSettings = Read-IntegrationText "ProjectSettings/LuauUnitySettings.asset"
Assert-ContainsLiteral "Checked-in Luau project settings" $checkedInSettings "importPolicy: 0"
Write-Host "PASS: the project-wide import policy is explicitly SourceOnly by default with one first-party opt-in mode."

foreach ($required in @(
    "LuauAssetImportSettings.FirstPartyProvenanceId",
    "AssetDatabase.AssetPathToGUID(ctx.assetPath)",
    "var artifact = LuauBytecodeArtifact.Create(",
    "Encoding.UTF8.GetBytes(assetGuid)",
    "asset.SetVerifiedBytecode(sourceText, artifact);"
)) {
    Assert-ContainsLiteral "First-party Luau precompile path" $importer $required
}
Write-Host "PASS: first-party precompile stores an artifact with configured provenance and the stable asset GUID."

$asset = Read-PackageText "Runtime/LuauAsset.cs"
foreach ($required in @(
    "Source = 0",
    "VerifiedBytecode = 1",
    "internal bool IsSource => contentKind == LuauAssetContentKind.Source;",
    "public ReadOnlySpan<byte> AsSpan()",
    "public ReadOnlyMemory<byte> AsMemory()",
    "throw InvalidContentKind();",
    "internal LuauBytecodeArtifact GetVerifiedBytecode()"
)) {
    Assert-ContainsLiteral "Luau asset" $asset $required
}
Assert-LiteralCount "Luau source access fail-closed guards" $asset "if (!IsSource)" 2
Write-Host "PASS: public source access fails closed for bytecode and unknown serialized content kinds."

$stateExtensions = Read-PackageText "Runtime/LuauStateExtensions.cs"
Assert-ContainsLiteral "Luau asset execution extensions" $stateExtensions "state.DoString("
Assert-ContainsLiteral "Luau asset execution extensions" $stateExtensions "state.DoStringInto("
Assert-ContainsLiteral "Luau asset execution extensions" $stateExtensions "state.ExecuteVerifiedBytecode("
Assert-ContainsLiteral "Luau asset execution extensions" $stateExtensions "state.ExecuteVerifiedBytecodeInto("
Assert-ContainsLiteral "Luau asset execution extensions" $stateExtensions "public static ValueTask<LuauValue[]> ExecuteAsync("
Assert-ContainsLiteral "Luau asset execution extensions" $stateExtensions "public static ValueTask<int> ExecuteIntoAsync("
Assert-ContainsLiteral "Luau asset execution extensions" $stateExtensions "LuauUnity.CompileAssetSourceAsync"
Assert-NotContainsLiteral "Luau asset execution extensions" $stateExtensions "state.DoStringAsync("
foreach ($forbidden in @(
    "ExecuteTrusted",
    "LoadTrusted",
    "AllowUnvalidated",
    "state.Load(",
    "state.LoadVerifiedBytecode(",
    "state.Execute("
)) {
    Assert-NotContainsLiteral "Luau asset execution extensions" $stateExtensions $forbidden
}

$stateCompilationExtensions = Read-PackageText "Runtime/LuauStateCompilationExtensions.cs"
foreach ($required in @(
    "public static ValueTask<LuauValue[]> ExecuteWithCompilationServiceAsync(",
    "public static ValueTask<int> ExecuteIntoWithCompilationServiceAsync(",
    "SnapshotAssetForAsync(state, asset, cancellationToken)",
    "ValidateSourceSize(state, source.Length, assetName)",
    "state.ExecuteCompilerOutputOnOwnerAsync(",
    "state.ExecuteCompilerOutputIntoOnOwnerAsync(",
    "state.ExecuteVerifiedBytecodeAsync(",
    "state.ExecuteVerifiedBytecodeIntoAsync(",
    "result.CompilationDiagnostic",
    "throw new LuauExecutionCanceledException("
)) {
    Assert-ContainsLiteral "Luau asset compilation execution" $stateCompilationExtensions $required
}
Assert-NotContainsLiteral `
    "Luau asset compilation execution" `
    $stateCompilationExtensions `
    "ExecuteCompilerOutputOnOwnerThreadAsync"

$unityCompilation = Read-PackageText "Runtime/LuauUnityCompilation.cs"
foreach ($required in @(
    "static LuauAssetCompilationProvider assetCompilationProvider = CompileAsync;",
    "internal static ValueTask<LuauCompileResult> CompileAssetSourceAsync(",
    "internal static IDisposable OverrideAssetCompilationProviderForTests(",
    "new LuauThreadedCompilationService(",
    "MaxQueuedRequestCount = windows ? 32 : 16",
    "MaxQueuedSourceBytes = windows ? 8L * 1024 * 1024 : 4L * 1024 * 1024",
    "internal static async Task DrainCompilationServiceAsync(",
    "compilationServiceStopping = true;",
    "await service.DisposeAsync().ConfigureAwait(false);",
    "compilationService = null;",
    "internal static void ResetCompilationServiceAfterDrainForTests()"
)) {
    Assert-ContainsLiteral "Unity package-owned compilation lane" $unityCompilation $required
}
$editorCompilationLifetime = Read-PackageText "Editor/LuauCompilationServiceEditorLifetime.cs"
foreach ($required in @(
    "AssemblyReloadEvents.beforeAssemblyReload += DrainForAssemblyReload;",
    "internal static void DrainForAssemblyReload()",
    "LuauUnity.DrainCompilationServiceAsync("
)) {
    Assert-ContainsLiteral "Unity Editor compilation lane lifetime" $editorCompilationLifetime $required
}
$unityCompilationTests = Read-PackageText "Tests/EditMode/LuauCompilationServiceTests.cs"
foreach ($required in @(
    "EditorReloadHookDrainsSharedLaneAndRejectsAdmissionUntilReset",
    "LuauCompilationServiceEditorLifetime.DrainForAssemblyReload();",
    "LuauUnity.ResetCompilationServiceAfterDrainForTests();",
    'Does.Contain("shutting down")'
)) {
    Assert-ContainsLiteral "Unity shared compilation lane lifecycle tests" $unityCompilationTests $required
}
$playerSmoke = Read-IntegrationText "Assets/Verification/Runtime/LuauPlayerSmoke.cs"
foreach ($required in @(
    "compiledResult = await first.ExecuteAsync(backgroundAsset);",
    '[LuauLibrary("PlayerSmokeCapability", Exposure = LuauLibraryExposure.Capability)]',
    "using var capabilityHandle = root.CreateHandle(capability);",
    "capability.Position = vector.create(1, 2, 3)",
    "capability.Hidden == nil",
    "exception.InnerException is MissingReferenceException"
)) {
    Assert-ContainsLiteral "Luau player ordinary asset and capability smoke" $playerSmoke $required
}
Assert-NotContainsLiteral `
    "Luau player ordinary asset smoke" `
    $playerSmoke `
    "ExecuteCompilerOutputOnOwnerThreadAsync"
Write-Host "PASS: ordinary async asset execution uses the injectable package-owned bounded lane and shared owner dispatch."

$unityPublicApiTests = Read-PackageText "Tests/EditMode/LuauUnityPublicApiTests.cs"
foreach ($required in @(
    "RuntimePublicAndProtectedApiMatchesApprovedInventory",
    "BindingFlags.NonPublic",
    "IsPublicOrProtected",
    "ExecuteExtensionSurfaceContainsOnlyApprovedShapes",
    '"ExecuteInto(Luau.LuauState,Luau.Unity.LuauAsset,System.Span<Luau.LuauValue>)->System.Int32"',
    '"ExecuteAsync(Luau.LuauState,Luau.Unity.LuauAsset,System.Threading.CancellationToken)->System.Threading.Tasks.ValueTask<Luau.LuauValue[]>"',
    'const string ApprovedApiSha256 ='
)) {
    Assert-ContainsLiteral "Unity runtime public API inventory" $unityPublicApiTests $required
}
Assert-NotContainsLiteral "Unity runtime public API inventory" $unityPublicApiTests "REPLACE_AFTER_PROBE"
Write-Host "PASS: Unity public/protected API and exact Execute extension shapes have deterministic approval gates."

$managedLoadSurface = Read-RepositoryText "src/Luau/LuauState.Load.cs"
$managedExecuteSurface = Read-RepositoryText "src/Luau/LuauState.Execute.cs"
$managedBytecodePolicy = Read-RepositoryText "src/Luau/LuauBytecodePolicy.cs"
foreach ($forbidden in @(
    "public LuauFunction Load(",
    "LoadTrustedBytecode"
)) {
    Assert-NotContainsLiteral "Managed bytecode load surface" $managedLoadSurface $forbidden
}
Assert-NotContainsLiteral "Managed bytecode execution surface" $managedExecuteSurface "ExecuteTrustedBytecode"
Assert-NotContainsLiteral "Managed bytecode policy" $managedBytecodePolicy "AllowUnvalidated"
Write-Host "PASS: runtime assets use source compilation or validator-gated ExecuteVerifiedBytecode APIs only."

$sourceOnlyValidator = Read-PackageText "Editor/LuauSourceOnlyAssetValidator.cs"
foreach ($required in @(
    "public static class LuauSourceOnlyAssetValidator",
    "public static IReadOnlyList<string> FindNonSourceAssets(",
    "AssetDatabase.LoadAllAssetsAtPath(path)",
    ".OfType<LuauAsset>()",
    ".Any(asset => !asset.IsSource)",
    "public static void ValidateSourceOnly(",
    "internal sealed class LuauSourceOnlyBuildPreprocessor : IPreprocessBuildWithReport",
    "LuauAssetImportSettings.ImportPolicy ==",
    "LuauAssetImportPolicy.AllowFirstPartyPrecompile)",
    "LuauSourceOnlyAssetValidator.ValidateProject();"
)) {
    Assert-ContainsLiteral "Source-only package validator" $sourceOnlyValidator $required
}
Assert-NotContainsLiteral `
    "Source-only package validator" `
    $sourceOnlyValidator `
    "LuauAssetImportSettings.ImportPolicy != LuauAssetImportPolicy.SourceOnly"

$importerEditor = Read-PackageText "Editor/LuauImporterEditor.cs"
Assert-LiteralCount "Importer precompile field" $importerEditor 'serializedObject.FindProperty("precompile")' 1
Assert-LiteralOrder `
    "Importer policy-specific UI" `
    $importerEditor `
    @(
        "if (policy == LuauAssetImportPolicy.AllowFirstPartyPrecompile)",
        "EditorGUILayout.PropertyField(",
        'serializedObject.FindProperty("precompile")',
        "else",
        "This project stores .luau assets as UTF-8 source."
    )
Write-Host "PASS: SourceOnly hides precompile controls and unknown policy values still receive fail-closed build validation."
Write-Host "PASS: reusable validation and the build preprocessor inspect imported asset content, not importer flags."

$moduleMap = Read-PackageText "Runtime/LuauModuleMap.cs"
foreach ($required in @(
    "public sealed class LuauModuleMap : LuauRequirer",
    "CanonicalizeModuleId",
    "protected override string GetCacheKey",
    "ExecuteModuleSource",
    "(byte[])source.Clone()"
)) {
    Assert-ContainsLiteral "Luau module map" $moduleMap $required
}
foreach ($forbidden in @(
    "using System.IO;",
    "UnityEngine.Resources",
    "Addressables.",
    "ExecuteModuleBytecode",
    "ExecuteTrustedModuleBytecode"
)) {
    Assert-NotContainsLiteral "Luau module map" $moduleMap $forbidden
}

$unityFacade = Read-PackageText "Runtime/LuauUnity.cs"
foreach ($required in @(
    "public LuauStateOptions StateOptions { get; set; } = LuauStateOptions.Default;",
    "public LuauModuleMap ModuleMap { get; set; }",
    "state.OpenRequireLibrary(options.ModuleMap);",
    "return stateOptions with",
    "DefaultExecutionOptions = executionOptions with",
    "DefaultMaxPrintArguments = 32",
    "DefaultMaxPrintUtf8Bytes = 4 * 1024",
    "DefaultMaxPrintMessagesPerSecond = 20"
)) {
    Assert-ContainsLiteral "Unity Luau facade" $unityFacade $required
}
foreach ($forbidden in @("EnableRequire", "public LuauRequirer Requirer")) {
    Assert-NotContainsLiteral "Unity Luau facade" $unityFacade $forbidden
}

$runtimeSource = [string]::Join(
    "`n",
    @(Get-ChildItem -LiteralPath (Join-Path $packageRoot "Runtime") -File -Filter "*.cs" |
        Sort-Object FullName |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }))
foreach ($forbidden in @(
    "UnityEngine.AddressableAssets",
    "UnityEngine.Resources",
    "Resources.Load",
    "ExecuteTrustedBytecode",
    "LoadTrustedBytecode",
    "LuauBytecodePolicy.AllowUnvalidated",
    "state.Load(",
    "state.LoadVerifiedBytecode(",
    "state.Execute("
)) {
    Assert-NotContainsLiteral "Unity package runtime" $runtimeSource $forbidden
}
Write-Host "PASS: module loading is immutable, source-only, canonicalized, and rooted in finite Unity defaults."

$managedPublicApiPath = Get-RepositoryFile "tests/Luau.Tests/PublicApi.approved.txt"
Assert-FileHash `
    "Managed public API approval inventory" `
    $managedPublicApiPath `
    "A61059B416961749E4500B0DD7BBBD08B458E605BF3A6FF99293E7D774665924"
$managedPublicApiProject = Read-RepositoryText "tests/Luau.Tests/Luau.Tests.csproj"
Assert-ContainsLiteral `
    "Managed public API test project" `
    $managedPublicApiProject `
    '<EmbeddedResource Include="PublicApi.approved.txt" LogicalName="Luau.Tests.PublicApi.approved.txt" />'
Assert-ContainsLiteral `
    "Unity public API approval inventory" `
    $unityPublicApiTests `
    '"835c137e18e804c96fc86220019caab7c1378ebb557d871c03218bf1de12da2b"'
Assert-NotContainsLiteral `
    "Unity public API approval inventory" `
    $unityPublicApiTests `
    '"56c52c'
Write-Host "PASS: managed API approval is byte-identical and Unity API approval contains only the reviewed smoke-type removal."

$headerPath = Get-RequiredFile $hostRoot "include/luau_host.h" "Native host ABI header"
$exportsPath = Get-RequiredFile $hostRoot "exports/luau_host.exports" "Native host export allowlist"
Assert-FileHash `
    "Native host ABI header" `
    $headerPath `
    "56D723242B7857B65B5E3BFFEFA54653B229AA7D8015F1798861263DFC962BB2"
Assert-FileHash `
    "Native host export allowlist" `
    $exportsPath `
    "C5F4E112B480D1C057A334F766CD05FF800974E2D648B34100D0BBEA47EA0A6B"
$exports = @(
    Get-Content -LiteralPath $exportsPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -ne "" -and !$_.StartsWith("#", [StringComparison]::Ordinal) }
)
Assert-Equal "Native host export count" $exports.Count 85
Assert-SequenceEqual "Native host sorted export allowlist" $exports @($exports | Sort-Object -CaseSensitive)
foreach ($export in $exports) {
    if (!$export.StartsWith("luau_host_", [StringComparison]::Ordinal)) {
        throw "Native export does not belong to the versioned host ABI: $export"
    }
}

$expectedUpstreamRevision = "6e9b580e2e24643214caf0f4bbbb3db911ca30f3"
$gitModules = Read-RepositoryText ".gitmodules"
Assert-ContainsLiteral "Luau submodule registration" $gitModules "path = native/luau"
Assert-ContainsLiteral "Luau submodule registration" $gitModules "url = https://github.com/luau-lang/luau.git"
Assert-NotContainsLiteral "Luau submodule registration" $gitModules "path = luau`n"

$gitLinkLine = (& git -C $repositoryRoot ls-files --stage -- "native/luau" 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect the native/luau gitlink: $gitLinkLine"
}
$gitLinkMatch = [Regex]::Match(
    $gitLinkLine,
    '^160000\s+([0-9a-fA-F]{40})\s+0\s+native/luau$')
if (!$gitLinkMatch.Success) {
    throw "native/luau is not an exact Git submodule entry: $gitLinkLine"
}
Assert-Equal `
    "Pinned upstream Luau gitlink" `
    $gitLinkMatch.Groups[1].Value.ToLowerInvariant() `
    $expectedUpstreamRevision

if (Test-Path -LiteralPath (Join-Path $upstreamRoot ".git")) {
    $actualUpstreamRevision = (& git -C $upstreamRoot rev-parse HEAD 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the initialized native/luau revision: $actualUpstreamRevision"
    }
    Assert-Equal "Initialized upstream Luau revision" $actualUpstreamRevision $expectedUpstreamRevision

    $upstreamStatus = (& git -C $upstreamRoot status --porcelain 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the initialized native/luau worktree: $upstreamStatus"
    }
    Assert-Equal "Initialized upstream Luau status" $upstreamStatus ""
}

$cmake = Read-RepositoryText "native/luau-host/CMakeLists.txt"
Assert-ContainsLiteral "Native host upstream path" $cmake '"${CMAKE_CURRENT_SOURCE_DIR}/../luau"'
Assert-ContainsLiteral `
    "Native host pinned revision" `
    $cmake `
    "set(LUAU_HOST_UPSTREAM_REVISION `"$expectedUpstreamRevision`")"
Write-Host "PASS: native ABI header, 85-symbol allowlist, checked-in plugins, and pinned upstream revision are unchanged."

$integrationManifest = Read-IntegrationJson "Packages/manifest.json"
$integrationDependency = $integrationManifest.dependencies.PSObject.Properties["com.nuskey.luau.unity"]
if ($null -eq $integrationDependency) {
    throw "The integration project does not declare the standalone Luau.Unity package."
}
Assert-Equal `
    "Integration-project package reference" `
    $integrationDependency.Value `
    "file:../../../Luau.Unity"
Assert-SequenceEqual `
    "Integration-project testable package inventory" `
    @($integrationManifest.testables) `
    @("com.nuskey.luau.unity")

$integrationLock = Read-IntegrationJson "Packages/packages-lock.json"
$integrationLockEntry = $integrationLock.dependencies.PSObject.Properties["com.nuskey.luau.unity"]
if ($null -eq $integrationLockEntry) {
    throw "The integration lock file does not contain the standalone Luau.Unity package."
}
Assert-Equal "Integration lock package reference" $integrationLockEntry.Value.version "file:../../../Luau.Unity"
Assert-Equal "Integration lock package depth" ([int]$integrationLockEntry.Value.depth) 0
Assert-Equal "Integration lock package source" $integrationLockEntry.Value.source "local"
if (@($integrationLockEntry.Value.dependencies.PSObject.Properties).Count -ne 0) {
    throw "The integration lock file records unexpected Luau.Unity dependencies."
}

foreach ($consumerProbeFile in @(
    "tests/Luau.Unity.PackageConsumerProbe/ConsumerProbe.asmdef",
    "tests/Luau.Unity.PackageConsumerProbe/ConsumerApiProbe.cs",
    "tests/Luau.Unity.PackageConsumerProbe/ConsumerGeneratedLibrary.cs",
    "tests/Luau.Unity.PackageConsumerProbe/Editor/RunConsumerProbe.cs"
)) {
    Get-RepositoryFile $consumerProbeFile | Out-Null
}
Write-Host "PASS: the integration project consumes the standalone package and the generated-consumer fixture remains a source-only probe."

$operationalFiles = @(
    "README.md",
    ".gitmodules",
    ".github/workflows/build-luau-host.android.yml",
    ".github/workflows/build-luau-host.windows.yml",
    ".github/workflows/build-luau-host.yml",
    ".github/workflows/validate-managed-package.yml",
    "native/luau-host/CMakeLists.txt",
    "native/luau-host/cmake/Write-ArtifactManifest.ps1",
    "tools/Copy-DotNetArtifactsToUnity.ps1",
    "tools/Copy-NativeArtifactsToUnity.ps1",
    "tools/Test-LuauHostSoak.ps1",
    "tools/Test-ManagedHarnessSelection.ps1",
    "tools/Test-UnityHost.ps1",
    "tools/Test-UnityPackageConsumer.ps1",
    "tools/Test-UnityPackageStatic.ps1",
    "tools/harness/Luau.Interop.csproj",
    "tests/Luau.Unity.Integration/Packages/manifest.json",
    "tests/Luau.Unity.Integration/Packages/packages-lock.json"
)
$obsoleteProjectUnix = @("src", "Luau.Unity") -join "/"
$obsoleteProjectWindows = @("src", "Luau.Unity") -join "\"
$obsoleteEmbeddedPackageUnix = @("Assets", "Luau.Unity") -join "/"
$obsoleteEmbeddedPackageWindows = @("Assets", "Luau.Unity") -join "\"
$obsoleteUpstreamUnix = @("..", "..", "luau") -join "/"
$obsoleteUpstreamWindows = @("..", "..", "luau") -join "\"
foreach ($operationalFile in $operationalFiles) {
    $operationalText = Read-RepositoryText $operationalFile
    foreach ($obsoletePath in @(
        $obsoleteProjectUnix,
        $obsoleteProjectWindows,
        $obsoleteEmbeddedPackageUnix,
        $obsoleteEmbeddedPackageWindows,
        $obsoleteUpstreamUnix,
        $obsoleteUpstreamWindows
    )) {
        Assert-NotContainsLiteral "Operational file $operationalFile" $operationalText $obsoletePath
    }
}

$trackedObsoleteProduct = (& git -C $repositoryRoot ls-files -- $obsoleteProjectUnix ($obsoleteProjectUnix + "/**") 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect tracked obsolete Unity paths: $trackedObsoleteProduct"
}
Assert-Equal "Tracked obsolete Unity project/product paths" $trackedObsoleteProduct ""
$trackedObsoleteSubmodule = (& git -C $repositoryRoot ls-files --stage -- "luau" 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect the obsolete root Luau submodule path: $trackedObsoleteSubmodule"
}
Assert-Equal "Tracked obsolete root Luau submodule path" $trackedObsoleteSubmodule ""

$copyManaged = Read-RepositoryText "tools/Copy-DotNetArtifactsToUnity.ps1"
Assert-ContainsLiteral "Managed artifact copy path" $copyManaged '$packageRoot = Join-Path $root "Luau.Unity"'
$copyNative = Read-RepositoryText "tools/Copy-NativeArtifactsToUnity.ps1"
Assert-ContainsLiteral "Native artifact copy path" $copyNative '$pluginsRoot = Join-Path $packageRoot "Runtime/Plugins"'
$hostSoak = Read-RepositoryText "tools/Test-LuauHostSoak.ps1"
Assert-ContainsLiteral "Host soak default plugin" $hostSoak '"Luau.Unity/Runtime/Plugins/win-x64/luau_host.dll"'
$harnessProject = Read-RepositoryText "tools/harness/Luau.Interop.csproj"
Assert-ContainsLiteral "Harness interop authority" $harnessProject '..\..\Luau.Unity\Runtime\Interop\NativeMethods.cs'
Assert-ContainsLiteral "Harness default plugin" $harnessProject '..\..\Luau.Unity\Runtime\Plugins\win-x64\luau_host.dll'
$artifactManifest = Read-RepositoryText "native/luau-host/cmake/Write-ArtifactManifest.ps1"
Assert-ContainsLiteral "Artifact manifest upstream root" $artifactManifest 'Join-Path $repositoryRoot "native/luau"'
Assert-ContainsLiteral "Artifact manifest interop authority" $artifactManifest 'Join-Path $packageRoot "Runtime/Interop/NativeTypes.cs"'
$disposableHost = Read-RepositoryText "tools/Test-UnityHost.ps1"
Assert-ContainsLiteral "Disposable host integration source" $disposableHost 'Join-Path $repositoryRoot "tests/Luau.Unity.Integration"'
Assert-ContainsLiteral "Disposable host package source" $disposableHost 'Join-Path $repositoryRoot "Luau.Unity"'
Assert-ContainsLiteral "Disposable host staged package" $disposableHost 'Join-Path $projectRoot "Packages/$luauPackageName"'
Write-Host "PASS: operational scripts use only final package, integration-project, interop, plugin, and upstream paths."

$managedWorkflow = Read-RepositoryText ".github/workflows/validate-managed-package.yml"
Assert-ContainsLiteral "Managed validation workflow" $managedWorkflow "pull_request:"
if ([Regex]::IsMatch($managedWorkflow, '(?m)^\s+paths(?:-ignore)?:\s*$')) {
    throw "Managed package validation must remain unfiltered so every compatibility path triggers it."
}
foreach ($requiredCommand in @(
    "dotnet test Luau.slnx",
    "tools/Test-ManagedHarnessSelection.ps1",
    "tools/Copy-DotNetArtifactsToUnity.ps1",
    "tools/Test-UnityPackageStatic.ps1"
)) {
    Assert-ContainsLiteral "Managed validation workflow" $managedWorkflow $requiredCommand
}
$compatibilityPathsCoveredByUnfilteredPullRequests = @(
    "Luau.Unity/Runtime/Interop/AssemblyInfo.cs",
    "Luau.Unity/Runtime/Interop/NativeMethods.cs",
    "Luau.Unity/Runtime/Interop/NativeTypes.cs",
    "Luau.Unity/Runtime/Plugins/win-x64/luau_host.dll",
    "Luau.Unity/Runtime/Plugins/win-x64/luau_host.dll.meta",
    "Luau.Unity/Runtime/Plugins/android-arm64/libluau_host.so",
    "Luau.Unity/Runtime/Plugins/android-arm64/libluau_host.so.meta",
    "Luau.Unity/Runtime/Plugins/android-x64/libluau_host.so",
    "Luau.Unity/Runtime/Plugins/android-x64/libluau_host.so.meta",
    "tools/Copy-DotNetArtifactsToUnity.ps1",
    "tools/Copy-NativeArtifactsToUnity.ps1",
    "native/luau-host/include/luau_host.h",
    "native/luau-host/exports/luau_host.exports",
    "native/luau-host/tests/luau_host_conformance.cpp",
    "native/luau-host/tests/luau_host_invalid_abi_fixture.c",
    ".gitmodules",
    "tests/Luau.Tests/Luau.Tests.csproj",
    "tests/Luau.Tests/PublicApiContractTests.cs",
    "tests/Luau.Tests/NativeAbiHandshakeTests.cs",
    "tests/Luau.ConsumerContract/Luau.ConsumerContract.csproj",
    "tests/Luau.ConsumerContract/PublicConsumerProbe.cs"
)
foreach ($compatibilityPath in $compatibilityPathsCoveredByUnfilteredPullRequests) {
    Get-RepositoryFile $compatibilityPath | Out-Null
}
Write-Host "PASS: unfiltered pull requests validate API baselines, interop, plugins, native ABI inputs, and deterministic harness paths without adding Stage 6 native lanes."

Write-Host "Unity package static policy validation passed."
