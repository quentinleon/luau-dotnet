param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [switch] $Check
)

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$packageRoot = Join-Path $root "Luau.Unity"
$runtimeDir = Join-Path $packageRoot "Runtime"

$projects = @(
    "src/Luau/Luau.csproj",
    "src/Luau.SourceGenerator/Luau.SourceGenerator.csproj"
)

foreach ($project in $projects) {
    $projectPath = Join-Path $root $project
    & dotnet build $projectPath --configuration $Configuration --nologo --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build managed Unity artifact project: $project"
    }
}

$artifacts = @(
    @{
        Source = "src/Luau/bin/$Configuration/netstandard2.1/Luau.dll"
        FileName = "Luau.dll"
    },
    @{
        Source = "src/Luau.SourceGenerator/bin/$Configuration/netstandard2.0/Luau.SourceGenerator.dll"
        FileName = "Luau.SourceGenerator.dll"
    }
)

foreach ($artifact in $artifacts) {
    $source = Join-Path $root $artifact.Source
    $destination = Join-Path $runtimeDir $artifact.FileName

    if (!(Test-Path -LiteralPath $source)) {
        throw "Missing artifact: $source. Build the relevant .NET project first."
    }

    if ($Check) {
        if (!(Test-Path -LiteralPath $destination -PathType Leaf)) {
            throw "Missing Unity artifact: $destination. Refresh the managed package artifacts."
        }

        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($sourceHash -ne $destinationHash) {
            throw "Stale Unity artifact: $destination does not match $source."
        }

        Write-Host "Current: $destination (SHA256=$destinationHash)"
    }
    else {
        Copy-Item -LiteralPath $source -Destination $destination -Force
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($sourceHash -ne $destinationHash) {
            throw "Copied Unity artifact failed SHA256 verification: $destination"
        }

        Write-Host "Copied $source -> $destination (SHA256=$destinationHash)"
    }
}

if ($Check) {
    Write-Host "Unity managed artifacts are current."
}
else {
    Write-Host "Unity package artifacts updated in $runtimeDir."
}
Write-Host "Native luau_host plugins are built and installed separately through native/luau-host CMake presets."
