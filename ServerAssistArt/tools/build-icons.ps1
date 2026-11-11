# Builds the game-ready UIAtlas PNGs, preview images and contact sheet
# from the high-resolution masters in ..\icons.
#
# Output:
#   ..\UIAtlases\UIAtlas\<sprite>.png   128x128 ARGB32, trimmed and centered
#   ..\preview\<sprite>.png             256x256 ARGB32
#   ..\contact-sheet.png                4-column labelled overview
#
# Requires Windows PowerShell 5.1+ (System.Drawing).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root    = Split-Path -Parent $PSScriptRoot
$srcDir  = Join-Path $root 'icons'
$atlasDir = Join-Path $root 'UIAtlases\UIAtlas'
$prevDir = Join-Path $root 'preview'

New-Item -ItemType Directory -Force -Path $atlasDir, $prevDir | Out-Null

# Preferred order for the contact sheet. Any other PNG in icons/ is appended
# alphabetically, so new masters show up without editing this list.
$preferred = @(
    'admin_kits',
    'admin_shop',
    'admin_suite',
    'auction_house',
    'daily_rewards',
    'dynamic_backpack',
    'permission_gui',
    'plugin_hot_reload',
    'vote_skip_night',
    'qln_pack',
    'sa_kits_collect',
    'sa_kits_manage',
    'sa_kits_create',
    'sa_kits_from_inventory',
    'sa_kits_edit',
    'sa_kits_delete',
    'sa_kits_limits',
    'sa_nav_players',
    'sa_nav_moderation',
    'sa_nav_teleport',
    'sa_nav_items',
    'sa_nav_world',
    'sa_nav_logs',
    'sa_nav_snapshots',
    'sa_nav_server',
    'sa_nav_warps',
    'sa_nav_help',
    'sa_act_ban',
    'sa_act_kick',
    'sa_act_mute',
    'sa_act_freeze',
    'sa_act_warn',
    'sa_act_heal',
    'sa_act_feed',
    'sa_act_godmode',
    'sa_act_spectate',
    'sa_act_give',
    'sa_act_repair',
    'sa_act_time',
    'sa_act_weather',
    'sa_act_airdrop',
    'sa_perm_oxide',
    'sa_perm_game',
    'sa_perm_plugin',
    'sa_perm_groups',
    'sa_perm_users',
    'sa_perm_tracks',
    'sa_profile_panel',
    'sa_profile_identity',
    'sa_profile_active',
    'sa_profile_switch',
    'sa_profile_new',
    'sa_profile_rename',
    'sa_profile_save',
    'sa_profile_delete',
    'sa_stream_panel',
    'sa_stream_home',
    'sa_stream_tier_1',
    'sa_stream_tier_2',
    'sa_stream_tier_3',
    'sa_stream_loadout',
    'sa_stream_catalog',
    'sa_stream_link',
    'sa_stream_link_pending',
    'sa_stream_linked',
    'sa_stream_link_cancel',
    'sa_stream_reward',
    'sa_shop_categories',
    'sa_shop_search',
    'sa_shop_buy',
    'sa_shop_permissions',
    'sa_shop_weapons',
    'sa_shop_ammo',
    'sa_shop_food_medical',
    'sa_shop_resources',
    'sa_shop_blocks',
    'sa_shop_books',
    'sa_act_prev',
    'sa_act_next',
    'sa_auction_browse',
    'sa_auction_mine',
    'sa_auction_sell',
    'sa_auction_cancel',
    'sa_rewards_weekly',
    'sa_rewards_monthly',
    'sa_rewards_seasonal',
    'sa_vote_yes',
    'sa_vote_no',
    'sa_vote_cancel',
    'sa_vote_bloodmoon',
    'sa_buggee_panel',
    'sa_buggee_home',
    'sa_buggee_info',
    'sa_buggee_warn',
    'sa_stream_buff_quick_feet',
    'sa_stream_buff_regen',
    'sa_stream_buff_looter',
    'sa_stream_buff_melee_surge',
    'sa_stream_buff_ranged_surge',
    'sa_stream_buff_infinite_ammo',
    'sa_stream_buff_toughness',
    'sa_stream_buff_xp_gain',
    'sa_stream_buff_pack_mule',
    'sa_stream_debuff_heavy_legs',
    'sa_stream_debuff_blurred_vision',
    'sa_stream_debuff_no_running',
    'sa_stream_debuff_no_melee',
    'sa_stream_debuff_no_ranged',
    'sa_stream_debuff_encumbered',
    'sa_stream_debuff_tougher_enemies',
    'sa_stream_debuff_double_spawns',
    'sa_stream_debuff_no_healing',
    'sa_act_kill',
    'sa_act_note',
    'sa_act_clear',
    'sa_act_take',
    'sa_act_jail',
    'sa_act_buff',
    'sa_act_debuff',
    'sa_act_search',
    'sa_act_creative',
    'sa_act_rollback',
    'sa_act_save',
    'sa_act_spawn',
    'sa_ac_panel',
    'sa_ac_risk',
    'sa_ac_evidence',
    'sa_ac_modules',
    'sa_ac_movement',
    'sa_ac_combat',
    'sa_ac_inventory',
    'sa_ac_grief',
    'sa_ac_exploit',
    'sa_ac_progression',
    'sa_ac_clear',
    'sa_ac_export'
)

