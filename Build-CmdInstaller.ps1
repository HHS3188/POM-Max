param(
    [string]$ProjectRoot = $PSScriptRoot,
    [string]$OutputPath = (Join-Path $PSScriptRoot "release\POM-Max-3.0.0-一键安装.cmd")
)

$ErrorActionPreference = "Stop"

$dllPath = Join-Path $ProjectRoot "dist\POMMax.dll"
$infoPath = Join-Path $ProjectRoot "dist\Info.json"
if (-not (Test-Path -LiteralPath $dllPath) -or -not (Test-Path -LiteralPath $infoPath)) {
    throw "Missing dist payload. Run Build.ps1 first."
}

$dllBytes = [IO.File]::ReadAllBytes($dllPath)
$infoBytes = [IO.File]::ReadAllBytes($infoPath)
$info = [Text.Encoding]::UTF8.GetString($infoBytes) | ConvertFrom-Json

function Get-Sha256Hex([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return -join ($sha.ComputeHash($Bytes) | ForEach-Object { $_.ToString("X2") })
    }
    finally {
        $sha.Dispose()
    }
}

function Convert-ToWrappedBase64([byte[]]$Bytes) {
    $base64 = [Convert]::ToBase64String($Bytes)
    $builder = New-Object Text.StringBuilder
    for ($offset = 0; $offset -lt $base64.Length; $offset += 120) {
        $length = [Math]::Min(120, $base64.Length - $offset)
        [void]$builder.Append($base64.Substring($offset, $length))
        [void]$builder.Append("`r`n")
    }
    return $builder.ToString()
}

$dllSha256 = Get-Sha256Hex $dllBytes
$infoSha256 = Get-Sha256Hex $infoBytes
$version = [string]$info.Version

$installerScript = @'
param([Parameter(Mandatory = $true)][string]$ContainerPath)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Console]::InputEncoding = New-Object Text.UTF8Encoding($false)
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
$host.UI.RawUI.WindowTitle = "POM-Max 一键替换修复"

$ExpectedDllSha256 = "__DLL_SHA256__"
$ExpectedInfoSha256 = "__INFO_SHA256__"
$PayloadVersion = "__VERSION__"
$GameFolderName = "A Dance of Fire and Ice"
$AppId = "977950"

function Write-Step([string]$Message) {
    Write-Host ("[进行中] " + $Message) -ForegroundColor Cyan
}

function Write-Ok([string]$Message) {
    Write-Host ("[完成] " + $Message) -ForegroundColor Green
}

function Get-Sha256Hex([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return -join ($sha.ComputeHash($Bytes) | ForEach-Object { $_.ToString("X2") })
    }
    finally {
        $sha.Dispose()
    }
}

function Read-EmbeddedPayload([string]$Name) {
    $container = [IO.File]::ReadAllText($ContainerPath, [Text.Encoding]::ASCII)
    $escapedName = [Text.RegularExpressions.Regex]::Escape($Name)
    $pattern = "(?ms)^::POMMAX_" + $escapedName + "_BEGIN\r?\n(?<data>.*?)\r?\n::POMMAX_" + $escapedName + "_END(?=\r?\n|$)"
    $match = [Text.RegularExpressions.Regex]::Match($container, $pattern)
    if (-not $match.Success) {
        throw "安装脚本内置载荷缺失：$Name"
    }

    $base64 = $match.Groups["data"].Value -replace "\s", ""
    try {
        return [Convert]::FromBase64String($base64)
    }
    catch {
        throw "安装脚本内置载荷损坏：$Name"
    }
}

function Test-GameRoot([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    return (Test-Path -LiteralPath (Join-Path $Path "A Dance of Fire and Ice.exe") -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $Path "A Dance of Fire and Ice_Data") -PathType Container)
}

function Resolve-GameRoot([string]$SelectedPath) {
    if ([string]::IsNullOrWhiteSpace($SelectedPath)) {
        return $null
    }

    try {
        $full = [IO.Path]::GetFullPath($SelectedPath.Trim().Trim('"'))
    }
    catch {
        return $null
    }

    $candidates = @(
        $full,
        (Join-Path $full "steamapps\common\$GameFolderName"),
        (Join-Path $full "common\$GameFolderName"),
        (Join-Path $full $GameFolderName)
    )
    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-GameRoot $candidate) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    return $null
}

