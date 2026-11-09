$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    if (Test-Path .\dist) {
        Remove-Item -Recurse -Force .\dist
    }

    dotnet build .\QLNPackServer.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw "Server build failed." }

    Remove-Item .\dist\QLNPackServer\*.deps.json -ErrorAction SilentlyContinue
    Remove-Item .\dist\QLNPackServer\*.pdb -ErrorAction SilentlyContinue
    Remove-Item .\dist\QLNPackServer\*.exe.config -ErrorAction SilentlyContinue

    Write-Host ""
    Write-Host "Built server:"
    Get-ChildItem -Recurse .\dist\QLNPackServer | ForEach-Object {
        Write-Host ("  " + $_.FullName.Substring($root.Length + 1))
    }
}
finally {
    Pop-Location
}
