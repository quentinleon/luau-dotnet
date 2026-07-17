param(
    [string] $UnityPath
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "tests/Luau.Unity.PackageConsumer"
$versionFile = Join-Path $project "ProjectSettings/ProjectVersion.txt"
$versionLine = Get-Content -LiteralPath $versionFile |
    Where-Object { $_ -like "m_EditorVersion:*" } |
    Select-Object -First 1

if (!$versionLine) {
    throw "The package-consumer Unity version is missing from $versionFile."
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
        $candidates.Add((Join-Path ${env:ProgramFiles(x86)} "Unity/Hub/Editor/$unityVersion/Editor/Unity.exe"))
    }

    $UnityPath = $candidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if (!$UnityPath -or !(Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity $unityVersion was not found. Pass -UnityPath with the matching Unity.exe."
}

$UnityPath = (Resolve-Path -LiteralPath $UnityPath).Path
$projectPath = [System.IO.Path]::GetFullPath($project)
$libraryPath = [System.IO.Path]::GetFullPath((Join-Path $project "Library"))
$projectPrefix = $projectPath.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (!$libraryPath.StartsWith($projectPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a package-consumer Library outside $projectPath."
}
if (Test-Path -LiteralPath $libraryPath) {
    Remove-Item -LiteralPath $libraryPath -Recurse -Force
}

$logDirectory = Join-Path $project "Logs"
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory "package-consumer-compile.log"

Write-Host "Compiling clean path-only package consumer with Unity $unityVersion."
$unityProcess = Start-Process `
    -FilePath $UnityPath `
    -ArgumentList @(
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath",
        $project,
        "-logFile",
        $logPath
    ) `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
$exitCode = $unityProcess.ExitCode

$log = if (Test-Path -LiteralPath $logPath) {
    Get-Content -LiteralPath $logPath -Raw
} else {
    ""
}

$compileFailure = $log -match "(?m)^.*(?:error CS\d+|Scripts have compiler errors|Compilation failed).*$"
if ($exitCode -ne 0 -or $compileFailure) {
    if ($log) {
        Get-Content -LiteralPath $logPath -Tail 120
    }

    throw "The clean Unity package consumer failed to compile (Unity exit code $exitCode)."
}

Write-Host "Clean path-only Unity package consumer compiled successfully."
