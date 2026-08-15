$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$installerSource = Join-Path $root 'release\POM-Max-4.0.0-一键安装.cmd'
$expectedDll = Join-Path $root 'dist\POMMax.dll'
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$testRoot = Join-Path $tempBase ('POMMaxV4ReleaseTest_' + [Guid]::NewGuid().ToString('N'))
$testRoot = [IO.Path]::GetFullPath($testRoot)
$windowsRoot = if ($env:WINDIR) { $env:WINDIR } else { $env:SystemRoot }
if (-not $windowsRoot) {
    $windowsRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
}

if (-not $testRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to create a test fixture outside the temporary directory.'
}

$testProcess = $null
$previousNoPause = $env:POMMAX_NO_PAUSE
try {
    $gameRoot = Join-Path $testRoot 'A Dance of Fire and Ice'
    $ummDirectory = Join-Path $gameRoot 'A Dance of Fire and Ice_Data\Managed\UnityModManager'
    $modDirectory = Join-Path $gameRoot 'Mods\POMMax'
    New-Item -ItemType Directory -Path $ummDirectory, $modDirectory -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $windowsRoot 'System32\PING.EXE') `
        -Destination (Join-Path $gameRoot 'A Dance of Fire and Ice.exe')
    [IO.File]::WriteAllBytes((Join-Path $ummDirectory 'UnityModManager.dll'), [byte[]](1))
    [IO.File]::WriteAllText((Join-Path $modDirectory 'Info.json'), 'old', [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText((Join-Path $modDirectory 'POMMax.dll'), 'old', [Text.Encoding]::ASCII)

    $settings = 'PRESERVE-ME'
    [IO.File]::WriteAllText((Join-Path $modDirectory 'Settings.xml'), $settings, [Text.Encoding]::ASCII)

    $installerPath = Join-Path $gameRoot 'POM-Max-4.0.0-一键安装.cmd'
    Copy-Item -LiteralPath $installerSource -Destination $installerPath

    $testProcess = Start-Process `
        -FilePath (Join-Path $gameRoot 'A Dance of Fire and Ice.exe') `
        -ArgumentList '-t', '127.0.0.1' `
        -WindowStyle Hidden `
        -PassThru
    Start-Sleep -Milliseconds 500

    $env:POMMAX_NO_PAUSE = '1'
    & $env:ComSpec /d /c ('"' + $installerPath + '"')
    $installerExitCode = $LASTEXITCODE
    Start-Sleep -Milliseconds 300

    $processStopped = $null -eq (Get-Process -Id $testProcess.Id -ErrorAction SilentlyContinue)
    $installedInfo = Get-Content -LiteralPath (Join-Path $modDirectory 'Info.json') -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $installedHash = (Get-FileHash -LiteralPath (Join-Path $modDirectory 'POMMax.dll') -Algorithm SHA256).Hash
    $expectedHash = (Get-FileHash -LiteralPath $expectedDll -Algorithm SHA256).Hash
    $settingsAfter = [IO.File]::ReadAllText((Join-Path $modDirectory 'Settings.xml'))
    $backupCount = @(
        Get-ChildItem -LiteralPath (Join-Path $gameRoot 'Mods') -Directory -Filter '_compat_backup_*'
    ).Count

    $result = [pscustomobject]@{
        ExitCode = $installerExitCode
        ProcessStopped = $processStopped
        Version = $installedInfo.Version
        DllHashMatch = $installedHash -eq $expectedHash
        SettingsPreserved = $settingsAfter -eq $settings
        BackupCount = $backupCount
    }
    $result | Format-List

    if ($installerExitCode -ne 0 -or
        -not $processStopped -or
        $installedInfo.Version -ne '4.0.0' -or
        $installedHash -ne $expectedHash -or
        $settingsAfter -ne $settings -or
        $backupCount -ne 1) {
        throw 'Final installer integration test failed.'
    }
}
finally {
    $env:POMMAX_NO_PAUSE = $previousNoPause
    if ($testProcess -and $null -ne (Get-Process -Id $testProcess.Id -ErrorAction SilentlyContinue)) {
        Stop-Process -Id $testProcess.Id -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $testRoot) {
        $cleanupPath = [IO.Path]::GetFullPath($testRoot)
        if (-not $cleanupPath.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove a test fixture outside the temporary directory.'
        }
        Remove-Item -LiteralPath $cleanupPath -Recurse -Force
    }
}
