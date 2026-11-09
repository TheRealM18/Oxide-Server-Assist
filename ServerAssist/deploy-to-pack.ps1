param(
    [string]$Destination = "C:\Users\robert\Desktop\Server Mods\Mods\AAY_ServerAssistMod"
)

$ErrorActionPreference = "Stop"

$source = Split-Path -Parent $MyInvocation.MyCommand.Path
$files = @(
    "ServerAssistMod.dll",
    "ModInfo.xml",
    "Config\XUi_InGame\xui.xml",
    "Config\XUi_InGame\windows.xml",
    "Config\XUi_Common\styles.xml"
)

foreach ($relative in $files) {
    $from = Join-Path $source $relative
    if (-not (Test-Path $from)) {
        throw "Missing build file: $from"
    }
}

foreach ($relative in $files) {
    $from = Join-Path $source $relative
    $to = Join-Path $Destination $relative
    $parent = Split-Path -Parent $to
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    Copy-Item $from $to -Force
}

foreach ($relative in $files) {
    $from = Join-Path $source $relative
    $to = Join-Path $Destination $relative
    $sourceHash = (Get-FileHash $from).Hash
    $destHash = (Get-FileHash $to).Hash
    if ($sourceHash -ne $destHash) {
        throw "Hash mismatch after copy: $relative"
    }
}

Write-Host ""
Write-Host "Server Assist client mod updated in:"
Write-Host "  $Destination"
Write-Host ""
Write-Host "Files:"
$files | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Write-Host "The AAZ_ServerAssistArt folder is intentionally untouched."
Write-Host "Hand this folder to the QLN pack agent to package, upload, and update the manifest."
