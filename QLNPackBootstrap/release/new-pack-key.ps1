param(
    [string]$OutputDir = ".\keys",
    [int]$KeySize = 3072
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force $OutputDir | Out-Null

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider($KeySize)
try {
    $rsa.PersistKeyInCsp = $false

    $privatePath = Join-Path $OutputDir 'private-key.xml'
    $publicPath = Join-Path $OutputDir 'public-key.xml'

    [IO.File]::WriteAllText($privatePath, $rsa.ToXmlString($true))
    [IO.File]::WriteAllText($publicPath, $rsa.ToXmlString($false))

    Write-Host "Private key: $privatePath"
    Write-Host "Public key:  $publicPath"
    Write-Host ""
    Write-Host "Keep private-key.xml out of the repository. Paste public-key.xml"
    Write-Host "into packbootstrap.xml and set RequireSignature to true."
}
finally {
    $rsa.Dispose()
}
