param(
    [string]$Runtime = "linux-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "MediaRelay.csproj"

$arguments = @(
    "publish",
    $project,
    "-c", "Release",
    "-r", $Runtime,
    "--nologo"
)

if (-not $FrameworkDependent) {
    $arguments += @(
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true"
    )
}

& dotnet @arguments

$publishDir = Join-Path $root "bin\Release\net8.0\$Runtime\publish"
Copy-Item (Join-Path $root "relay.example.json") (Join-Path $publishDir "relay.example.json") -Force

$binary = if ($Runtime.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) { "MediaRelay.exe" } else { "MediaRelay" }

Write-Host ""
Write-Host "Published to: $publishDir"
Write-Host "Run $binary once to generate relay.json, then edit it and restart."
if (-not $Runtime.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
    Write-Host "On Linux: chmod +x MediaRelay, and chmod 600 relay.json."
}
