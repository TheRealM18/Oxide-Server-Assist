param(
    [Parameter(Mandatory = $true)]
    [string]$LinksFile,

    [Parameter(Mandatory = $true)]
    [string]$ItemsFile,

    [string]$OutputFile = "",

    [string]$ReportFile = "",

    [switch]$NoScrape,

    [int]$MinScore = 55
)

$ErrorActionPreference = 'Stop'

function Normalize-Name {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $value = [IO.Path]::GetFileNameWithoutExtension($Value)
    $value = $value.ToLowerInvariant()
    $value = $value -replace '\bv?\d+(?:[._-]\d+){1,3}\b', ' '
    $value = $value -replace '[^a-z0-9]+', ' '
    $value = $value -replace '\b(client|server|mod|modlet|pack|update|zip)\b', ' '
    $value = $value -replace '\s+', ' '
    return $value.Trim()
}

function Compact-Name {
    param([string]$Value)
    return ($Value -replace '\s+', '')
}

function Score-Match {
    param([string]$Hint, [string]$Candidate)

    $hintNorm = Normalize-Name $Hint
    $candNorm = Normalize-Name $Candidate

    if ([string]::IsNullOrEmpty($hintNorm) -or [string]::IsNullOrEmpty($candNorm)) {
        return 0
    }

    if ($hintNorm -eq $candNorm) {
        return 100
    }

    if ((Compact-Name $hintNorm) -eq (Compact-Name $candNorm)) {
        return 98
    }

    if ($hintNorm.Contains($candNorm) -or $candNorm.Contains($hintNorm)) {
        $short = [Math]::Min($hintNorm.Length, $candNorm.Length)
        $long = [Math]::Max($hintNorm.Length, $candNorm.Length)
        return 80 + [Math]::Min(10, [int](10.0 * $short / $long))
    }

    $hintTokens = @($hintNorm.Split(' ') | Where-Object { $_.Length -gt 0 })
    $candTokens = @($candNorm.Split(' ') | Where-Object { $_.Length -gt 0 })

    if ($hintTokens.Count -eq 0 -or $candTokens.Count -eq 0) {
        return 0
    }

    $common = @($hintTokens | Where-Object { $candTokens -contains $_ }).Count
    $union = @($hintTokens + $candTokens | Select-Object -Unique).Count

    if ($union -eq 0) {
        return 0
    }

    $score = 50 + [int](40.0 * $common / $union)
    return $score
}

function Get-DriveTitle {
    param([string]$Url)

    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -MaximumRedirection 5 -TimeoutSec 20
        $html = [string]$response.Content
        $match = [regex]::Match($html, '<title[^>]*>(.*?)</title>', 'IgnoreCase,Singleline')

        if (-not $match.Success) {
            return ""
        }

        $title = [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
        $title = $title -replace '\s*-\s*Google Drive\s*$', ''
        return $title.Trim()
    }
    catch {
        return ""
    }
}

$itemsPath = (Resolve-Path $ItemsFile).Path
$items = Get-Content -Path $itemsPath -Raw | ConvertFrom-Json
$items = @($items)

if ($items.Count -eq 0) {
    throw "Items file is empty: $itemsPath"
}

$outputPath = if ([string]::IsNullOrWhiteSpace($OutputFile)) {
    $itemsPath
}
else {
    [IO.Path]::GetFullPath($OutputFile)
}

$reportPath = if ([string]::IsNullOrWhiteSpace($ReportFile)) {
    Join-Path (Split-Path -Parent $outputPath) 'drive-match-report.txt'
}
else {
    [IO.Path]::GetFullPath($ReportFile)
}

$rawLinks = Get-Content -Path (Resolve-Path $LinksFile).Path -Raw
$trimmed = $rawLinks.TrimStart()
$linkEntries = @()