function Get-SteamRoots {
    $roots = New-Object "Collections.Generic.HashSet[string]" ([StringComparer]::OrdinalIgnoreCase)
    $registryValues = @(
        @("HKCU:\Software\Valve\Steam", "SteamPath"),
        @("HKLM:\Software\WOW6432Node\Valve\Steam", "InstallPath"),
        @("HKLM:\Software\Valve\Steam", "InstallPath")
    )
    foreach ($entry in $registryValues) {
        try {
            $value = (Get-ItemProperty -LiteralPath $entry[0] -Name $entry[1] -ErrorAction Stop).$($entry[1])
            if ($value -and (Test-Path -LiteralPath $value -PathType Container)) {
                [void]$roots.Add(([IO.Path]::GetFullPath($value)))
            }
        }
        catch {
        }
    }

    $fallbackRoots = New-Object Collections.Generic.List[string]
    if (${env:ProgramFiles(x86)}) {
        $fallbackRoots.Add((Join-Path ${env:ProgramFiles(x86)} "Steam"))
    }
    if ($env:ProgramFiles) {
        $fallbackRoots.Add((Join-Path $env:ProgramFiles "Steam"))
    }
    foreach ($fallback in $fallbackRoots) {
        if ($fallback -and (Test-Path -LiteralPath $fallback -PathType Container)) {
            [void]$roots.Add(([IO.Path]::GetFullPath($fallback)))
        }
    }

    return $roots
}

function Find-GameRoot {
    foreach ($localCandidate in @(
        (Split-Path -Parent $ContainerPath),
        (Get-Location).Path
    )) {
        $resolved = Resolve-GameRoot $localCandidate
        if ($resolved) {
            return $resolved
        }
    }

    $libraries = New-Object "Collections.Generic.HashSet[string]" ([StringComparer]::OrdinalIgnoreCase)
    foreach ($steamRoot in Get-SteamRoots) {
        [void]$libraries.Add($steamRoot)
        $vdfPath = Join-Path $steamRoot "steamapps\libraryfolders.vdf"
        if (-not (Test-Path -LiteralPath $vdfPath -PathType Leaf)) {
            continue
        }

        try {
            $vdfText = [IO.File]::ReadAllText($vdfPath)
            foreach ($match in [Text.RegularExpressions.Regex]::Matches($vdfText, '"path"\s*"([^"]+)"', "IgnoreCase")) {
                $library = $match.Groups[1].Value.Replace("\\", "\")
                if (Test-Path -LiteralPath $library -PathType Container) {
                    [void]$libraries.Add([IO.Path]::GetFullPath($library))
                }
            }
        }
        catch {
        }
    }

    foreach ($library in $libraries) {
        $manifestPath = Join-Path $library "steamapps\appmanifest_$AppId.acf"
        if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
            try {
                $manifest = [IO.File]::ReadAllText($manifestPath)
                $match = [Text.RegularExpressions.Regex]::Match($manifest, '"installdir"\s*"([^"]+)"', "IgnoreCase")
                if ($match.Success) {
                    $candidate = Join-Path $library ("steamapps\common\" + $match.Groups[1].Value)
                    if (Test-GameRoot $candidate) {
                        return [IO.Path]::GetFullPath($candidate)
                    }
                }
            }
            catch {
            }
        }

        $fallback = Join-Path $library "steamapps\common\$GameFolderName"
        if (Test-GameRoot $fallback) {
            return [IO.Path]::GetFullPath($fallback)
        }
    }

    return $null
}

function Select-GameRoot {
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = New-Object Windows.Forms.FolderBrowserDialog
        $dialog.Description = "未自动找到游戏。请选择 A Dance of Fire and Ice 游戏目录或 Steam 库目录。"
        $dialog.ShowNewFolderButton = $false
        if ($dialog.ShowDialog() -eq [Windows.Forms.DialogResult]::OK) {
            return Resolve-GameRoot $dialog.SelectedPath
        }
    }
    catch {
    }

    return $null
}

