<#
.SYNOPSIS
Runs the deterministic Stage 3 lifetime and fault soak harness against luau_host.

.DESCRIPTION
Builds and runs the final managed host harness once, writes a structured JSON
report, and fails if any deterministic scenario or soak group fails.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools/Test-LuauHostSoak.ps1 -SoakIterations 25
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [ValidateRange(1, 10000)]
    [int] $SoakIterations = 25,

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
$plugin = Join-Path $repositoryRoot "src/Luau.Unity/Assets/Luau.Unity/Interop/Plugins/win-x64/luau_host.dll"
if (!(Test-Path -LiteralPath $plugin -PathType Leaf)) {
    throw "The Windows luau_host plugin was not found at $plugin. Build the windows-x64 CMake preset and install the artifact first."
}

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
    "--",
    "run", "--output", $report,
    "--soak-iterations", $SoakIterations.ToString([Globalization.CultureInfo]::InvariantCulture)
)

Write-Host "Stage 3 luau_host soak validation passed."
Write-Host "Report: $report"
