$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseDirectory = Join-Path $root 'release'
$infoPath = Join-Path $root 'dist\Info.json'
$dllPath = Join-Path $root 'dist\POMMax.dll'

& (Join-Path $root 'Build.ps1')
& (Join-Path $root 'Verify.ps1')
& (Join-Path $root 'Build-CmdInstaller.ps1')

$info = Get-Content -LiteralPath $infoPath -Raw -Encoding UTF8 | ConvertFrom-Json
$version = [string]$info.Version
$zipPath = Join-Path $releaseDirectory ("POM-Max-$version.zip")
$cmdPath = Join-Path $releaseDirectory ("POM-Max-$version-一键安装.cmd")
$checksumsPath = Join-Path $releaseDirectory ("SHA256SUMS-$version.txt")

if (-not (Test-Path -LiteralPath $cmdPath -PathType Leaf)) {
    throw "Installer was not generated: $cmdPath"
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ('pommax-release-' + [Guid]::NewGuid().ToString('N'))
$stagingMod = Join-Path $stagingRoot 'POMMax'
$temporaryZip = Join-Path $releaseDirectory (".POM-Max-$version-" + [Guid]::NewGuid().ToString('N') + '.tmp.zip')

try {
    New-Item -ItemType Directory -Path $stagingMod -Force | Out-Null
    Copy-Item -LiteralPath $infoPath -Destination (Join-Path $stagingMod 'Info.json') -Force
    Copy-Item -LiteralPath $dllPath -Destination (Join-Path $stagingMod 'POMMax.dll') -Force

    Remove-Item -LiteralPath $temporaryZip -Force -ErrorAction SilentlyContinue
    Compress-Archive -LiteralPath $stagingMod -DestinationPath $temporaryZip -CompressionLevel Optimal
    Move-Item -LiteralPath $temporaryZip -Destination $zipPath -Force

    $assets = @($zipPath, $cmdPath)
    $checksumLines = foreach ($asset in $assets) {
        $hash = Get-FileHash -LiteralPath $asset -Algorithm SHA256
        '{0} *{1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $asset)
    }
    [IO.File]::WriteAllLines($checksumsPath, $checksumLines, (New-Object Text.UTF8Encoding($false)))

    Write-Host ''
    Write-Host "Release assets ready: $version" -ForegroundColor Green
    foreach ($asset in @($zipPath, $cmdPath, $checksumsPath)) {
        $item = Get-Item -LiteralPath $asset
        $hash = Get-FileHash -LiteralPath $asset -Algorithm SHA256
        Write-Host ("  {0}  {1} bytes  SHA256={2}" -f $item.Name, $item.Length, $hash.Hash)
    }
}
finally {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryZip -Force -ErrorAction SilentlyContinue
}
