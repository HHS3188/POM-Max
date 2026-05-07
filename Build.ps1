$ErrorActionPreference = 'Stop'

$modDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameRoot = Split-Path -Parent (Split-Path -Parent $modDir)
$managed = Join-Path $gameRoot 'A Dance of Fire and Ice_Data\Managed'
$umm = Join-Path $managed 'UnityModManager'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $modDir 'POMMax.dll'
$source = Join-Path $modDir 'POMMax.cs'

$args = @(
    '/nologo',
    '/noconfig',
    '/nostdlib+',
    '/utf8output',
    '/codepage:65001',
    '/target:library',
    '/optimize+',
    '/debug:pdbonly',
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
    ('/reference:' + (Join-Path $managed 'UnityEngine.VideoModule.dll')),
    ('/reference:' + (Join-Path $umm '0Harmony.dll')),
    ('/reference:' + (Join-Path $umm 'UnityModManager.dll')),
    $source
)

& $csc @args
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
