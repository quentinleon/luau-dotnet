param(
    [string] $UnityPath,
    [string] $PackageReference,
    [string] $OutputRoot
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$integrationProjectRoot = Join-Path $repositoryRoot "tests/Luau.Unity.Integration"
$packageRoot = Join-Path $repositoryRoot "Luau.Unity"
$fixtureRoot = Join-Path $repositoryRoot "tests/Luau.Unity.PackageConsumerProbe"
$allowedOutputRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot "native/luau-host/out"))
$versionFile = Join-Path $integrationProjectRoot "ProjectSettings/ProjectVersion.txt"
$passedMarker = "LUAU_PACKAGE_CONSUMER_PASS"
$failedMarker = "LUAU_PACKAGE_CONSUMER_FAIL"

function Assert-ExistingFile([string] $Path, [string] $Description) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }
}

function Assert-ExistingDirectory([string] $Path, [string] $Description) {
    if (!(Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description is missing: $Path"
    }
}

function Test-IsSameOrDescendant([string] $Path, [string] $Parent) {
    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $fullParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $parentPrefix = $fullParent + [System.IO.Path]::DirectorySeparatorChar

    return $fullPath.Equals($fullParent, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($parentPrefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-StrictDescendant([string] $Path, [string] $Parent) {
    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $fullParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $parentPrefix = $fullParent + [System.IO.Path]::DirectorySeparatorChar

    if (!$fullPath.StartsWith($parentPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path that is not a strict descendant of $fullParent`: $fullPath"
    }
}

function Remove-DisposableProject([string] $Path) {
    Assert-StrictDescendant $Path $allowedOutputRoot
    $lastError = $null
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            $lastError = $_
            if ($attempt -lt 20) {
                Start-Sleep -Milliseconds 250
            }
        }
    }

    throw "The generated Unity package consumer passed but cleanup failed: $Path`n$lastError"
}

function ConvertTo-UpmPackageReference([string] $Reference) {
    if ([string]::IsNullOrWhiteSpace($Reference)) {
        Assert-ExistingDirectory $packageRoot "Standalone package"
        return "file:" + $packageRoot.Replace(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
    }

    $candidate = $Reference.Trim()
    $pathCandidate = $candidate
    $hasFilePrefix = $candidate.StartsWith(
        "file:",
        [System.StringComparison]::OrdinalIgnoreCase)
    if ($hasFilePrefix) {
        $pathCandidate = $candidate.Substring(5)
    }

    if ([System.IO.Path]::IsPathRooted($pathCandidate)) {
        if (!(Test-Path -LiteralPath $pathCandidate)) {
            throw "The local package reference does not exist: $pathCandidate"
        }

        $fullPackagePath = [System.IO.Path]::GetFullPath($pathCandidate)
        return "file:" + $fullPackagePath.Replace(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
    }

    return $candidate
}

Assert-ExistingFile $versionFile "Integration-project Unity version"
Assert-ExistingDirectory $fixtureRoot "Package-consumer source fixture"

$requiredFixtureFiles = @(
    "ConsumerProbe.asmdef",
    "ConsumerProbe.asmdef.meta",
    "ConsumerApiProbe.cs",
    "ConsumerApiProbe.cs.meta",
    "ConsumerGeneratedLibrary.cs",
    "ConsumerGeneratedLibrary.cs.meta",
    "Editor.meta",
    "Editor/RunConsumerProbe.cs",
    "Editor/RunConsumerProbe.cs.meta"
)
foreach ($relativePath in $requiredFixtureFiles) {
    Assert-ExistingFile (Join-Path $fixtureRoot $relativePath) "Package-consumer fixture file"
}

$unexpectedFixtureFiles = @(Get-ChildItem -LiteralPath $fixtureRoot -Recurse -File |
    ForEach-Object {
        $_.FullName.Substring($fixtureRoot.Length + 1).Replace(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
    } |
    Where-Object { $requiredFixtureFiles -notcontains $_ })
if ($unexpectedFixtureFiles.Count -ne 0) {
    throw "The package-consumer fixture contains unexpected files: $($unexpectedFixtureFiles -join ', ')"
}

$versionLine = Get-Content -LiteralPath $versionFile |
    Where-Object { $_ -like "m_EditorVersion:*" } |
    Select-Object -First 1
if (!$versionLine) {
    throw "The integration-project Unity version is missing from $versionFile."
}
$unityVersion = ($versionLine -split ":", 2)[1].Trim()

if (!$UnityPath) {
    $candidates = [System.Collections.Generic.List[string]]::new()
    if ($env:UNITY_EXE) {
        $candidates.Add($env:UNITY_EXE)
    }
    if ($env:UNITY_PATH) {
        $candidates.Add($env:UNITY_PATH)
    }
    if ($env:ProgramFiles) {
        $candidates.Add((Join-Path $env:ProgramFiles "Unity/Hub/Editor/$unityVersion/Editor/Unity.exe"))
    }
    if (${env:ProgramFiles(x86)}) {
        $candidates.Add(
            (Join-Path ${env:ProgramFiles(x86)} "Unity/Hub/Editor/$unityVersion/Editor/Unity.exe"))
    }

    $UnityPath = $candidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if (!$UnityPath -or !(Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity $unityVersion was not found. Pass -UnityPath with the matching Unity.exe."
}
$UnityPath = (Resolve-Path -LiteralPath $UnityPath).Path
$resolvedPackageReference = ConvertTo-UpmPackageReference $PackageReference

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = $allowedOutputRoot
}
$outputParent = [System.IO.Path]::GetFullPath($OutputRoot)
if (!(Test-IsSameOrDescendant $outputParent $allowedOutputRoot)) {
    throw "The package-consumer output root must stay under $allowedOutputRoot`: $outputParent"
}
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null

$projectPath = Join-Path $outputParent (
    "unity-package-consumer-" + [Guid]::NewGuid().ToString("N"))
Assert-StrictDescendant $projectPath $allowedOutputRoot
$assetsPath = Join-Path $projectPath "Assets/ConsumerProbe"
$packagesPath = Join-Path $projectPath "Packages"
$projectSettingsPath = Join-Path $projectPath "ProjectSettings"
$logPath = Join-Path $projectPath "Logs/package-consumer.log"

New-Item -ItemType Directory -Path $assetsPath -Force | Out-Null
New-Item -ItemType Directory -Path $packagesPath -Force | Out-Null
New-Item -ItemType Directory -Path $projectSettingsPath -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $logPath) -Force | Out-Null
Get-ChildItem -LiteralPath $fixtureRoot -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $assetsPath -Recurse -Force
}
Copy-Item -LiteralPath $versionFile -Destination (
    Join-Path $projectSettingsPath "ProjectVersion.txt") -Force

$manifest = [ordered]@{
    dependencies = [ordered]@{
        "com.nuskey.luau.unity" = $resolvedPackageReference
    }
}
$manifestJson = $manifest | ConvertTo-Json -Depth 4
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText(
    (Join-Path $packagesPath "manifest.json"),
    $manifestJson,
    $utf8WithoutBom)

Write-Host "Running generated minimal Unity package consumer with Unity $unityVersion."
Write-Host "Disposable project: $projectPath"
$unityArguments = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath",
    ('"' + $projectPath + '"'),
    "-executeMethod",
    "Luau.Unity.PackageConsumerProbe.RunConsumerProbe.Execute",
    "-logFile",
    ('"' + $logPath + '"')
)

$unityProcess = Start-Process `
    -FilePath $UnityPath `
    -ArgumentList $unityArguments `
    -WindowStyle Hidden `
    -PassThru
try {
    $unityProcess.WaitForExit()
    $unityProcess.Refresh()
    $exitCode = $unityProcess.ExitCode
}
finally {
    $unityProcess.Dispose()
}

$log = if (Test-Path -LiteralPath $logPath -PathType Leaf) {
    Get-Content -LiteralPath $logPath -Raw
} else {
    ""
}
$reportedFailure = $log -match [regex]::Escape($failedMarker)
$compilerOrPackageFailure = $log -match (
    "(?im)^.*(?:error CS\d+|Scripts have compiler errors|Compilation failed|" +
    "Failed to resolve packages?|An error occurred while resolving packages?|" +
    "DllNotFoundException.*luau_host|EntryPointNotFoundException.*luau_host).*$")
$passed = $log -match [regex]::Escape($passedMarker)

if ($exitCode -ne 0 -or !$passed -or $reportedFailure -or $compilerOrPackageFailure) {
    if ($log) {
        Get-Content -LiteralPath $logPath -Tail 160
    }

    Write-Host "Failed disposable project and log retained at $projectPath"
    throw (
        "The generated Unity package consumer failed " +
        "(Unity exit code $exitCode, pass marker present: $passed).")
}

Remove-DisposableProject $projectPath
if (Test-Path -LiteralPath $projectPath) {
    throw "The generated Unity package consumer passed but cleanup failed: $projectPath"
}

Write-Host (
    "Generated minimal Unity consumer resolved the package, compiled source-generator output, " +
    "loaded the native VM, and executed successfully.")
