param(
    [string]$SourceUri = 'https://myislemap.com/map-data.js?v=53',
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\TheIsleOverlay.App\Assets\GatewayZones.json')
)

$content = (Invoke-WebRequest -UseBasicParsing -Uri $SourceUri).Content
$kind = $null
$zones = [System.Collections.Generic.List[object]]::new()
$circlePattern = '^\s*\{ type: "circle", cx: (?<cx>-?[0-9.]+), cy: (?<cy>-?[0-9.]+), r: (?<r>[0-9.]+), label: "(?<label>[^"]+)", gameLabel: "(?<id>[^"]+)" \},?'
$polygonPattern = '^\s*\{ type: "polygon", points: "(?<points>[^"]+)", label: "(?<label>[^"]+)", gameLabel: "(?<id>[^"]+)" \},?'

foreach ($line in ($content -split "`n")) {
    if ($line -match '^\s{2}(sanctuary|patrol|migration):\s*\{') {
        $kind = $Matches[1]
        continue
    }

    if ($line -match '^\s{2}[A-Za-z][A-Za-z0-9_]*:\s*\{' -and
        $line -notmatch '^\s{2}(sanctuary|patrol|migration):') {
        $kind = $null
        continue
    }

    if ($null -eq $kind) {
        continue
    }

    $points = [System.Collections.Generic.List[object]]::new()
    $label = $null
    $id = $null
    if ($line -match $circlePattern) {
        $cx = [double]::Parse($Matches.cx, [System.Globalization.CultureInfo]::InvariantCulture)
        $cy = [double]::Parse($Matches.cy, [System.Globalization.CultureInfo]::InvariantCulture)
        $radius = [double]::Parse($Matches.r, [System.Globalization.CultureInfo]::InvariantCulture)
        $label = $Matches.label
        $id = $Matches.id
        for ($index = 0; $index -lt 40; $index++) {
            $angle = 2 * [Math]::PI * $index / 40
            $points.Add([ordered]@{
                left = ($cx + $radius * [Math]::Cos($angle)) / 1000
                top = ($cy + $radius * [Math]::Sin($angle)) / 1003
            })
        }
    }
    elseif ($line -match $polygonPattern) {
        $label = $Matches.label
        $id = $Matches.id
        foreach ($pair in ($Matches.points -split ' ')) {
            $coordinates = $pair -split ','
            $points.Add([ordered]@{
                left = [double]::Parse($coordinates[0], [System.Globalization.CultureInfo]::InvariantCulture) / 1000
                top = [double]::Parse($coordinates[1], [System.Globalization.CultureInfo]::InvariantCulture) / 1003
            })
        }
    }

    if ($points.Count -gt 0) {
        $zones.Add([ordered]@{
            id = $id
            name = $label
            kind = $kind
            points = $points
        })
    }
}

if ($zones.Count -lt 20) {
    throw "Zone import returned only $($zones.Count) entries; refusing to replace the bundled catalog."
}

$document = [ordered]@{
    source = $SourceUri
    viewBox = '0 0 1000 1003'
    zones = $zones
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8
Write-Output "Imported $($zones.Count) Gateway zones to $resolvedOutput"
