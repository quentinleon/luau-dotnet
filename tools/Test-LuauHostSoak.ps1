<#
.SYNOPSIS
Runs the deterministic Stage 3 lifetime and fault soak harness against luau_host.

.DESCRIPTION
Builds and runs the final managed host harness once, writes a structured JSON
report, and fails if any deterministic scenario or soak group fails. The
checked-in Windows package plugin is used unless -NativeHostPath explicitly
selects another artifact by absolute path.

.PARAMETER NativeHostPath
Absolute path to the Windows luau_host artifact to test. Defaults to the
checked-in package plugin.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools/Test-LuauHostSoak.ps1 -SoakIterations 25
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [ValidateRange(1, 10000)]
    [int] $SoakIterations = 25,

    [string] $NativeHostPath,

    [string] $OutputRoot
)

$ErrorActionPreference = "Stop"

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Command,
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,
        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    Write-Host "==> $Description"
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$project = Join-Path $repositoryRoot "tests/Luau.HostSoak/Luau.HostSoak.csproj"
$defaultNativeHostPath = Join-Path $repositoryRoot "Luau.Unity/Runtime/Plugins/win-x64/luau_host.dll"
if ([string]::IsNullOrWhiteSpace($NativeHostPath)) {
    $NativeHostPath = $defaultNativeHostPath
}
elseif (![System.IO.Path]::IsPathRooted($NativeHostPath)) {
    throw "NativeHostPath must be an absolute path when supplied explicitly. Selected value: '$NativeHostPath'."
}

$nativeHost = [System.IO.Path]::GetFullPath($NativeHostPath)
if (!(Test-Path -LiteralPath $nativeHost -PathType Leaf)) {
    throw "The selected Windows luau_host artifact was not found at $nativeHost."
}
$nativeHostHash = (Get-FileHash -LiteralPath $nativeHost -Algorithm SHA256).Hash
Write-Host "Selected luau_host native artifact: $nativeHost"
Write-Host "Selected luau_host SHA256: $nativeHostHash"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot "artifacts/stage-3-host-soak"
}
elseif (![System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot $OutputRoot
}
$output = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $output -Force | Out-Null

$report = Join-Path $output "luau-host.json"
$dotnetArtifacts = Join-Path $output "dotnet"

Invoke-CheckedCommand -Command "dotnet" -Description "Run luau_host lifetime/fault soak harness" -Arguments @(
    "run", "--project", $project,
    "--configuration", $Configuration,
    "--artifacts-path", $dotnetArtifacts,
    "-p:LuauHostNativePath=$nativeHost",
    "--",
    "run", "--output", $report,
    "--soak-iterations", $SoakIterations.ToString([Globalization.CultureInfo]::InvariantCulture)
)

Write-Host "Stage 3 luau_host soak validation passed."
Write-Host "Report: $report"
