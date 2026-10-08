param([switch]$ResetRules,[switch]$KeepMode)
$ErrorActionPreference = 'Stop'
$sourcePath = $PSScriptRoot
$statePath = Join-Path $env:LOCALAPPDATA 'CodexHdrSwitch'
$appPath = Join-Path $statePath 'App'
$backupPath = Join-Path $statePath 'previous-shortcuts'
$stopFile = Join-Path $statePath 'stop.request'
$manualFile = Join-Path $statePath 'manual.json'
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'HDR 自动切换.lnk'
$menuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'HDR 显示模式.lnk'
$sourceExe = Join-Path $sourcePath 'HdrFluentTray.exe'
$targetExe = Join-Path $appPath 'HdrFluentTray.exe'
$files = @('HdrFluentTray.exe','HdrFluentTray.ico','HdrSwitch.ps1','HdrDisplay.cs','Foreground.cs','Rules.ps1','Media.ps1','config.json')
if (-not (Test-Path -LiteralPath $sourceExe)) { & (Join-Path $sourcePath 'Build.ps1') }
foreach ($file in $files) { if (-not (Test-Path -LiteralPath (Join-Path $sourcePath $file))) { throw ('Missing runtime file: ' + $file) } }
if ([IO.Path]::GetFullPath($sourcePath) -eq [IO.Path]::GetFullPath($appPath)) { throw 'Run Setup.ps1 from the delivered package, outside the App folder.' }
$null = New-Item -ItemType Directory -Path $statePath,$appPath,$backupPath -Force

# Gracefully stop an earlier copy before replacing its executable or scripts.
Start-Process -FilePath $sourceExe -ArgumentList '--quit' -WindowStyle Hidden -Wait
Set-Content -LiteralPath $stopFile -Value 'stop' -Encoding ASCII
$deadline = (Get-Date).AddSeconds(12)
do {
    $existingTray = @(Get-Process -Name HdrFluentTray -ErrorAction SilentlyContinue | Where-Object { $_.Path -in @($sourceExe,$targetExe) })
    $workerRunning = $false
    try {
        $savedState = Get-Content -LiteralPath (Join-Path $statePath 'status.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($savedState.Running -and $savedState.ProcessId) {
            $workerProcess = Get-Process -Id $savedState.ProcessId -ErrorAction SilentlyContinue
            $workerRunning = $null -ne $workerProcess -and $workerProcess.ProcessName -eq 'powershell'
        }
    } catch { }
    if (-not $workerRunning -and $existingTray.Count -eq 0) { break }
    Start-Sleep -Milliseconds 400
} while ((Get-Date) -lt $deadline)
if ($workerRunning -or $existingTray.Count -gt 0) { throw 'The previous display-mode tool is still exiting. Retry Setup.ps1 in a few seconds.' }

$shell = New-Object -ComObject WScript.Shell
$oldScript = Join-Path $sourcePath 'HdrSwitch.ps1'
$oldNames = @('HDR 自动切换','SDR 写代码','HDR 看视频和游戏','关闭 HDR 自动切换')
foreach ($name in $oldNames) {
    $oldLink = Join-Path ([Environment]::GetFolderPath('Desktop')) ($name + '.lnk')
    if (-not (Test-Path -LiteralPath $oldLink)) { continue }
    $oldShortcut = $shell.CreateShortcut($oldLink)
    if ($oldShortcut.Arguments.IndexOf($oldScript,[StringComparison]::OrdinalIgnoreCase) -lt 0) { continue }
    $archiveLink = Join-Path $backupPath ($name + '-' + (Get-Date -Format 'yyyyMMddHHmmss') + '.lnk')
    if (-not [IO.Path]::GetFullPath($archiveLink).StartsWith([IO.Path]::GetFullPath($backupPath) + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid archive path.' }
    Move-Item -LiteralPath $oldLink -Destination $archiveLink
}
if (Test-Path -LiteralPath $startupLink) {
    $previousStartup = $shell.CreateShortcut($startupLink)
    $ours = $previousStartup.Arguments.IndexOf($oldScript,[StringComparison]::OrdinalIgnoreCase) -ge 0 -or $previousStartup.TargetPath -in @($sourceExe,$targetExe)
    if (-not $ours) { throw 'The existing startup shortcut belongs to another tool.' }
    $archiveStartup = Join-Path $backupPath ('登录启动-' + (Get-Date -Format 'yyyyMMddHHmmss') + '.lnk')
    Move-Item -LiteralPath $startupLink -Destination $archiveStartup
}
foreach ($file in $files) {
    $destination = Join-Path $appPath $file
    if ($file -eq 'config.json' -and (Test-Path -LiteralPath $destination) -and -not $ResetRules) { continue }
    Copy-Item -LiteralPath (Join-Path $sourcePath $file) -Destination $destination -Force
}
foreach ($link in @(@{Path=$startupLink;Arguments='--startup'},@{Path=$menuLink;Arguments=''})) {
    $shortcut = $shell.CreateShortcut($link.Path)
    $shortcut.TargetPath = $targetExe
    $shortcut.Arguments = $link.Arguments
    $shortcut.WorkingDirectory = $appPath
    $shortcut.Description = 'Fluent 显示模式：代码 SDR，视频与游戏 HDR'
    $shortcut.IconLocation = $targetExe + ',0'
    $shortcut.Save()
}
if (-not $KeepMode -and (Test-Path -LiteralPath $manualFile)) { Remove-Item -LiteralPath $manualFile }
if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile }
Start-Process -FilePath $targetExe -WindowStyle Hidden
[pscustomobject]@{InstalledAt=$appPath;StartMenu=$menuLink;Startup=$startupLink;OldShortcutsArchivedAt=$backupPath;ModePreserved=[bool]$KeepMode} | ConvertTo-Json
