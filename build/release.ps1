param(
    [string]$Version = (Get-Date -Format "yyyy.MM.dd"),
    [string]$OutputDirectory = "release-out"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Output = Join-Path $Root $OutputDirectory
$StageRoot = Join-Path $Output ".stage"

function Reset-Directory([string]$Path) {
    if (Test-Path $Path) {
        Remove-Item -Recurse -Force $Path
    }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Copy-IfExists([string]$Source, [string]$Destination, [string]$Root) {
    if (-not (Test-Path $Source)) {
        return
    }
    $relative = $Source.Substring($Root.Length).TrimStart("\", "/")
    $parts = $relative -split "[\\/]", 2
    $targetRelative = if ($parts.Length -gt 1) { $parts[1] } else { $parts[0] }
    $target = Join-Path $Destination $targetRelative
    $parent = Split-Path -Parent $target
    if ($parent) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    Copy-Item -LiteralPath $Source -Destination $target -Recurse -Force
}

function New-ZipFromDirectory([string]$Source, [string]$ZipPath, [string]$EntryPrefix = "") {
    $zipFull = [System.IO.Path]::GetFullPath($ZipPath)
    $parent = Split-Path -Parent $zipFull
    if ($parent) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    if (Test-Path $zipFull) {
        Remove-Item -LiteralPath $zipFull -Force
    }

    $stream = [System.IO.File]::Open($zipFull, [System.IO.FileMode]::CreateNew)
    $archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $sourceFull = (Resolve-Path $Source).Path
        $files = Get-ChildItem -LiteralPath $sourceFull -Recurse -File
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($sourceFull.Length).TrimStart("\", "/").Replace("\", "/")
            $entryName = if ($EntryPrefix) { "$EntryPrefix/$relative" } else { $relative }
            $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $file.LastWriteTime
            $entryStream = $entry.Open()
            try {
                $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
                $entryStream.Write($bytes, 0, $bytes.Length)
            }
            finally {
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
        $stream.Dispose()
    }
}

function Get-PluginVersion([string]$Path) {
    $text = Get-Content -LiteralPath $Path -Raw
    $match = [regex]::Match($text, '\[Info\([^,]+,\s*[^,]+,\s*"([^"]+)"\)\]')
    if ($match.Success) {
        return $match.Groups[1].Value
    }
    return $null
}

function Get-ModVersion([string]$Directory) {
    $modInfo = Join-Path $Directory "ModInfo.xml"
    if (-not (Test-Path $modInfo)) {
        return $null
    }
    $text = Get-Content -LiteralPath $modInfo -Raw
    $match = [regex]::Match($text, '<Version\s+value="([^"]+)"')
    if ($match.Success) {
        return $match.Groups[1].Value
    }
    return $null
}

function Get-ProgramVersion([string]$Directory) {
    $versionFile = Join-Path $Directory "version.txt"
    if (Test-Path $versionFile) {
        return (Get-Content -LiteralPath $versionFile -Raw).Trim()
    }
    return $null
}

$plugins = @(
    @{ Name = "AdminKits"; Source = "AdminKits/oxide/plugins/AdminKits.cs"; Extras = @("AdminKits/README.txt", "AdminKits/ICONS.md", "AdminKits/Config/AdminKits.example.json") },
    @{ Name = "AdminShop"; Source = "AdminShop/oxide/plugins/AdminShop.cs"; Extras = @("AdminShop/README.txt") },
    @{ Name = "AdminSuite"; Source = "AdminSuite/oxide/plugins/AdminSuite.cs"; Extras = @("AdminSuite/README.txt", "AdminSuite/docs/QUICKSTART.md") },
    @{ Name = "AuctionHouse"; Source = "AuctionHouse/oxide/plugins/AuctionHouse.cs"; Extras = @("AuctionHouse/README.txt", "AuctionHouse/UPDATE.md") },
    @{ Name = "BuggeeNet"; Source = "BuggeeNet/oxide/plugins/BuggeeCore.cs"; Extras = @("BuggeeNet/README.md") },
    @{ Name = "DailyRewards"; Source = "DailyRewards/oxide/plugins/DailyRewards.cs"; Extras = @("DailyRewards/README.txt", "DailyRewards/ICON_MAP.md") },
    @{ Name = "DynamicBackpack"; Source = "DynamicBackpack/oxide/plugins/DynamicBackpack.cs"; Extras = @("DynamicBackpack/README.txt") },
    @{ Name = "PermissionGui"; Source = "PermissionGui/oxide/plugins/PermissionGui.cs"; Extras = @("PermissionGui/README.txt", "PermissionGui/TEMPLATE_self_permissions.txt") },
    @{ Name = "PlayerProfiles"; Source = "PlayerProfiles/oxide/plugins/PlayerProfiles.cs"; Extras = @("PlayerProfiles/README.txt", "PlayerProfiles/icon-request.md", "PlayerProfiles/update.md", "PlayerProfiles/Config/PlayerProfiles.example.json") },
    @{ Name = "PluginHotReload"; Source = "PluginHotReload/oxide/plugins/PluginHotReload.cs"; Extras = @("PluginHotReload/README.txt") },
    @{ Name = "ServerAssist"; Source = "ServerAssist/oxide/plugins/ServerAssist.cs"; Extras = @("ServerAssist/README.txt", "ServerAssist/API.md", "ServerAssist/update.md", "ServerAssist/MIGRATION.md") },
    @{ Name = "TwitchIntegration"; Source = "TwitchIntegration/oxide/plugins/TwitchIntegration.cs"; Extras = @("TwitchIntegration/README.md", "TwitchIntegration/DESIGN.md") },
    @{ Name = "VoteSkipNight"; Source = "VoteSkipNight/oxide/plugins/VoteSkipNight.cs"; Extras = @("VoteSkipNight/README.txt") }
)

$mods = @(
    @{ Name = "ServerAssistMod"; Source = "release-inputs/mods/ServerAssistMod"; RootName = "AA0_ServerAssistMod" },
    @{ Name = "QLNPackBootstrap"; Source = "release-inputs/mods/QLNPackBootstrap"; RootName = "000_QLNPackBootstrap" }
)

Reset-Directory $Output
Reset-Directory $StageRoot

$pluginGroupStage = Join-Path $StageRoot "plugins"
$modGroupStage = Join-Path $StageRoot "mods"
$programGroupStage = Join-Path $StageRoot "programs"
$allStage = Join-Path $StageRoot "all"
New-Item -ItemType Directory -Force -Path $pluginGroupStage, $modGroupStage, $programGroupStage, $allStage | Out-Null

$pluginStagingRoot = Join-Path $pluginGroupStage "oxide/plugins"
New-Item -ItemType Directory -Force -Path $pluginStagingRoot | Out-Null

$generated = New-Object System.Collections.Generic.List[string]
$pluginVersions = New-Object System.Collections.Generic.List[string]

foreach ($plugin in $plugins) {
    $source = Join-Path $Root $plugin.Source
    if (-not (Test-Path $source)) {
        throw "Plugin source not found: $source"
    }

    $pluginVersion = Get-PluginVersion $source
    if (-not $pluginVersion) {
        throw "Could not read [Info] version from $source"
    }

    $stage = Join-Path $StageRoot ("plugin-" + $plugin.Name)
    $stagePlugins = Join-Path $stage "oxide/plugins"
    New-Item -ItemType Directory -Force -Path $stagePlugins | Out-Null
    Copy-Item -LiteralPath $source -Destination (Join-Path $stagePlugins (Split-Path -Leaf $source)) -Force

    foreach ($extra in $plugin.Extras) {
        Copy-IfExists (Join-Path $Root $extra) $stage $Root
    }

    $zipName = "$($plugin.Name)-$pluginVersion.zip"
    $zipPath = Join-Path $Output $zipName
    New-ZipFromDirectory $stage $zipPath
    $generated.Add($zipPath) | Out-Null

    Copy-Item -LiteralPath $source -Destination (Join-Path $pluginStagingRoot (Split-Path -Leaf $source)) -Force
    $pluginVersions.Add("$($plugin.Name) $pluginVersion") | Out-Null
}

$pluginReadme = Join-Path $pluginGroupStage "README.txt"
@(
    "Server Assist plugin pack",
    "Release: $Version",
    "",
    "Install all plugin files in oxide/plugins/.",
    "",
    ($pluginVersions | Sort-Object)
) | Set-Content -LiteralPath $pluginReadme -Encoding UTF8

$zipPath = Join-Path $Output "Oxide-Plugins-$Version.zip"
New-ZipFromDirectory $pluginGroupStage $zipPath
$generated.Add($zipPath) | Out-Null

foreach ($mod in $mods) {
    $source = Join-Path $Root $mod.Source
    if (-not (Test-Path $source)) {
        throw "Mod input folder not found: $source"
    }

    $modVersion = Get-ModVersion $source
    if (-not $modVersion) {
        throw "Could not read ModInfo.xml version from $source"
    }

    $stage = Join-Path $StageRoot ("mod-" + $mod.Name)
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Copy-Item -Path (Join-Path $source "*") -Destination $stage -Recurse -Force

    $zipName = "$($mod.Name)-$modVersion.zip"
    $zipPath = Join-Path $Output $zipName
    New-ZipFromDirectory $stage $zipPath $mod.RootName
    $generated.Add($zipPath) | Out-Null

    $groupTarget = Join-Path $modGroupStage $mod.RootName
    New-Item -ItemType Directory -Force -Path $groupTarget | Out-Null
    Copy-Item -Path (Join-Path $source "*") -Destination $groupTarget -Recurse -Force
}

$zipPath = Join-Path $Output "Oxide-Mods-$Version.zip"
New-ZipFromDirectory $modGroupStage $zipPath
$generated.Add($zipPath) | Out-Null

$programs = @(
    @{ Name = "QLNPackServer"; Source = "release-inputs/programs/QLNPackServer"; RootName = "QLNPackServer" },
    @{ Name = "QLNPackUpdater"; Source = "release-inputs/programs/QLNPackUpdater"; RootName = "QLNPackUpdater" }
)

foreach ($program in $programs) {
    $source = Join-Path $Root $program.Source
    if (-not (Test-Path $source)) {
        Write-Host "Skipping $($program.Name): $source not present."
        continue
    }

    $files = Get-ChildItem -LiteralPath $source -Recurse -File
    if ($files.Count -eq 0) {
        Write-Host "Skipping $($program.Name): no files."
        continue
    }

    $programVersion = Get-ProgramVersion $source
    if (-not $programVersion) {
        $programVersion = $Version
    }

    $stage = Join-Path $StageRoot ("program-" + $program.Name)
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Copy-Item -Path (Join-Path $source "*") -Destination $stage -Recurse -Force

    $zipName = "$($program.Name)-$programVersion.zip"
    $zipPath = Join-Path $Output $zipName
    New-ZipFromDirectory $stage $zipPath $program.RootName
    $generated.Add($zipPath) | Out-Null

    $groupTarget = Join-Path $programGroupStage $program.RootName
    New-Item -ItemType Directory -Force -Path $groupTarget | Out-Null
    Copy-Item -Path (Join-Path $source "*") -Destination $groupTarget -Recurse -Force
}

$twitchSource = Join-Path $Root "release-inputs/programs/TwitchRelay"
if (Test-Path $twitchSource) {
    $runtimeDirectories = Get-ChildItem -LiteralPath $twitchSource -Directory
    foreach ($runtime in $runtimeDirectories) {
        $files = Get-ChildItem -LiteralPath $runtime.FullName -Recurse -File
        if ($files.Count -eq 0) {
            continue
        }

        $programVersion = Get-ProgramVersion $twitchSource
        if (-not $programVersion) {
            $programVersion = $Version
        }

        $zipName = "TwitchRelay-$programVersion-$($runtime.Name).zip"
        $zipPath = Join-Path $Output $zipName
        New-ZipFromDirectory $runtime.FullName $zipPath "TwitchRelay/$($runtime.Name)"
        $generated.Add($zipPath) | Out-Null

        $groupTarget = Join-Path $programGroupStage ("TwitchRelay/" + $runtime.Name)
        New-Item -ItemType Directory -Force -Path $groupTarget | Out-Null
        Copy-Item -Path (Join-Path $runtime.FullName "*") -Destination $groupTarget -Recurse -Force
    }
}

$programFiles = Get-ChildItem -LiteralPath $programGroupStage -Recurse -File
if ($programFiles.Count -gt 0) {
    $zipPath = Join-Path $Output "Oxide-Programs-$Version.zip"
    New-ZipFromDirectory $programGroupStage $zipPath
    $generated.Add($zipPath) | Out-Null
}
else {
    Write-Host "Skipping Oxide-Programs ZIP: no program inputs."
}

Copy-Item -Path (Join-Path $pluginGroupStage "*") -Destination $allStage -Recurse -Force
$allMods = Join-Path $allStage "mods"
New-Item -ItemType Directory -Force -Path $allMods | Out-Null
Copy-Item -Path (Join-Path $modGroupStage "*") -Destination $allMods -Recurse -Force

if ($programFiles.Count -gt 0) {
    $allPrograms = Join-Path $allStage "programs"
    New-Item -ItemType Directory -Force -Path $allPrograms | Out-Null
    Copy-Item -Path (Join-Path $programGroupStage "*") -Destination $allPrograms -Recurse -Force
}

@(
    "Server Assist release $Version",
    "",
    "oxide/plugins/      Oxide plugins",
    "mods/               client mods (AA0_ServerAssistMod, 000_QLNPackBootstrap)",
    "programs/           optional programs",
    "",
    "Install plugins into oxide/plugins on the server.",
    "Install client mods into the Mods folder on the server and every client."
) | Set-Content -LiteralPath (Join-Path $allStage "README.txt") -Encoding UTF8

$zipPath = Join-Path $Output "Oxide-All-$Version.zip"
New-ZipFromDirectory $allStage $zipPath
$generated.Add($zipPath) | Out-Null

$checksums = foreach ($file in ($generated | Sort-Object)) {
    if (Test-Path $file) {
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $(Split-Path -Leaf $file)"
    }
}
$checksums | Set-Content -LiteralPath (Join-Path $Output "SHA256SUMS.txt") -Encoding ASCII

Write-Host ""
Write-Host "Created release assets in ${Output}:"
Get-ChildItem -LiteralPath $Output -File | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0} ({1:N1} KB)" -f $_.Name, ($_.Length / 1KB))
}
