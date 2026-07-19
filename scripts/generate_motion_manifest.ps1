param(
    [string]$GameData = 'E:\SteamLibrary\steamapps\common\Digimon Story Time Stranger\gamedata',
    [string]$MbeDumper = 'E:\ReverseEngineProjects\TimeStranger\MbeDumper\bin\Release\net9.0\MbeDumper.exe',
    [string]$Output = 'E:\ReverseEngineProjects\TimeStranger\AnimationFixes\src\AnimationFix\data\native_motion_names.txt'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $MbeDumper)) {
    throw "MbeDumper not found: $MbeDumper"
}

$archives = Get-ChildItem -LiteralPath $GameData -Filter '*.mvgl' |
    Where-Object { $_.Name -match '^(app_0|patch|addcont_[0-9]+)\.dx11\.mvgl$' } |
    Sort-Object Name

$motions = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)

foreach ($archive in $archives) {
    & $MbeDumper list $archive.FullName '.anim' | ForEach-Object {
        if ($_ -match '^(chr[^_\\\s]+)_([^\\\s]+)\.anim') {
            [void]$motions.Add("$($Matches[1])_$($Matches[2])".ToLowerInvariant())
        }
    }
}

$outputDirectory = Split-Path -Parent $Output
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$motions | Sort-Object | Set-Content -LiteralPath $Output -Encoding utf8NoBOM
Write-Host "Wrote $($motions.Count) native motion names to $Output"
