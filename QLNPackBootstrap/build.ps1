param(
    [string]$GameDir = ""
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    if (Test-Path .\dist) {
        Remove-Item -Recurse -Force .\dist
    }

    $gameArg = ""
    if (-not [string]::IsNullOrWhiteSpace($GameDir)) {
        $gameArg = "-p:GameDir=$GameDir"
    }

    dotnet build .\QLNPackBootstrap.csproj -c Release $gameArg
    if ($LASTEXITCODE -ne 0) { throw "Bootstrap build failed." }

    dotnet build .\updater\QLNPackUpdater.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw "Updater build failed." }

    # A class library build can leave behind files the game does not need.
    Remove-Item .\dist\000_QLNPackBootstrap\*.deps.json -ErrorAction SilentlyContinue
    Remove-Item .\dist\000_QLNPackBootstrap\*.pdb -ErrorAction SilentlyContinue
    Remove-Item .\dist\000_QLNPackBootstrap\QLNPackUpdater.exe.config -ErrorAction SilentlyContinue

    $zipPath = Join-Path $root 'dist\000_QLNPackBootstrap.zip'
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        (Join-Path $root 'dist\000_QLNPackBootstrap'),
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    Write-Host ""
    Write-Host "Built mod folder:"
    Get-ChildItem -Recurse .\dist\000_QLNPackBootstrap | ForEach-Object {
        Write-Host ("  " + $_.FullName.Substring($root.Length + 1))
    }
    Write-Host ""
    Write-Host "Distribution zip: $zipPath"
}
finally {
    Pop-Location
}
