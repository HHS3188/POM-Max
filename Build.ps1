$ErrorActionPreference = 'Stop'

$modDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$candidateRoots = @()
if ($env:ADOFAI_GAME_ROOT) {
    $candidateRoots += $env:ADOFAI_GAME_ROOT
}
$candidateRoots += Split-Path -Parent (Split-Path -Parent $modDir)
$candidateRoots += Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $modDir))

$gameRoot = $null
foreach ($candidate in $candidateRoots) {
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