$order = New-Object System.Collections.Generic.List[string]
foreach ($name in $preferred) {
    if (Test-Path (Join-Path $srcDir "$name.png")) {
        $order.Add($name)
    }
}

Get-ChildItem $srcDir -Filter *.png | Sort-Object Name | ForEach-Object {
    if (-not $order.Contains($_.BaseName)) {
        $order.Add($_.BaseName)
    }
}

function Get-TrimmedBitmap {
    param([string]$Path)

    $source = [System.Drawing.Bitmap]::FromFile($Path)
    try {
        $minX = $source.Width; $minY = $source.Height; $maxX = -1; $maxY = -1
        for ($y = 0; $y -lt $source.Height; $y++) {
            for ($x = 0; $x -lt $source.Width; $x++) {
                if ($source.GetPixel($x, $y).A -gt 8) {
                    if ($x -lt $minX) { $minX = $x }
                    if ($y -lt $minY) { $minY = $y }
                    if ($x -gt $maxX) { $maxX = $x }
                    if ($y -gt $maxY) { $maxY = $y }
                }
            }
        }
        if ($maxX -lt 0) { throw "No visible pixels found in $Path" }

        $rect = New-Object System.Drawing.Rectangle($minX, $minY, ($maxX - $minX + 1), ($maxY - $minY + 1))
        return $source.Clone($rect, $source.PixelFormat)
    }
    finally {
        $source.Dispose()
    }
}

function New-FittedIcon {
    param(
        [System.Drawing.Bitmap]$Source,
        [int]$Size,
        [int]$Padding
    )

    $canvas = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        $inner = $Size - ($Padding * 2)
        $scale = [Math]::Min($inner / $Source.Width, $inner / $Source.Height)
        $w = [int][Math]::Round($Source.Width * $scale)
        $h = [int][Math]::Round($Source.Height * $scale)
        $x = [int][Math]::Round(($Size - $w) / 2)
        $y = [int][Math]::Round(($Size - $h) / 2)

        $dest = New-Object System.Drawing.Rectangle($x, $y, $w, $h)
        $g.DrawImage($Source, $dest, 0, 0, $Source.Width, $Source.Height, [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $g.Dispose()
    }
    return $canvas
}

$built = @()

foreach ($name in $order) {
    $srcPath = Join-Path $srcDir "$name.png"
    if (-not (Test-Path $srcPath)) {
        Write-Warning "Missing master: $srcPath"
        continue
    }

    # All mod atlases fold into the same global UIAtlas, so prefix the sprite
    # names to avoid colliding with other mods' PNGs. Masters that already start
    # with sa_ keep their name.
    $sprite = if ($name.StartsWith('sa_')) { $name } else { "sa_$name" }

    $trimmed = Get-TrimmedBitmap -Path $srcPath
    try {
        $atlas = New-FittedIcon -Source $trimmed -Size 128 -Padding 4
        try { $atlas.Save((Join-Path $atlasDir "$sprite.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
        finally { $atlas.Dispose() }

        $preview = New-FittedIcon -Source $trimmed -Size 256 -Padding 10
        try { $preview.Save((Join-Path $prevDir "$sprite.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
        finally { $preview.Dispose() }
    }
    finally {
        $trimmed.Dispose()
    }

    $built += $sprite
    Write-Host "Built $sprite"
}

# Contact sheet
$cell = 300
$labelH = 44
$cols = 4
$rows = [int][Math]::Ceiling($built.Count / $cols)
$sheet = New-Object System.Drawing.Bitmap(($cell * $cols), (($cell + $labelH) * $rows), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($sheet)
try {
    $g.Clear([System.Drawing.Color]::FromArgb(255, 22, 24, 26))
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $font = New-Object System.Drawing.Font('Segoe UI', 15, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = [System.Drawing.Brushes]::WhiteSmoke
    $muted = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 160, 170, 175))
    $line = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 52, 58, 62), 1)

    for ($i = 0; $i -lt $built.Count; $i++) {
        $name = $built[$i]
        $col = $i % $cols
        $row = [int][Math]::Floor($i / $cols)
        $x = $col * $cell
        $y = $row * ($cell + $labelH)

        $g.DrawRectangle($line, $x, $y, ($cell - 1), ($cell + $labelH - 1))

        $preview = [System.Drawing.Image]::FromFile((Join-Path $prevDir "$name.png"))
        try {
            $g.DrawImage($preview, ($x + (($cell - 256) / 2)), ($y + 20), 256, 256)
        }
        finally { $preview.Dispose() }

        $text = $name
        $size = $g.MeasureString($text, $font)
        $g.DrawString($text, $font, $brush, ($x + (($cell - $size.Width) / 2)), ($y + $cell + 8))
    }
}
finally {
    $g.Dispose()
}
$sheet.Save((Join-Path $root 'contact-sheet.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()

Write-Host ''
Write-Host "Done. $($built.Count) icons."