function Test-GameRunning([string]$GameRoot) {
    try {
        $targetExecutable = [IO.Path]::GetFullPath((Join-Path $GameRoot "A Dance of Fire and Ice.exe"))
        $processes = Get-CimInstance Win32_Process -Filter "Name='A Dance of Fire and Ice.exe'" -ErrorAction Stop
        foreach ($process in $processes) {
            if (-not [string]::IsNullOrWhiteSpace($process.ExecutablePath) -and
                [string]::Equals(
                    [IO.Path]::GetFullPath($process.ExecutablePath),
                    $targetExecutable,
                    [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
        return $false
    }
    catch {
        return @((Get-Process -Name "A Dance of Fire and Ice" -ErrorAction SilentlyContinue)).Count -gt 0
    }
}

function Test-IsAdministrator {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $false
    }
}

function Test-DirectoryWriteAccess([string]$Path) {
    $probe = Join-Path $Path (".pommax-write-test-" + [Guid]::NewGuid().ToString("N"))
    try {
        [IO.Directory]::CreateDirectory($Path) | Out-Null
        [IO.File]::WriteAllBytes($probe, [byte[]](1, 2, 3))
        return $true
    }
    catch {
        return $false
    }
    finally {
        if (Test-Path -LiteralPath $probe -PathType Leaf) {
            Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
        }
    }
}

function Restart-Elevated {
    Write-Host "[提示] 目标目录需要管理员权限，正在请求授权。" -ForegroundColor Yellow
    try {
        $env:POMMAX_NO_PAUSE = "1"
        $process = Start-Process -FilePath $ContainerPath -Verb RunAs -Wait -PassThru
        exit $process.ExitCode
    }
    catch {
        throw "未获得管理员权限，无法写入游戏目录。"
    }
}

function Write-Atomic([string]$TargetPath, [byte[]]$Bytes) {
    $parent = Split-Path -Parent $TargetPath
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $temporary = $TargetPath + ".pommax-new-" + [Guid]::NewGuid().ToString("N")
    $replacementBackup = $TargetPath + ".pommax-replace-backup-" + [Guid]::NewGuid().ToString("N")
    try {
        $stream = New-Object IO.FileStream(
            $temporary,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None,
            81920,
            [IO.FileOptions]::WriteThrough)
        try {
            $stream.Write($Bytes, 0, $Bytes.Length)
            $stream.Flush($true)
        }
        finally {
            $stream.Dispose()
        }

        if (Test-Path -LiteralPath $TargetPath -PathType Leaf) {
            [IO.File]::Replace($temporary, $TargetPath, $replacementBackup, $true)
        }
        else {
            [IO.File]::Move($temporary, $TargetPath)
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) {
            Remove-Item -LiteralPath $temporary -Force
        }
        if (Test-Path -LiteralPath $replacementBackup -PathType Leaf) {
            Remove-Item -LiteralPath $replacementBackup -Force
        }
    }
}

function Restore-Backup([string]$BackupDirectory, [string]$ModsDirectory, [object[]]$Records) {
    foreach ($record in $Records) {
        $relative = [string]$record.RelativePath
        $target = Join-Path $ModsDirectory $relative
        if ([bool]$record.Existed) {
            $source = Join-Path $BackupDirectory $relative
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "回滚文件缺失：$relative"
            }
            Write-Atomic $target ([IO.File]::ReadAllBytes($source))
        }
        elseif (Test-Path -LiteralPath $target -PathType Leaf) {
            Remove-Item -LiteralPath $target -Force
        }
    }
}

try {
    Clear-Host
    Write-Host "POM-Max 一键替换修复" -ForegroundColor White
    Write-Host "版本：$PayloadVersion" -ForegroundColor Gray
    Write-Host ""

    Write-Step "校验脚本内置文件"
    $dllBytes = Read-EmbeddedPayload "DLL"
    $infoBytes = Read-EmbeddedPayload "INFO"
    $dllSha256 = Get-Sha256Hex $dllBytes
    $infoSha256 = Get-Sha256Hex $infoBytes
    if ($dllSha256 -ne $ExpectedDllSha256 -or $infoSha256 -ne $ExpectedInfoSha256) {
        throw "内置文件 SHA-256 校验失败，请重新获取完整脚本。"
    }

    $infoText = New-Object Text.UTF8Encoding($false)
    $info = $infoText.GetString($infoBytes) | ConvertFrom-Json
    if ($info.Version -ne $PayloadVersion -or
        $info.AssemblyName -ne "POMMax.dll" -or
        $info.EntryMethod -ne "POMMax.Main.Load") {
        throw "内置 Info.json 清单与安装载荷不匹配。"
    }
    Write-Ok "内置文件完整"

    Write-Step "自动搜索 Steam 游戏目录"
    $gameRoot = Find-GameRoot
    if (-not $gameRoot) {
        Write-Host "[提示] 未能自动定位游戏，将打开目录选择窗口。" -ForegroundColor Yellow
        $gameRoot = Select-GameRoot
    }
    if (-not $gameRoot) {
        throw "未找到有效的 A Dance of Fire and Ice 游戏目录。"
    }

    $ummPath = Join-Path $gameRoot "A Dance of Fire and Ice_Data\Managed\UnityModManager\UnityModManager.dll"
    if (-not (Test-Path -LiteralPath $ummPath -PathType Leaf)) {
        throw "目标游戏未检测到 Unity Mod Manager，已停止安装。"
    }
    Write-Ok ("目标目录：" + $gameRoot)

    if (Test-GameRunning $gameRoot) {
        throw "游戏仍在运行。请先关闭游戏，再重新执行本脚本。"
    }

    $modsDirectory = Join-Path $gameRoot "Mods"
    if (-not (Test-DirectoryWriteAccess $modsDirectory)) {
        if (-not (Test-IsAdministrator)) {
            Restart-Elevated
        }
        throw "当前账户没有游戏 Mods 目录的写入权限。"
    }

    $modDirectory = Join-Path $modsDirectory "POMMax"
    [IO.Directory]::CreateDirectory($modsDirectory) | Out-Null
    [IO.Directory]::CreateDirectory($modDirectory) | Out-Null

    Write-Step "备份现有 POM-Max 核心文件"
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss_fff"
    $backupDirectory = Join-Path $modsDirectory ("_compat_backup_" + $stamp + "_before_POMMax_" + $PayloadVersion)
    [IO.Directory]::CreateDirectory($backupDirectory) | Out-Null

    $relativePaths = New-Object Collections.Generic.List[string]
    $relativePaths.Add("POMMax\POMMax.dll")
    $relativePaths.Add("POMMax\Info.json")
    foreach ($cache in Get-ChildItem -LiteralPath $modDirectory -Filter "POMMax.dll.*.cache" -File -ErrorAction SilentlyContinue) {
        $relativePaths.Add("POMMax\" + $cache.Name)
    }

    $records = New-Object Collections.Generic.List[object]
    foreach ($relative in $relativePaths | Select-Object -Unique) {
        $source = Join-Path $modsDirectory $relative
        $existed = Test-Path -LiteralPath $source -PathType Leaf
        $record = [pscustomobject]@{
            RelativePath = $relative
            Existed = $existed
            Sha256 = ""
        }
        if ($existed) {
            $bytes = [IO.File]::ReadAllBytes($source)
            $record.Sha256 = Get-Sha256Hex $bytes
            $backupFile = Join-Path $backupDirectory $relative
            [IO.Directory]::CreateDirectory((Split-Path -Parent $backupFile)) | Out-Null
            [IO.File]::WriteAllBytes($backupFile, $bytes)
        }
        $records.Add($record)
    }

    $manifest = [ordered]@{
        SchemaVersion = 1
        Purpose = "POM-Max CMD installer rollback"
        GameRoot = $gameRoot
        CreatedAtUtc = [DateTime]::UtcNow.ToString("O")
        PayloadVersion = $PayloadVersion
        Files = $records
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText(
        (Join-Path $backupDirectory "backup-manifest.json"),
        $manifestJson,
        (New-Object Text.UTF8Encoding($false)))
    Write-Ok ("备份目录：" + $backupDirectory)

    try {
        Write-Step "原子替换 Mod 核心文件"
        Write-Atomic (Join-Path $modDirectory "POMMax.dll") $dllBytes
        Write-Atomic (Join-Path $modDirectory "Info.json") $infoBytes

        foreach ($cache in Get-ChildItem -LiteralPath $modDirectory -Filter "POMMax.dll.*.cache" -File -ErrorAction SilentlyContinue) {
            Remove-Item -LiteralPath $cache.FullName -Force
        }

        $installedDllSha256 = Get-Sha256Hex ([IO.File]::ReadAllBytes((Join-Path $modDirectory "POMMax.dll")))
        $installedInfoSha256 = Get-Sha256Hex ([IO.File]::ReadAllBytes((Join-Path $modDirectory "Info.json")))
        if ($installedDllSha256 -ne $ExpectedDllSha256 -or $installedInfoSha256 -ne $ExpectedInfoSha256) {
            throw "安装后的文件哈希不匹配。"
        }
    }
    catch {
        Write-Host "[回滚] 安装失败，正在恢复原文件。" -ForegroundColor Yellow
        Restore-Backup $backupDirectory $modsDirectory $records
        throw
    }

    Write-Host ""
    Write-Ok "POM-Max 已更新到 $PayloadVersion"
    Write-Host ("安装位置：" + $modDirectory) -ForegroundColor White
    Write-Host "个人配置未被覆盖；旧程序集缓存已清理。" -ForegroundColor White
    Write-Host ("DLL SHA-256：" + $ExpectedDllSha256) -ForegroundColor DarkGray
    exit 0
}
catch {
    Write-Host ""
    Write-Host ("[失败] " + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
'@

$installerScript = $installerScript.
    Replace("__DLL_SHA256__", $dllSha256).
    Replace("__INFO_SHA256__", $infoSha256).
    Replace("__VERSION__", $version)

$scriptBase64 = Convert-ToWrappedBase64 ([Text.Encoding]::UTF8.GetBytes($installerScript))
$dllBase64 = Convert-ToWrappedBase64 $dllBytes
$infoBase64 = Convert-ToWrappedBase64 $infoBytes

$wrapper = @'
@echo off
setlocal EnableExtensions DisableDelayedExpansion
chcp 65001 >nul
title POM-Max One-Click Repair
set "POMMAX_CMD=%~f0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$f=$env:POMMAX_CMD;$t=[IO.File]::ReadAllText($f,[Text.Encoding]::ASCII);$m=[regex]::Match($t,'(?ms)^::POMMAX_SCRIPT_BEGIN\r?\n(?<data>.*?)\r?\n::POMMAX_SCRIPT_END(?=\r?\n|$)');if(-not $m.Success){throw 'Embedded script is missing.'};$s=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($m.Groups['data'].Value-replace'\s','')));&([ScriptBlock]::Create($s)) -ContainerPath $f"
set "POMMAX_EXIT=%ERRORLEVEL%"
echo.
if /I not "%POMMAX_NO_PAUSE%"=="1" pause
exit /b %POMMAX_EXIT%
::POMMAX_SCRIPT_BEGIN
'@

$content = $wrapper + "`r`n" +
    $scriptBase64 +
    "::POMMAX_SCRIPT_END`r`n" +
    "::POMMAX_DLL_BEGIN`r`n" +
    $dllBase64 +
    "::POMMAX_DLL_END`r`n" +
    "::POMMAX_INFO_BEGIN`r`n" +
    $infoBase64 +
    "::POMMAX_INFO_END`r`n"

$outputDirectory = Split-Path -Parent $OutputPath
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
[IO.File]::WriteAllText($OutputPath, $content, [Text.Encoding]::ASCII)

$outputBytes = [IO.File]::ReadAllBytes($OutputPath)
[pscustomobject]@{
    OutputPath = $OutputPath
    Version = $version
    SizeBytes = $outputBytes.Length
    DllSha256 = $dllSha256
    InfoSha256 = $infoSha256
    CmdSha256 = Get-Sha256Hex $outputBytes
}
