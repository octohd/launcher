param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string] $Runtime,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $Version,

    [string] $UpdateRepository = $env:GITHUB_REPOSITORY,

    [switch] $NativeAot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src/OctoHD.App/OctoHD.App.csproj'
$outputPath = Join-Path $repositoryRoot "releases/$Runtime"
$publishAot = if ($NativeAot) { 'true' } else { 'false' }

if ($env:GITHUB_ACTIONS -eq 'true' -and [string]::IsNullOrWhiteSpace($UpdateRepository)) {
    throw 'GITHUB_REPOSITORY is required in CI so released builds can locate their update channel.'
}

$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'releases'))
$resolvedOutput = [System.IO.Path]::GetFullPath($outputPath)
if (-not $resolvedOutput.StartsWith($releaseRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean publish output outside $releaseRoot."
}

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

$publishProperties = @(
    "-p:PublishAot=$publishAot",
    '-p:IncludeSourceRevisionInInformationalVersion=false',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugSymbols=false',
    '-p:DebugType=None'
)
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $publishProperties += "-p:Version=$Version"
}
if (-not [string]::IsNullOrWhiteSpace($UpdateRepository)) {
    $publishProperties += "-p:OctoHDUpdateRepository=$UpdateRepository"
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    @publishProperties `
    --output $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "Publish for $Runtime failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $resolvedOutput -File -Filter '*.pdb' | Remove-Item -Force

$expectedExecutableName = if ($Runtime.StartsWith('win-', [System.StringComparison]::OrdinalIgnoreCase)) {
    'OctoHD.exe'
} else {
    'OctoHD'
}
$publishedFiles = @(Get-ChildItem -LiteralPath $resolvedOutput -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne $expectedExecutableName) {
    $publishedNames = $publishedFiles.Name -join ', '
    throw "Single-file publish validation failed for $Runtime. Found: $publishedNames"
}

$sizeMegabytes = $publishedFiles[0].Length / 1MB
Write-Host "OctoHD single executable published to $($publishedFiles[0].FullName) ($($sizeMegabytes.ToString('N1')) MB)"