if ($trimmed.StartsWith('[') -or $trimmed.StartsWith('{')) {
    $parsed = $rawLinks | ConvertFrom-Json

    if ($parsed -is [System.Array]) {
        foreach ($entry in $parsed) {
            if ($null -eq $entry) { continue }

            $linkEntries += [pscustomobject]@{
                name = [string]$entry.name
                file = [string]$entry.file
                installFolder = [string]$entry.installFolder
                url = [string]$entry.url
            }
        }
    }
    else {
        foreach ($property in $parsed.PSObject.Properties) {
            $linkEntries += [pscustomobject]@{
                name = $property.Name
                file = ""
                installFolder = ""
                url = [string]$property.Value
            }
        }
    }
}
elseif ($rawLinks -match ',') {
    $rows = Import-Csv -Path (Resolve-Path $LinksFile).Path

    foreach ($row in $rows) {
        $url = [string]$row.url
        if ([string]::IsNullOrWhiteSpace($url)) { $url = [string]$row.link }

        $file = [string]$row.file
        if ([string]::IsNullOrWhiteSpace($file)) { $file = [string]$row.filename }

        $name = [string]$row.name
        if ([string]::IsNullOrWhiteSpace($name)) { $name = [string]$row.mod }

        $linkEntries += [pscustomobject]@{
            name = $name
            file = $file
            installFolder = [string]$row.installFolder
            url = $url
        }
    }
}
else {
    foreach ($line in ($rawLinks -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        $linkEntries += [pscustomobject]@{
            name = ""
            file = ""
            installFolder = ""
            url = $line.Trim()
        }
    }
}

$report = New-Object System.Collections.Generic.List[string]
$matchedUrls = @{}
$usedItems = @{}

foreach ($link in $linkEntries) {
    if ([string]::IsNullOrWhiteSpace($link.url)) {
        $report.Add("UNMATCHED LINK: missing url")
        continue
    }

    $hint = ""
    if (-not [string]::IsNullOrWhiteSpace($link.file)) { $hint = $link.file }
    elseif (-not [string]::IsNullOrWhiteSpace($link.name)) { $hint = $link.name }
    elseif (-not [string]::IsNullOrWhiteSpace($link.installFolder)) { $hint = $link.installFolder }

    if ([string]::IsNullOrWhiteSpace($hint) -and -not $NoScrape -and
        $link.url -match 'drive\.google\.com|dropbox\.com') {
        $hint = Get-DriveTitle $link.url
        Start-Sleep -Milliseconds 250
    }

    if ([string]::IsNullOrWhiteSpace($hint)) {
        $report.Add("UNMATCHED LINK (no filename hint): $($link.url)")
        continue
    }

    $scored = @()

    for ($index = 0; $index -lt $items.Count; $index++) {
        $item = $items[$index]
        $candidates = @(
            [string]$item.name,
            [string]$item.installFolder,
            [string]$item.displayName,
            [IO.Path]::GetFileNameWithoutExtension([string]$item.file)
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

        $best = 0
        foreach ($candidate in $candidates) {
            $score = Score-Match -Hint $hint -Candidate $candidate
            if ($score -gt $best) { $best = $score }
        }

        if ($best -gt 0) {
            $scored += [pscustomobject]@{
                Index = $index
                Score = $best
                Item = $item
            }
        }
    }

    $scored = @($scored | Sort-Object Score -Descending)

    if ($scored.Count -eq 0 -or $scored[0].Score -lt $MinScore) {
        $report.Add("UNMATCHED LINK: '$hint' -> $($link.url) (best score: " +
            $(if ($scored.Count -gt 0) { $scored[0].Score } else { 0 }) + ")")
        continue
    }

    if ($scored.Count -gt 1 -and ($scored[0].Score - $scored[1].Score) -lt 5) {
        $report.Add("AMBIGUOUS LINK: '$hint' -> $($link.url) (scores: " +
            $scored[0].Score + ", " + $scored[1].Score + ")")
        continue
    }

    $chosen = $scored[0]
    $items[$chosen.Index].url = $link.url
    $matchedUrls[$chosen.Index] = $link.url
    $usedItems[$chosen.Index] = $true

    $report.Add("MATCHED: '$hint' -> $($chosen.Item.name) (score " +
        $chosen.Score + "): $($link.url)")
}

$unmatchedItems = @()
for ($index = 0; $index -lt $items.Count; $index++) {
    if (-not $usedItems.ContainsKey($index) -or
        [string]::IsNullOrWhiteSpace([string]$items[$index].url) -or
        ([string]$items[$index].url) -match 'REPLACE_') {
        $unmatchedItems += $items[$index]
    }
}

foreach ($item in $unmatchedItems) {
    $report.Add("UNMATCHED ITEM: $($item.name) ($($item.installFolder))")
}

if ($outputPath -eq $itemsPath -and (Test-Path $itemsPath)) {
    Copy-Item -Path $itemsPath -Destination ($itemsPath + '.bak') -Force
}

$json = ConvertTo-Json -InputObject @($items) -Depth 8
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($outputPath, $json, $utf8NoBom)
[IO.File]::WriteAllLines($reportPath, $report, $utf8NoBom)

Write-Host ""
Write-Host "Wrote:  $outputPath"
Write-Host "Report: $reportPath"
Write-Host ""
Write-Host "Matched: $($matchedUrls.Count) / $($items.Count)"
Write-Host "Review the report for unmatched or ambiguous links."
