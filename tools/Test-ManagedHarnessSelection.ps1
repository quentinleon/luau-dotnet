[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$scratchName = 'luau-managed-harness-selection-' + [System.Guid]::NewGuid().ToString('N')
$scratchRoot = [System.IO.Path]::GetFullPath((Join-Path $tempRoot $scratchName))
$succeeded = $false

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Source,

        [Parameter(Mandatory = $true)]
        [string] $Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required source file is missing: $Source"
    }

    $destinationDirectory = Split-Path -Parent $Destination
    [System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination
}

try {
    [System.IO.Directory]::CreateDirectory($scratchRoot) | Out-Null

    $miniHarnessDirectory = Join-Path $scratchRoot 'tools/harness'
    $miniInteropDirectory = Join-Path $scratchRoot 'Luau.Unity/Runtime/Interop'
    $miniPackagePlugin = Join-Path $scratchRoot 'Luau.Unity/Runtime/Plugins/win-x64/luau_host.dll'
    $miniIgnoredPlugin = Join-Path $scratchRoot 'native/luau-host/out/install/windows-x64/luau_host.dll'
    $buildLog = Join-Path $scratchRoot 'build.log'

    Copy-RequiredFile `
        -Source (Join-Path $repoRoot 'Directory.Build.props') `
        -Destination (Join-Path $scratchRoot 'Directory.Build.props')
    Copy-RequiredFile `
        -Source (Join-Path $repoRoot 'tools/harness/Luau.Interop.csproj') `
        -Destination (Join-Path $miniHarnessDirectory 'Luau.Interop.csproj')

    foreach ($interopFile in @('AssemblyInfo.cs', 'NativeTypes.cs', 'NativeMethods.cs')) {
        Copy-RequiredFile `
            -Source (Join-Path $repoRoot "Luau.Unity/Runtime/Interop/$interopFile") `
            -Destination (Join-Path $miniInteropDirectory $interopFile)
    }

    Copy-RequiredFile `
        -Source (Join-Path $repoRoot 'Luau.Unity/Runtime/Plugins/win-x64/luau_host.dll') `
        -Destination $miniPackagePlugin

    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $miniIgnoredPlugin)) | Out-Null
    [System.IO.File]::WriteAllBytes(
        $miniIgnoredPlugin,
        [System.Text.Encoding]::UTF8.GetBytes("stage-5 ignored native build sentinel`n"))

    $packageHash = (Get-FileHash -LiteralPath $miniPackagePlugin -Algorithm SHA256).Hash
    $ignoredHash = (Get-FileHash -LiteralPath $miniIgnoredPlugin -Algorithm SHA256).Hash
    if ($packageHash -eq $ignoredHash) {
        throw 'The package plugin and ignored-build sentinel unexpectedly have the same SHA256 hash.'
    }

    Write-Host "Disposable harness root: $scratchRoot"
    Write-Host "Checked package plugin: $miniPackagePlugin (SHA256=$packageHash)"
    Write-Host "Conflicting ignored plugin: $miniIgnoredPlugin (SHA256=$ignoredHash)"

    Push-Location $scratchRoot
    try {
        & dotnet build 'tools/harness/Luau.Interop.csproj' `
            --configuration Release `
            --nologo `
            --verbosity minimal *> $buildLog
        $buildExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    Get-Content -LiteralPath $buildLog
    if ($buildExitCode -ne 0) {
        throw "Disposable harness build failed with exit code $buildExitCode."
    }

    $outputPlugin = Join-Path $miniHarnessDirectory 'bin/Release/netstandard2.1/luau_host.dll'
    if (-not (Test-Path -LiteralPath $outputPlugin -PathType Leaf)) {
        throw "The disposable harness build did not produce its native output: $outputPlugin"
    }

    $outputHash = (Get-FileHash -LiteralPath $outputPlugin -Algorithm SHA256).Hash
    if ($outputHash -ne $packageHash) {
        throw "Harness output did not select the checked package plugin. Expected SHA256 $packageHash; actual $outputHash."
    }

    if ($outputHash -eq $ignoredHash) {
        throw "Harness output incorrectly selected the ignored native build sentinel (SHA256=$ignoredHash)."
    }

    Write-Host "Managed harness native selection passed: $outputPlugin (SHA256=$outputHash)."
    $succeeded = $true
}
catch {
    Write-Host "Managed harness native selection failed. Diagnostics retained at: $scratchRoot"
    throw
}
finally {
    if ($succeeded) {
        $resolvedScratchRoot = [System.IO.Path]::GetFullPath($scratchRoot)
        $expectedPrefix = $tempRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $resolvedScratchRoot.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not (Split-Path -Leaf $resolvedScratchRoot).StartsWith('luau-managed-harness-selection-', [System.StringComparison]::Ordinal)) {
            throw "Refusing to remove unexpected disposable harness path: $resolvedScratchRoot"
        }

        Remove-Item -LiteralPath $resolvedScratchRoot -Recurse -Force
    }
}
