$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$infoPath = Join-Path $root 'Info.json'
$dllPath = Join-Path $root 'dist\POMMax.dll'
$v4Source = Join-Path $root 'POMMaxV4.cs'

if (-not (Test-Path -LiteralPath $infoPath -PathType Leaf)) {
    throw 'Info.json is missing.'
}
if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
    throw 'dist\POMMax.dll is missing. Run Build.ps1 first.'
}

$info = Get-Content -LiteralPath $infoPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($info.Id -ne 'POMMax' -or $info.Version -ne '4.0.0') {
    throw 'Release metadata is not POMMax 4.0.0.'
}

$sourceText = Get-Content -LiteralPath $v4Source -Raw -Encoding UTF8
foreach ($forbidden in @('RDInput', 'KeyCode', 'GC.Collect', 'FindObjectsOfType', 'Resources.FindObjects', 'File.Write', 'File.Delete')) {
    if ($sourceText.Contains($forbidden)) {
        throw "Forbidden V4 hot-path dependency found: $forbidden"
    }
}

$ilspy = Get-Command ilspycmd -ErrorAction SilentlyContinue
if (-not $ilspy) {
    throw 'ilspycmd is required for assembly verification.'
}

$decompileRoot = Join-Path ([IO.Path]::GetTempPath()) ('pommax-v4-verify-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $decompileRoot | Out-Null
    & $ilspy.Source $dllPath -p -o $decompileRoot | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "ilspycmd failed with exit code $LASTEXITCODE"
    }

    $decompiled = Get-ChildItem -LiteralPath $decompileRoot -Recurse -Filter '*.cs' |
        Get-Content -Raw -Encoding UTF8
    $joined = [string]::Join("`n", $decompiled)
    foreach ($required in @(
        'class V4DecorationLogicUpdatePatch',
        'class V4VisualDecorationShaderPatch',
        'class V4DecorationManagerUpdatePatch',
        'class PerformanceTelemetry',
        'class AdaptiveRuntimeScheduler',
        'class V4TextureHeaderCache')) {
        if (-not $joined.Contains($required)) {
            throw "Compiled V4 type is missing: $required"
        }
    }
}
finally {
    Remove-Item -LiteralPath $decompileRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$hash = Get-FileHash -LiteralPath $dllPath -Algorithm SHA256
[pscustomobject]@{
    Version = $info.Version
    Assembly = $dllPath
    Size = (Get-Item -LiteralPath $dllPath).Length
    SHA256 = $hash.Hash
    Result = 'PASS'
} | Format-List
