param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "0.1.0",

    [Parameter(Mandatory = $false)]
    [string]$SourceDir = "",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appName = "PowerGauge"
$defaultSourceDir = Join-Path $repoRoot "artifacts/bin/PowerGauge/Debug/net10.0"
$resolvedSourceDir = if ([string]::IsNullOrWhiteSpace($SourceDir)) { $defaultSourceDir } else { $SourceDir }
$resolvedOutputDir = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    Join-Path $repoRoot "artifacts/package/macos/$appName.app"
}
else {
    $OutputDir
}

$infoPlistTemplate = Join-Path $repoRoot "src/PowerGauge/Properties/MacOS/Info.plist"
$contentsDir = Join-Path $resolvedOutputDir "Contents"
$macOsDir = Join-Path $contentsDir "MacOS"
$resourcesDir = Join-Path $contentsDir "Resources"
$executablePath = Join-Path $resolvedSourceDir $appName
$infoPlistPath = Join-Path $contentsDir "Info.plist"

if (-not (Test-Path $resolvedSourceDir)) {
    throw "Source directory not found: $resolvedSourceDir"
}

if (-not (Test-Path $executablePath)) {
    throw "Executable not found at $executablePath. Build or publish the app first."
}

if (-not (Test-Path $infoPlistTemplate)) {
    throw "Info.plist template not found: $infoPlistTemplate"
}

if (Test-Path $resolvedOutputDir) {
    Remove-Item $resolvedOutputDir -Recurse -Force
}

New-Item -ItemType Directory -Path $macOsDir -Force | Out-Null
New-Item -ItemType Directory -Path $resourcesDir -Force | Out-Null

Write-Host "Copying app files from $resolvedSourceDir..."
$sourceFiles = Get-ChildItem -Path $resolvedSourceDir -Recurse -File
foreach ($file in $sourceFiles) {
    if ($file.Extension -eq ".pdb") {
        continue
    }

    $relativePath = [System.IO.Path]::GetRelativePath($resolvedSourceDir, $file.FullName)
    $destinationPath = Join-Path $macOsDir $relativePath
    $destinationDirectory = Split-Path -Parent $destinationPath

    if (-not (Test-Path $destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }

    Copy-Item $file.FullName -Destination $destinationPath
}

$numericVersionMatch = [System.Text.RegularExpressions.Regex]::Match($Version, '^\d+(?:\.\d+){0,3}')
if (-not $numericVersionMatch.Success) {
    throw "Version '$Version' must start with a numeric version like 0.1.0"
}

$buildVersion = $numericVersionMatch.Value
$infoPlist = Get-Content -Path $infoPlistTemplate -Raw
$infoPlist = $infoPlist.Replace("__VERSION__", $Version)
$infoPlist = $infoPlist.Replace("__BUILD_VERSION__", $buildVersion)
[System.IO.File]::WriteAllText($infoPlistPath, $infoPlist, [System.Text.Encoding]::UTF8)

if ($IsMacOS) {
    & chmod +x (Join-Path $macOsDir $appName)
    if ($LASTEXITCODE -ne 0) {
        throw "chmod failed with exit code $LASTEXITCODE"
    }
}

Write-Host "Created macOS app bundle at $resolvedOutputDir"
