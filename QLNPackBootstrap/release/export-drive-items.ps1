param(
    [string]$PackDir = "",

    [string]$ZipDir = "",

    [switch]$FromZips,

    [string]$ItemsFile = ".\google-drive-items.json",

    [string]$UrlTemplate = "https://drive.google.com/file/d/REPLACE_{0}_ID/view?usp=sharing",

    [string]$UrlMap = "",

    [switch]$UseExistingZips,

    [switch]$Force,

    [switch]$NoReuseUrls
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-ModInfoValue {
    param($Xml, [string]$Name, [string]$Fallback)

    $node = $Xml.SelectSingleNode("/xml/$Name")
    if ($null -eq $node) {
        $node = $Xml.SelectSingleNode("/xml/ModInfo/$Name")
    }

    if ($null -eq $node) {
        return $Fallback
    }

    $value = $node.GetAttribute("value")
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Fallback
    }

    return $value.Trim()
}

function ConvertTo-SafeFolder {
    param([string]$Name)

    $safe = [regex]::Replace($Name, '[^0-9A-Za-z_\-]', '_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        throw "Could not derive a safe folder name from '$Name'."
    }

    return $safe
}

function Get-RelativePath {
    param([string]$BaseDir, [string]$Path)

    $base = [IO.Path]::GetFullPath($BaseDir)
    if (-not $base.EndsWith('\')) {
        $base += '\'
    }

    $fullPath = [IO.Path]::GetFullPath($Path)

    try {
        $baseUri = New-Object System.Uri($base)
        $pathUri = New-Object System.Uri($fullPath)
        $relative = [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString())
        $relative = $relative.Replace('/', '\')

        if ($relative -match '^[A-Za-z]:' -or $relative.StartsWith('file:')) {
            return $fullPath
        }

        return $relative
    }
    catch {
        return $fullPath
    }
}

function Test-RealUrl {
    param([string]$Value)

    return -not [string]::IsNullOrWhiteSpace($Value) -and $Value -notmatch 'REPLACE_'
}

$packDirPath = ""
if (-not [string]::IsNullOrWhiteSpace($PackDir)) {
    $packDirPath = (Resolve-Path $PackDir).Path
}

if (-not $FromZips -and [string]::IsNullOrWhiteSpace($packDirPath)) {
    throw "Provide -PackDir (mod folders) or use -FromZips -ZipDir <zip folder>."
}

$itemsPath = [IO.Path]::GetFullPath($ItemsFile)
$itemsDir = Split-Path -Parent $itemsPath

if (-not (Test-Path $itemsDir)) {
    New-Item -ItemType Directory -Force $itemsDir | Out-Null
}

if ([string]::IsNullOrWhiteSpace($ZipDir)) {
    $zipDirPath = Join-Path $itemsDir 'zips'
}
else {
    $zipDirPath = [IO.Path]::GetFullPath($ZipDir)
}

New-Item -ItemType Directory -Force $zipDirPath | Out-Null

$existingUrls = @{}

if (-not $NoReuseUrls -and (Test-Path $itemsPath)) {
    try {
        $old = Get-Content -Path $itemsPath -Raw | ConvertFrom-Json
        foreach ($entry in @($old)) {
            if ($null -eq $entry) { continue }

            $folderKey = [string]$entry.installFolder
            $nameKey = [string]$entry.name

            if (-not [string]::IsNullOrWhiteSpace($folderKey)) {
                $existingUrls[$folderKey] = [string]$entry.url
            }

            if (-not [string]::IsNullOrWhiteSpace($nameKey)) {
                $existingUrls[$nameKey] = [string]$entry.url
            }
        }

        Write-Host "Reusing existing URLs from $itemsPath where available."
    }
    catch {
        Write-Warning "Could not read existing items file: $($_.Exception.Message)"
    }
}

$mappedUrls = @{}

if (-not [string]::IsNullOrWhiteSpace($UrlMap)) {
    $mapPath = (Resolve-Path $UrlMap).Path
    $rawMap = Get-Content -Path $mapPath -Raw | ConvertFrom-Json

    if ($rawMap -is [System.Array]) {
        foreach ($entry in $rawMap) {
            if ($null -eq $entry) { continue }

            $folderKey = [string]$entry.installFolder
            $nameKey = [string]$entry.name
            $url = [string]$entry.url

            if (-not [string]::IsNullOrWhiteSpace($folderKey)) {
                $mappedUrls[$folderKey] = $url
            }

            if (-not [string]::IsNullOrWhiteSpace($nameKey)) {
                $mappedUrls[$nameKey] = $url
            }
        }
    }
    else {
        foreach ($property in $rawMap.PSObject.Properties) {
            $mappedUrls[$property.Name] = [string]$property.Value
        }
    }

    Write-Host "Loaded URL map from $mapPath."
}

function Get-ItemUrl {
    param([string]$SafeFolder, [string]$Name)

    $url = ""

    if ($mappedUrls.ContainsKey($SafeFolder)) {
        $url = $mappedUrls[$SafeFolder]
    }
    elseif ($mappedUrls.ContainsKey($Name)) {
        $url = $mappedUrls[$Name]
    }
    elseif ($existingUrls.ContainsKey($SafeFolder)) {
        $url = $existingUrls[$SafeFolder]
    }
    elseif ($existingUrls.ContainsKey($Name)) {
        $url = $existingUrls[$Name]
    }

    if (-not (Test-RealUrl $url)) {
        if ($UrlTemplate.Contains('{0}')) {
            $url = $UrlTemplate -f $SafeFolder
        }
        else {
            $url = $UrlTemplate
        }
    }

    return $url
}

function Get-ZipLayout {
    param($Archive)

    $rootFiles = 0
    $topDirs = @()

    foreach ($entry in $Archive.Entries) {
        $name = [string]$entry.FullName
        if ([string]::IsNullOrEmpty($name)) { continue }

        $name = $name.Replace('\', '/')
        $slash = $name.IndexOf('/')
        if ($slash -lt 0) {
            $rootFiles++
        }
        else {
            $topDirs += $name.Substring(0, $slash)
        }
    }

    return [pscustomobject]@{
        RootFiles = $rootFiles
        TopDirs = @($topDirs | Sort-Object -Unique)
    }
}

function Get-FallbackModIdentity {
    param([string]$ZipPath, $Archive)

    $baseName = [IO.Path]::GetFileNameWithoutExtension($ZipPath)
    $folder = $baseName

    if ($null -ne $Archive) {
        $layout = Get-ZipLayout -Archive $Archive
        $contentFolders = @(
            'Config', 'XUi', 'Resources', 'Mods', 'Harmony', 'Assemblies',
            'Localization', 'Patches', 'ItemIcons', 'UIAtlases', 'Unity', 'Data'
        )

        if ($layout.RootFiles -eq 0 -and
            $layout.TopDirs.Count -eq 1 -and
            $contentFolders -notcontains $layout.TopDirs[0]) {
            $folder = $layout.TopDirs[0]
        }
    }

    # QLNPackServer cache zips end with a 12-hex fingerprint.
    $folder = $folder -replace '-[0-9a-fA-F]{12}$', ''
    $folder = $folder -replace '[-_ ]v?\d+(?:[._-]\d+){1,3}$', ''
    $safeFolder = ConvertTo-SafeFolder $folder

    return [pscustomobject]@{
        Name = $safeFolder
        DisplayName = $baseName
        Version = '0.0.0'
        InstallFolder = $safeFolder
        HasModInfo = $false
    }
}

function Read-ModInfoFromZip {
    param([string]$ZipPath)

    $archive = $null

    try {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
        $entries = @($archive.Entries | Where-Object { $_.FullName -match '(^|/)ModInfo\.xml$' })

        if ($entries.Count -eq 0) {
            return Get-FallbackModIdentity -ZipPath $ZipPath -Archive $archive
        }

        $entry = $entries | Where-Object { $_.FullName -eq 'ModInfo.xml' } | Select-Object -First 1

        if ($null -eq $entry) {
            if ($entries.Count -ne 1) {
                return Get-FallbackModIdentity -ZipPath $ZipPath -Archive $archive
            }

            $entry = $entries[0]
        }

        $reader = New-Object System.IO.StreamReader($entry.Open())
        try {
            $text = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        [xml]$info = $text
        $name = Get-ModInfoValue -Xml $info -Name 'Name' -Fallback ([IO.Path]::GetFileNameWithoutExtension($ZipPath))
        $version = Get-ModInfoValue -Xml $info -Name 'Version' -Fallback '0.0.0'
        $displayName = Get-ModInfoValue -Xml $info -Name 'DisplayName' -Fallback $name

        if ($name -notmatch '^[0-9A-Za-z_\-]+$') {
            return Get-FallbackModIdentity -ZipPath $ZipPath -Archive $archive
        }

        $installFolder = ""

        $parts = $entry.FullName.Split('/')
        if ($parts.Length -gt 1) {
            $installFolder = $parts[0]
        }

        if ([string]::IsNullOrWhiteSpace($installFolder)) {
            $installFolder = [IO.Path]::GetFileNameWithoutExtension($ZipPath)

            # QLNPackServer cache zips end with a 12-hex fingerprint.
            $installFolder = $installFolder -replace '-[0-9a-fA-F]{12}$', ''
            $installFolder = $installFolder -replace '[-_ ]v?\d+(?:[._-]\d+){1,3}$', ''
        }

        if ([string]::IsNullOrWhiteSpace($installFolder)) {
            $installFolder = $name
        }

        return [pscustomobject]@{
            Name = $name
            DisplayName = $displayName
            Version = $version
            InstallFolder = $installFolder
            HasModInfo = $true
        }
    }
    catch {
        return Get-FallbackModIdentity -ZipPath $ZipPath -Archive $archive
    }
    finally {
        if ($null -ne $archive) {
            $archive.Dispose()
        }
    }
}

$items = @()

if ($FromZips) {
    $zipFiles = @(Get-ChildItem -Path $zipDirPath -Filter *.zip -File | Sort-Object Name)

    foreach ($zipFile in $zipFiles) {
        $mod = Read-ModInfoFromZip -ZipPath $zipFile.FullName

        if ($null -eq $mod) {
            Write-Host "Skipping $($zipFile.Name): could not read any mod files."
            continue
        }

        $name = $mod.Name
        $version = $mod.Version
        $displayName = $mod.DisplayName
        $safeFolder = ConvertTo-SafeFolder $mod.InstallFolder
        $fileValue = Get-RelativePath -BaseDir $itemsDir -Path $zipFile.FullName
        $url = Get-ItemUrl -SafeFolder $safeFolder -Name $name

        $items += [ordered]@{
            name = $name
            displayName = $displayName
            version = $version
            url = $url
            file = $fileValue
            installFolder = $safeFolder
        }

        if ($mod.HasModInfo) {
            Write-Host "Added from zip: $($zipFile.Name)"
        }
        else {
            Write-Host "Added from zip (no ModInfo.xml): $($zipFile.Name)"
        }
    }
}
else {
    $modDirs = Get-ChildItem -Path $packDirPath -Directory | Sort-Object Name

    foreach ($dir in $modDirs) {
        if ($dir.Name -eq '000_QLNPackBootstrap' -or $dir.Name -eq 'Data') {
            Write-Host "Skipping $($dir.Name): bootstrap/reserved folder."
            continue
        }

        $modInfoPath = Join-Path $dir.FullName 'ModInfo.xml'
        $hasModInfo = Test-Path $modInfoPath

        if ($hasModInfo) {
            [xml]$info = Get-Content -Path $modInfoPath -Raw
            $name = Get-ModInfoValue -Xml $info -Name 'Name' -Fallback $dir.Name
            $version = Get-ModInfoValue -Xml $info -Name 'Version' -Fallback '0.0.0'
            $displayName = Get-ModInfoValue -Xml $info -Name 'DisplayName' -Fallback $name

            if ($name -notmatch '^[0-9A-Za-z_\-]+$') {
                Write-Warning "ModInfo.xml in $($dir.Name) has an invalid Name '$name'. Using a safe folder name instead."
                $name = ConvertTo-SafeFolder $dir.Name
                $displayName = $dir.Name
                $hasModInfo = $false
            }
        }
        else {
            $name = ConvertTo-SafeFolder $dir.Name
            $version = '0.0.0'
            $displayName = $dir.Name
        }

        $safeFolder = ConvertTo-SafeFolder $dir.Name
        $zipName = (ConvertTo-SafeFolder ($dir.Name + '-' + $version)) + '.zip'
        $zipPath = Join-Path $zipDirPath $zipName

        if ($UseExistingZips) {
            if (-not (Test-Path $zipPath)) {
                throw "Existing zip not found: $zipPath. Run the packer first, or omit -UseExistingZips."
            }

            Write-Host "Using existing zip: $zipName"
        }
        else {
            $needsBuild = $Force -or -not (Test-Path $zipPath)

            if (-not $needsBuild) {
                $zipTime = (Get-Item $zipPath).LastWriteTimeUtc
                $newestFile = Get-ChildItem -Path $dir.FullName -Recurse -File -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTimeUtc -Descending |
                    Select-Object -First 1

                if ($null -ne $newestFile -and $newestFile.LastWriteTimeUtc -gt $zipTime) {
                    $needsBuild = $true
                }
            }

            if ($needsBuild) {
                if (Test-Path $zipPath) {
                    Remove-Item $zipPath -Force
                }

                [System.IO.Compression.ZipFile]::CreateFromDirectory(
                    $dir.FullName,
                    $zipPath,
                    [System.IO.Compression.CompressionLevel]::Optimal,
                    $false)

                Write-Host "Created zip: $zipName"
            }
            else {
                Write-Host "Reusing zip: $zipName (current; use -Force to rebuild)"
            }
        }

        $fileValue = Get-RelativePath -BaseDir $itemsDir -Path $zipPath
        $url = Get-ItemUrl -SafeFolder $safeFolder -Name $name

        $items += [ordered]@{
            name = $name
            displayName = $displayName
            version = $version
            url = $url
            file = $fileValue
            installFolder = $safeFolder
        }

        if ($hasModInfo) {
            Write-Host "Added: $($dir.Name)"
        }
        else {
            Write-Host "Added (no ModInfo.xml): $($dir.Name)"
        }
    }
}

if ($items.Count -eq 0) {
    throw "No mod folders or zips were found. Check the pack/zip folder."
}

$json = ConvertTo-Json -InputObject @($items) -Depth 6
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($itemsPath, $json, $utf8NoBom)

Write-Host ""
Write-Host "Wrote: $itemsPath"
Write-Host "Zips:  $zipDirPath"
Write-Host ""
Write-Host "Next:"
if ($FromZips) {
    Write-Host "1. Upload the zips in $zipDirPath to Google Drive and share them as 'Anyone with the link'."
}
else {
    Write-Host "1. Upload any newly created/rebuilt zips to Google Drive and share them as 'Anyone with the link'."
}
Write-Host "2. If a new upload got a new file ID, update its URL in $itemsPath."
Write-Host "   Reruns preserve existing URLs, including old ones."
Write-Host "3. Or maintain a URL map and rerun with -UrlMap."
Write-Host "4. Build the manifest:"
Write-Host ""
Write-Host "   .\release\build-manifest-from-urls.ps1 -ItemsFile `"$itemsPath`" -OutputDir .\release-out -PackVersion 1.0.0 -GameVersion 3.4"
