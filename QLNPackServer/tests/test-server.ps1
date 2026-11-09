param(
    [string]$ServerExe = ".\dist\QLNPackServer\QLNPackServer.exe",
    [int]$Port = 18765,
    [string]$Token = "test-token"
)

$ErrorActionPreference = 'Stop'

function Fail-Test {
    param([string]$Message)
    Write-Host "FAIL: $Message" -ForegroundColor Red
    throw $Message
}

function Pass-Test {
    param([string]$Message)
    Write-Host "PASS: $Message" -ForegroundColor Green
}

$exe = (Resolve-Path $ServerExe).Path
$root = Join-Path $env:TEMP ("QLNPackServerTest-" + [guid]::NewGuid().ToString("N"))
$mods = Join-Path $root "Mods"
$mod = Join-Path $mods "TestMod"
$cache = Join-Path $root "cache"
$configPath = Join-Path $root "QLNPackServer.json"
$stdout = Join-Path $root "stdout.txt"
$stderr = Join-Path $root "stderr.txt"
$process = $null

try {
    New-Item -ItemType Directory -Force $mod | Out-Null
    Set-Content -Path (Join-Path $mod "ModInfo.xml") -Encoding UTF8 -Value @'
<?xml version="1.0" encoding="utf-8"?>
<xml>
  <Name value="TestMod" />
  <DisplayName value="Test Mod" />
  <Version value="1.2.3" />
</xml>
'@
    Set-Content -Path (Join-Path $mod "hello.txt") -Encoding UTF8 -Value "hello"

    $config = [ordered]@{
        bindAddress = "127.0.0.1"
        port = $Port
        publicBaseUrl = ""
        accessToken = $Token
        modsPath = $mods
        cacheDir = $cache
        include = @("TestMod")
        exclude = @()
        packVersion = "auto"
        gameVersion = "3.4"
        notes = "test"
        remove = @()
        signingKeyXml = ""
        refreshSeconds = 30
        logFile = (Join-Path $root "server.log")
    }

    [IO.File]::WriteAllText(
        $configPath,
        ($config | ConvertTo-Json -Depth 6),
        (New-Object System.Text.UTF8Encoding($false)))

    $process = Start-Process -FilePath $exe -ArgumentList @("--config", $configPath) `
        -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr

    $healthy = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 250
        try {
            $health = Invoke-WebRequest "http://127.0.0.1:$Port/health" -UseBasicParsing -TimeoutSec 2
            if ($health.StatusCode -eq 200) {
                $healthy = $true
                break
            }
        }
        catch {
            # Not up yet.
        }
    }

    if (-not $healthy) {
        Fail-Test "Server did not answer /health"
    }

    Pass-Test "health endpoint"

    $manifest = Invoke-RestMethod "http://127.0.0.1:$Port/manifest.json?token=$Token" -TimeoutSec 5
    if ($manifest.mods.Count -ne 1) {
        Fail-Test "Expected 1 mod, got $($manifest.mods.Count)"
    }

    if ($manifest.mods[0].sha256 -notmatch '^[0-9a-f]{64}$') {
        Fail-Test "Manifest sha256 is missing or invalid"
    }

    Pass-Test "manifest with token"

    $zipPath = Join-Path $root "downloaded.zip"
    Invoke-WebRequest "$($manifest.mods[0].url)?token=$Token" -OutFile $zipPath -TimeoutSec 15

    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $manifest.mods[0].sha256) {
        Fail-Test "Downloaded zip hash does not match manifest"
    }

    Pass-Test "zip download and hash"

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName })
        if ($names -notcontains "ModInfo.xml") {
            Fail-Test "Zip does not contain ModInfo.xml at its root"
        }
    }
    finally {
        $zip.Dispose()
    }

    Pass-Test "zip layout"

    $blocked = $false
    try {
        Invoke-WebRequest "http://127.0.0.1:$Port/manifest.json" -UseBasicParsing -TimeoutSec 5 | Out-Null
    }
    catch {
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode.value__ -eq 403) {
            $blocked = $true
        }
    }

    if (-not $blocked) {
        Fail-Test "Manifest without token was not rejected"
    }

    Pass-Test "token required"

    Write-Host ""
    Write-Host "ALL SERVER TESTS PASSED" -ForegroundColor Green
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Milliseconds 250

    try {
        Remove-Item -Recurse -Force $root -ErrorAction Stop
    }
    catch {
        Write-Host "Test folder left behind: $root"
    }
}
