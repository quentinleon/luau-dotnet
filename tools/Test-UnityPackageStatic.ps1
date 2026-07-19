Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$packageRoot = Join-Path $repositoryRoot "src/Luau.Unity/Assets/Luau.Unity"

function Get-RepositoryFile {
    param([string] $RelativePath)

    $path = Join-Path $repositoryRoot $RelativePath
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required repository file is missing: $RelativePath"
    }

    return $path
}

function Read-RepositoryText {
    param([string] $RelativePath)

    return [IO.File]::ReadAllText((Get-RepositoryFile $RelativePath))
}

function Read-RepositoryJson {
    param([string] $RelativePath)

    return Read-RepositoryText $RelativePath | ConvertFrom-Json
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

$runtimeAsmdef = Read-RepositoryJson "src/Luau.Unity/Assets/Luau.Unity/Runtime/Luau.Unity.asmdef"
Assert-Equal "Runtime assembly name" $runtimeAsmdef.name "Luau.Unity"
Assert-SequenceEqual `
    "Runtime assembly references" `
    @($runtimeAsmdef.references) `
    @("GUID:d61c9d398cb91443ea08b5afda6f6d75")
Assert-Equal "Runtime unsafe-code policy" ([bool]$runtimeAsmdef.allowUnsafeCode) $false
Assert-Equal "Runtime override-references policy" ([bool]$runtimeAsmdef.overrideReferences) $false
Assert-SequenceEqual "Runtime precompiled references" @($runtimeAsmdef.precompiledReferences) @()
Assert-SequenceEqual "Runtime version defines" @($runtimeAsmdef.versionDefines) @()
Write-Host "PASS: runtime assembly references only package-owned interop and enables no unsafe code."

$interopAsmdef = Read-RepositoryJson "src/Luau.Unity/Assets/Luau.Unity/Interop/Luau.Interop.asmdef"
Assert-Equal "Interop assembly name" $interopAsmdef.name "Luau.Interop"
Assert-SequenceEqual "Interop assembly references" @($interopAsmdef.references) @()
Assert-Equal "Interop unsafe-code policy" ([bool]$interopAsmdef.allowUnsafeCode) $true
Assert-Equal "Interop override-references policy" ([bool]$interopAsmdef.overrideReferences) $false
Assert-SequenceEqual "Interop precompiled references" @($interopAsmdef.precompiledReferences) @()
Write-Host "PASS: package-owned interop remains isolated and is the sole unsafe source assembly."

$editorAsmdef = Read-RepositoryJson "src/Luau.Unity/Assets/Luau.Unity/Editor/Luau.Unity.Editor.asmdef"
Assert-Equal "Editor assembly name" $editorAsmdef.name "Luau.Unity.Editor"
Assert-SequenceEqual `
    "Editor assembly references" `
    @($editorAsmdef.references) `
    @("GUID:c727d2ef8dd2e4846ab81fbe6ca1f508")
Assert-SequenceEqual "Editor platforms" @($editorAsmdef.includePlatforms) @("Editor")
Assert-Equal "Editor unsafe-code policy" ([bool]$editorAsmdef.allowUnsafeCode) $false
Assert-Equal "Editor override-references policy" ([bool]$editorAsmdef.overrideReferences) $true
Assert-SequenceEqual "Editor precompiled references" @($editorAsmdef.precompiledReferences) @("Luau.dll")
Assert-SequenceEqual "Editor version defines" @($editorAsmdef.versionDefines) @()
Write-Host "PASS: editor code references only the package runtime and shipped managed artifact."

$testAsmdef = Read-RepositoryJson "src/Luau.Unity/Assets/Luau.Unity/Tests/EditMode/Luau.Unity.EditModeTests.asmdef"
Assert-Equal "EditMode test assembly name" $testAsmdef.name "Luau.Unity.EditModeTests"
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
Assert-SequenceEqual "EditMode test precompiled references" @($testAsmdef.precompiledReferences) @("Luau.dll")
Assert-SequenceEqual "EditMode test optional references" @($testAsmdef.optionalUnityReferences) @("TestAssemblies")

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

$package = Read-RepositoryJson "src/Luau.Unity/Assets/Luau.Unity/package.json"
Assert-Equal "Unity package name" $package.name "com.nuskey.luau.unity"
$dependencyProperty = $package.PSObject.Properties["dependencies"]
if ($null -ne $dependencyProperty -and @($dependencyProperty.Value.PSObject.Properties).Count -ne 0) {
    throw "The Unity package must not rely on undeclared development-project dependencies."
}
Write-Host "PASS: package.json declares no external package dependencies."

foreach ($retiredPath in @(
    "src/Luau.Unity/Assets/Luau.Unity/Runtime/AddressablesLuauRequirer.cs",
    "src/Luau.Unity/Assets/Luau.Unity/Runtime/AddressablesLuauRequirer.cs.meta",
    "src/Luau.Unity/Assets/Luau.Unity/Runtime/ResourcesLuauRequirer.cs",
    "src/Luau.Unity/Assets/Luau.Unity/Runtime/ResourcesLuauRequirer.cs.meta"
)) {
    Assert-PathAbsent $retiredPath
}
Write-Host "PASS: retired global Resources and Addressables resolvers remain absent."

$importer = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Editor/LuauImporter.cs"
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

$compilerIdentityDependency = Read-RepositoryText `
    "src/Luau.Unity/Assets/Luau.Unity/Editor/LuauCompilerIdentityDependency.cs"
Get-RepositoryFile `
    "src/Luau.Unity/Assets/Luau.Unity/Editor/LuauCompilerIdentityDependency.cs.meta" |
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
$importerPolicyTests = Read-RepositoryText `
    "src/Luau.Unity/Assets/Luau.Unity/Tests/EditMode/LuauImporterPolicyTests.cs"
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

$projectSettings = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Editor/LuauUnityProjectSettings.cs"
foreach ($required in @(
    "SourceOnly = 0",
    "AllowFirstPartyPrecompile = 1",
    "LuauAssetImportPolicy importPolicy = LuauAssetImportPolicy.SourceOnly;"
)) {
    Assert-ContainsLiteral "Luau project import policy" $projectSettings $required
}

$checkedInSettings = Read-RepositoryText "src/Luau.Unity/ProjectSettings/LuauUnitySettings.asset"
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

$asset = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Runtime/LuauAsset.cs"
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

$stateExtensions = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Runtime/LuauStateExtensions.cs"
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

$stateCompilationExtensions = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Runtime/LuauStateCompilationExtensions.cs"
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

$unityCompilation = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Runtime/LuauUnityCompilation.cs"
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
$editorCompilationLifetime = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Editor/LuauCompilationServiceEditorLifetime.cs"
foreach ($required in @(
    "AssemblyReloadEvents.beforeAssemblyReload += DrainForAssemblyReload;",
    "internal static void DrainForAssemblyReload()",
    "LuauUnity.DrainCompilationServiceAsync("
)) {
    Assert-ContainsLiteral "Unity Editor compilation lane lifetime" $editorCompilationLifetime $required
}
$unityCompilationTests = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Tests/EditMode/LuauCompilationServiceTests.cs"
foreach ($required in @(
    "EditorReloadHookDrainsSharedLaneAndRejectsAdmissionUntilReset",
    "LuauCompilationServiceEditorLifetime.DrainForAssemblyReload();",
    "LuauUnity.ResetCompilationServiceAfterDrainForTests();",
    'Does.Contain("shutting down")'
)) {
    Assert-ContainsLiteral "Unity shared compilation lane lifecycle tests" $unityCompilationTests $required
}
$playerSmoke = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Runtime/LuauPlayerSmoke.cs"
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

$unityPublicApiTests = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Tests/EditMode/LuauUnityPublicApiTests.cs"
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

$sourceOnlyValidator = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Editor/LuauSourceOnlyAssetValidator.cs"
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

$importerEditor = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Editor/LuauImporterEditor.cs"
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

$moduleMap = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Runtime/LuauModuleMap.cs"
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

$unityFacade = Read-RepositoryText "src/Luau.Unity/Assets/Luau.Unity/Runtime/LuauUnity.cs"
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

Write-Host "Unity package static policy validation passed."
