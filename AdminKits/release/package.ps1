param(
    [string] $OutputPath = ""
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$pluginPath = Join-Path $projectRoot "oxide\plugins\AdminKits.cs"
$readmePath = Join-Path $projectRoot "README.txt"
$iconsPath = Join-Path $projectRoot "ICONS.md"
$configPath = Join-Path $projectRoot "Config\AdminKits.example.json"

if (-not (Test-Path $pluginPath)) {
    throw "Plugin not found: $pluginPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputDirectory = Split-Path -Parent $projectRoot
    $OutputPath = Join-Path $outputDirectory "AdminKits.zip"
}

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("AdminKits_package_" + [Guid]::NewGuid().ToString("N"))
$stagingProject = Join-Path $staging "AdminKits"
$stagingPlugins = Join-Path $stagingProject "oxide\plugins"
$stagingConfig = Join-Path $stagingProject "Config"

New-Item -ItemType Directory -Force -Path $stagingPlugins | Out-Null
New-Item -ItemType Directory -Force -Path $stagingConfig | Out-Null

Copy-Item $pluginPath (Join-Path $stagingPlugins "AdminKits.cs") -Force
Copy-Item $readmePath (Join-Path $stagingProject "README.txt") -Force
Copy-Item $iconsPath (Join-Path $stagingProject "ICONS.md") -Force
Copy-Item $configPath (Join-Path $stagingConfig "AdminKits.example.json") -Force

if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Force
}

Compress-Archive -Path $stagingProject -DestinationPath $OutputPath -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force

Write-Host "Created $OutputPath"
