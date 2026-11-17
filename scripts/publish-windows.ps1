[CmdletBinding()]
param([switch]$SkipTests)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $repoRoot "VERSION"
$projectFile = Join-Path $repoRoot "src/FirebirdStudio.Desktop/FirebirdStudio.Desktop.csproj"
$releaseDirectory = Join-Path $repoRoot "releases"
$stagingRoot = Join-Path $releaseDirectory ".staging"
$runtimeIdentifier = "win-x64"
$targetFramework = "net10.0"

if (-not (Test-Path $versionFile)) { throw "Version file not found: $versionFile" }
$version = (Get-Content -Path $versionFile -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "VERSION must contain a valid SemVer version without a leading 'v'. Found: '$version'"
}
if (-not (Test-Path $projectFile)) { throw "Desktop project not found: $projectFile" }

$null = New-Item -ItemType Directory -Path $releaseDirectory -Force
$null = New-Item -ItemType Directory -Path $stagingRoot -Force

$frameworkPublish = Join-Path $stagingRoot "framework-dependent"
$selfContainedPublish = Join-Path $stagingRoot "self-contained"
$frameworkZipName = "FirebirdStudio-$version-$runtimeIdentifier-framework-dependent.zip"
$selfContainedZipName = "FirebirdStudio-$version-$runtimeIdentifier-self-contained.zip"
$frameworkZip = Join-Path $releaseDirectory $frameworkZipName
$selfContainedZip = Join-Path $releaseDirectory $selfContainedZipName

Write-Host "Firebird Studio $version — Windows $runtimeIdentifier" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host "Running solution tests..."
    dotnet test (Join-Path $repoRoot "FirebirdStudio.sln") -c Release "-p:Version=$version"
    if ($LASTEXITCODE -ne 0) { throw "Tests failed. No release packages were generated." }
}

# Remove only this script's temporary publish directories. Existing release files are preserved.
foreach ($path in @($frameworkPublish, $selfContainedPublish)) {
    if (Test-Path $path) { Remove-Item -Path $path -Recurse -Force }
}
foreach ($path in @($frameworkZip, $selfContainedZip)) {
    if (Test-Path $path) { Remove-Item -Path $path -Force }
}

Write-Host "Publishing framework-dependent single-file package..."
$frameworkArgs = @("publish", $projectFile, "-c", "Release", "-f", $targetFramework, "-r", $runtimeIdentifier, "--self-contained", "false", "-p:PublishSingleFile=true", "-p:DebugType=None", "-p:DebugSymbols=false", "-p:Version=$version", "-o", $frameworkPublish)
& dotnet @frameworkArgs
if ($LASTEXITCODE -ne 0) { throw "Framework-dependent publish failed." }

Write-Host "Publishing self-contained folder package..."
$selfContainedArgs = @("publish", $projectFile, "-c", "Release", "-f", $targetFramework, "-r", $runtimeIdentifier, "--self-contained", "true", "-p:PublishSingleFile=false", "-p:DebugType=None", "-p:DebugSymbols=false", "-p:Version=$version", "-o", $selfContainedPublish)
& dotnet @selfContainedArgs
if ($LASTEXITCODE -ne 0) { throw "Self-contained publish failed." }

foreach ($path in @($frameworkPublish, $selfContainedPublish)) {
    if (-not (Test-Path (Join-Path $path "FirebirdStudio.Desktop.exe"))) {
        throw "Expected application executable was not produced in: $path"
    }
}

Write-Host "Creating portable ZIP archives..."
Compress-Archive -Path (Join-Path $frameworkPublish "*") -DestinationPath $frameworkZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $selfContainedPublish "*") -DestinationPath $selfContainedZip -CompressionLevel Optimal

$artifacts = @()
foreach ($zipPath in @($frameworkZip, $selfContainedZip)) {
    $file = Get-Item -LiteralPath $zipPath
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $mode = if ($file.Name -like "*framework-dependent*") { "framework-dependent" } else { "self-contained" }
    $artifacts += [ordered]@{
        fileName = $file.Name
        operatingSystem = "windows"
        architecture = "x64"
        runtimeIdentifier = $runtimeIdentifier
        targetFramework = $targetFramework
        publishMode = $mode
        requiresDotNetRuntime = ($mode -eq "framework-dependent")
        sha256 = $hash
        sizeBytes = $file.Length
    }
}

$checksumLines = foreach ($artifact in $artifacts) { "$($artifact.sha256)  $($artifact.fileName)" }
$checksumPath = Join-Path $releaseDirectory "checksums.txt"
[System.IO.File]::WriteAllLines($checksumPath, [string[]]$checksumLines, [System.Text.UTF8Encoding]::new($false))

$manifest = [ordered]@{
    schemaVersion = 1
    product = "FirebirdStudio"
    version = $version
    releaseChannel = "stable"
    publishedAtUtc = [DateTime]::UtcNow.ToString("o")
    artifacts = $artifacts
}
$manifestPath = Join-Path $releaseDirectory "release-manifest.json"
$manifestJson = $manifest | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($manifestPath, $manifestJson + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host "Windows release packages generated successfully." -ForegroundColor Green
Write-Host "  $frameworkZip"
Write-Host "  $selfContainedZip"
Write-Host "  $checksumPath"
Write-Host "  $manifestPath"
Write-Host ""
Write-Host "Framework-dependent package requires the .NET 10 Desktop Runtime."
Write-Host "Self-contained package includes the .NET runtime."
