param(
    [string] $OutputPath = ""
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$pluginPath = Join-Path $projectRoot "oxide\plugins\AdminSuite.cs"
$readmePath = Join-Path $projectRoot "README.txt"
$configPath = Join-Path $projectRoot "Config\AdminSuite.example.json"

if (-not (Test-Path $pluginPath)) {
    throw "Plugin not found: $pluginPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputDirectory = Split-Path -Parent $projectRoot
    $OutputPath = Join-Path $outputDirectory "AdminSuite.zip"
}

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("AdminSuite_package_" + [Guid]::NewGuid().ToString("N"))
$stagingProject = Join-Path $staging "AdminSuite"
$stagingPlugins = Join-Path $stagingProject "oxide\plugins"
$stagingConfig = Join-Path $stagingProject "Config"
$stagingDocs = Join-Path $stagingProject "docs"

New-Item -ItemType Directory -Force -Path $stagingPlugins | Out-Null
New-Item -ItemType Directory -Force -Path $stagingConfig | Out-Null
New-Item -ItemType Directory -Force -Path $stagingDocs | Out-Null

Copy-Item $pluginPath (Join-Path $stagingPlugins "AdminSuite.cs") -Force
Copy-Item $readmePath (Join-Path $stagingProject "README.txt") -Force
Copy-Item $configPath (Join-Path $stagingConfig "AdminSuite.example.json") -Force
Copy-Item (Join-Path $projectRoot "docs\QUICKSTART.md") (Join-Path $stagingDocs "QUICKSTART.md") -Force

if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Force
}

Compress-Archive -Path $stagingProject -DestinationPath $OutputPath -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force

Write-Host "Created $OutputPath"
