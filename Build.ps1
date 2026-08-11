$ErrorActionPreference = 'Stop'

$modDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$candidateRoots = @()
if ($env:ADOFAI_GAME_ROOT) {
    $candidateRoots += $env:ADOFAI_GAME_ROOT
}
$candidateRoots += Split-Path -Parent (Split-Path -Parent $modDir)
$candidateRoots += Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $modDir))

$steamRoots = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in @(
    @('HKCU:\Software\Valve\Steam', 'SteamPath'),
    @('HKLM:\Software\WOW6432Node\Valve\Steam', 'InstallPath'),
    @('HKLM:\Software\Valve\Steam', 'InstallPath')
)) {
    try {
        $value = (Get-ItemProperty -LiteralPath $entry[0] -Name $entry[1] -ErrorAction Stop).$($entry[1])
        if ($value -and (Test-Path -LiteralPath $value -PathType Container)) {
            [void]$steamRoots.Add([IO.Path]::GetFullPath($value))
        }
    }
    catch {
    }
}

foreach ($steamRoot in @($steamRoots)) {
    $candidateRoots += Join-Path $steamRoot 'steamapps\common\A Dance of Fire and Ice'
    $vdf = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
    if (-not (Test-Path -LiteralPath $vdf -PathType Leaf)) {
        continue
    }

    try {
        $text = [IO.File]::ReadAllText($vdf)
        foreach ($match in [Text.RegularExpressions.Regex]::Matches($text, '"path"\s*"([^"]+)"', 'IgnoreCase')) {
            $library = $match.Groups[1].Value.Replace('\\', '\')
            $candidateRoots += Join-Path $library 'steamapps\common\A Dance of Fire and Ice'
        }
    }
    catch {
    }
}

$gameRoot = $null
foreach ($candidate in $candidateRoots | Select-Object -Unique) {
    if ($candidate -and (Test-Path (Join-Path $candidate 'A Dance of Fire and Ice_Data\Managed'))) {
        $gameRoot = $candidate
        break
    }
}

if (-not $gameRoot) {
    throw 'Could not locate A Dance of Fire and Ice. Set ADOFAI_GAME_ROOT to the game root.'
}

$managed = Join-Path $gameRoot 'A Dance of Fire and Ice_Data\Managed'
$umm = Join-Path $managed 'UnityModManager'
$windowsRoot = if ($env:WINDIR) { $env:WINDIR } else { $env:SystemRoot }
if (-not $windowsRoot) {
    $windowsRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
}
$csc = Join-Path $windowsRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$dist = Join-Path $modDir 'dist'
$output = Join-Path $dist 'POMMax.dll'
$source = Join-Path $modDir 'POMMax.cs'

New-Item -ItemType Directory -Path $dist -Force | Out-Null

$args = @(
    '/nologo',
    '/noconfig',
    '/nostdlib+',
    '/utf8output',
    '/codepage:65001',
    '/target:library',
    '/optimize+',
    '/debug-',
    "/out:$output",
    ('/reference:' + (Join-Path $managed 'mscorlib.dll')),
    ('/reference:' + (Join-Path $managed 'System.dll')),
    ('/reference:' + (Join-Path $managed 'System.Core.dll')),
    ('/reference:' + (Join-Path $managed 'System.Xml.dll')),
    ('/reference:' + (Join-Path $managed 'netstandard.dll')),
    ('/reference:' + (Join-Path $managed 'Assembly-CSharp.dll')),
    ('/reference:' + (Join-Path $managed 'Assembly-CSharp-firstpass.dll')),
    ('/reference:' + (Join-Path $managed 'DOTween.dll')),
    ('/reference:' + (Join-Path $managed 'RDTools.dll')),
    ('/reference:' + (Join-Path $managed 'UnityEngine.dll')),
    ('/reference:' + (Join-Path $managed 'UnityEngine.AudioModule.dll')),
    ('/reference:' + (Join-Path $managed 'UnityEngine.CoreModule.dll')),
    ('/reference:' + (Join-Path $managed 'UnityEngine.IMGUIModule.dll')),
    ('/reference:' + (Join-Path $managed 'UnityEngine.InputLegacyModule.dll')),
    ('/reference:' + (Join-Path $managed 'UnityEngine.Physics2DModule.dll')),
    ('/reference:' + (Join-Path $managed 'UnityEngine.TextRenderingModule.dll')),
    ('/reference:' + (Join-Path $managed 'UnityEngine.VideoModule.dll')),
    ('/reference:' + (Join-Path $umm '0Harmony.dll')),
    ('/reference:' + (Join-Path $umm 'UnityModManager.dll')),
    $source
)

& $csc @args
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Copy-Item -LiteralPath (Join-Path $modDir 'Info.json') -Destination (Join-Path $dist 'Info.json') -Force
Write-Host "Built: $output"
