param(
    [Parameter(Mandatory = $true)][string]$Sheet,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [Parameter(Mandatory = $true)][string[]]$Names
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if ($Names.Count -ne 4) {
    throw "Expected exactly 4 output names (2x2 sheet)."
}

$source = [System.Drawing.Bitmap]::FromFile($Sheet)
try {
    $halfW = [int][Math]::Floor($source.Width / 2)
    $halfH = [int][Math]::Floor($source.Height / 2)
    $rects = @(
        (New-Object System.Drawing.Rectangle(0, 0, $halfW, $halfH)),
        (New-Object System.Drawing.Rectangle($halfW, 0, ($source.Width - $halfW), $halfH)),
        (New-Object System.Drawing.Rectangle(0, $halfH, $halfW, ($source.Height - $halfH))),
        (New-Object System.Drawing.Rectangle($halfW, $halfH, ($source.Width - $halfW), ($source.Height - $halfH)))
    )

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

    for ($i = 0; $i -lt 4; $i++) {
        $crop = $source.Clone($rects[$i], $source.PixelFormat)
        try {
            $outPath = Join-Path $OutDir ($Names[$i] + '.png')
            $crop.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Host "Wrote $outPath"
        }
        finally {
            $crop.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}
