param(
    [ValidateSet('Auto','SDR','HDR','Stop','Status')][string]$Mode = 'Auto',
    [switch]$Startup
)
$ErrorActionPreference = 'Stop'
$installPath = $PSScriptRoot
$statePath = Join-Path $env:LOCALAPPDATA 'CodexHdrSwitch'
$null = New-Item -ItemType Directory -Path $statePath -Force
$overrideFile = Join-Path $statePath 'manual.json'
$stopFile = Join-Path $statePath 'stop.request'
$statusFile = Join-Path $statePath 'status.json'
$logFile = Join-Path $statePath 'switch.log'
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'HDR 自动切换.lnk'
$config = Get-Content -LiteralPath (Join-Path $installPath 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Add-Type -Path (Join-Path $installPath 'HdrDisplay.cs')

function Write-HdrLog([string]$message) {
    try {
        if ((Test-Path -LiteralPath $logFile) -and (Get-Item -LiteralPath $logFile).Length -gt 262144) {
            Move-Item -LiteralPath $logFile -Destination (Join-Path $statePath 'switch.previous.log') -Force
        }
        Add-Content -LiteralPath $logFile -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $message) -Encoding UTF8
    } catch { }
}
function Write-HdrState($value) {
    $temp = Join-Path $statePath ('status.' + $PID + '.tmp')
    $value | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $statusFile -Force
}
function Set-HdrMode([bool]$enabled) {
    $gate = New-Object System.Threading.Mutex($false, 'Local\CodexHdrSwitch.DisplaySet')
    $owned = $false
    try {
        try { $owned = $gate.WaitOne(10000) } catch [System.Threading.AbandonedMutexException] { $owned = $true }
        if (-not $owned) { throw 'Another display switch did not finish in time.' }
        $result = @([HdrDisplay]::Set($enabled, [string[]]@($config.TargetDisplays)))
        if ($result.Count -eq 0) { throw 'No active HDR-capable display was found.' }
        $failed = @($result | Where-Object { -not $_.Verified })
        if ($failed.Count -gt 0) { throw (($failed | ForEach-Object { $_.Name + ': ' + $_.Error + ' (status ' + $_.Status + ')' }) -join '; ') }
        return $result
    } finally { if ($owned) { $gate.ReleaseMutex() }; $gate.Dispose() }
}

if ($Mode -eq 'Status') {
    $saved = $null
    if (Test-Path -LiteralPath $statusFile) { try { $saved = Get-Content -LiteralPath $statusFile -Raw -Encoding UTF8 | ConvertFrom-Json } catch { } }
    [pscustomobject]@{ Display=@([HdrDisplay]::Enumerate()); Worker=$saved; StartupEnabled=(Test-Path -LiteralPath $startupLink); ManualOverride=(Test-Path -LiteralPath $overrideFile) } | ConvertTo-Json -Depth 8
    exit
}
if ($Mode -in @('SDR','HDR')) {
    $enabled = $Mode -eq 'HDR'
    [pscustomobject]@{Enabled=$enabled; Updated=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $overrideFile -Encoding UTF8
    $result = Set-HdrMode $enabled
    Write-HdrLog ('manual ' + $Mode)
    $result | ConvertTo-Json -Depth 4
    exit
}
if ($Mode -eq 'Stop') {
    Set-Content -LiteralPath $stopFile -Value 'stop' -Encoding ASCII
    if (Test-Path -LiteralPath $startupLink) { Remove-Item -LiteralPath $startupLink -Force }
    Start-Sleep -Milliseconds 1500
    $result = Set-HdrMode $false
    Write-HdrState ([pscustomobject]@{Running=$false; Updated=(Get-Date).ToString('o'); Mode='SDR'; Reason='disabled'; ProcessId=$null})
    Write-HdrLog 'automatic switching disabled; SDR restored'
    $result | ConvertTo-Json -Depth 4
    exit
}

# Explicitly choosing Auto cancels a manual override. No global execution-policy change.
if (-not $Startup -and (Test-Path -LiteralPath $overrideFile)) { Remove-Item -LiteralPath $overrideFile -Force }
if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile -Force }
if (-not (Test-Path -LiteralPath $startupLink) -and -not (Test-Path -LiteralPath (Join-Path $installPath 'HdrFluentTray.exe'))) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startupLink)
    $shortcut.TargetPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $shortcut.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $PSCommandPath + '" -Mode Auto -Startup'
    $shortcut.WorkingDirectory = $installPath
    $shortcut.Description = 'SDR for coding, HDR for video and games'
    $shortcut.WindowStyle = 7
    $shortcut.Save()
}
$owner = New-Object System.Threading.Mutex($false, 'Local\CodexHdrSwitch.Auto.Owner')
$owned = $false
try { $owned = $owner.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $owned = $true }
if (-not $owned) { $owner.Dispose(); exit }
$exitReason = 'stopped'
try {
    Add-Type -Path (Join-Path $installPath 'Foreground.cs')
    . (Join-Path $installPath 'Rules.ps1')
    . (Join-Path $installPath 'Media.ps1')
    Initialize-HdrMedia | Out-Null
    Write-HdrLog 'automatic switching started'
    $initialEnabled = $false
    $initialError = $null
    $initialMixed = $false
    try {
        if (Test-Path -LiteralPath $overrideFile) {
            $initialOverride = Get-Content -LiteralPath $overrideFile -Raw -Encoding UTF8 | ConvertFrom-Json
            $initialEnabled = [bool]$initialOverride.Enabled
            $null = Set-HdrMode $initialEnabled
        } else {
            # Auto starts from the actual display state and observes the chosen delay.
            $targetSelectors = [string[]]@($config.TargetDisplays)
            $initialDisplays = @([HdrDisplay]::Enumerate() | Where-Object {
                $_.QueryStatus -eq 0 -and $_.HdrSupported -and (
                    $targetSelectors.Count -eq 0 -or $targetSelectors -contains $_.Name -or
                    $targetSelectors -contains $_.DeviceName -or $targetSelectors -contains $_.MonitorDevicePath -or
                    $targetSelectors -contains $_.Key
                )
            })
            if ($initialDisplays.Count -eq 0) { throw 'No active HDR-capable display was found.' }
            $hdrCount = @($initialDisplays | Where-Object { $_.ActiveHdr }).Count
            $initialEnabled = $hdrCount -eq $initialDisplays.Count
            $initialMixed = $hdrCount -gt 0 -and $hdrCount -lt $initialDisplays.Count
        }
    } catch {
        $initialError = $_.Exception.Message
        Write-HdrLog ('waiting for display: ' + $initialError)
    }
    $pendingMode = $null
    $pendingPolicy = $null
    $pendingSince = Get-Date
    $lastVerify = if ($initialError -or $initialMixed) { [datetime]::MinValue } else { Get-Date }
    $lastMode = $initialEnabled
    $lastMedia = @()
    $mediaStamp = [datetime]::MinValue
    $lastReason = ''
    $lastStatus = [datetime]::MinValue
    $lastSuccess = -not [bool]$initialError
    $lastError = $initialError
    $configFile = Join-Path $installPath 'config.json'
    $configStamp = (Get-Item -LiteralPath $configFile).LastWriteTimeUtc
    $lastExternalForeground = $null
    while (-not (Test-Path -LiteralPath $stopFile)) {
        $now = Get-Date
        try {
            $newConfigStamp = (Get-Item -LiteralPath $configFile).LastWriteTimeUtc
            if ($newConfigStamp -ne $configStamp) {
                $config = Get-Content -LiteralPath $configFile -Raw -Encoding UTF8 | ConvertFrom-Json
                $configStamp = $newConfigStamp
                # New rules or delay settings begin a fresh observation period.
                $pendingMode = $null
                $pendingPolicy = $null
                Write-HdrLog 'application rules and delays reloaded'
            }
            $foreground = [HdrForeground]::Read()
            if ($foreground.ProcessName -eq 'HdrFluentTray') {
                if ($null -ne $lastExternalForeground) { $foreground = $lastExternalForeground }
            } else { $lastExternalForeground = $foreground }
            $process = $foreground.ProcessName
            $intent = $null
            if (Test-Path -LiteralPath $overrideFile) {
                $manual = Get-Content -LiteralPath $overrideFile -Raw -Encoding UTF8 | ConvertFrom-Json
                $intent = [pscustomobject]@{Enabled=[bool]$manual.Enabled; Reason='manual'; HoldSeconds=0}
            } else {
                $isBrowser = Test-HdrProcessList (Get-HdrProcessName $process) $config.BrowserProcesses
                if ($isBrowser -and ($now - $mediaStamp).TotalSeconds -ge 2) {
                    $lastMedia = @(Get-HdrMediaSessions)
                    $mediaStamp = Get-Date
                }
                $intent = Resolve-HdrIntent -Foreground $foreground -Media $lastMedia -Config $config
            }
            $policy = if ($intent.Reason -eq 'manual') { 'manual' }
                elseif ($intent.Reason -match '^(editor|media-app|game|game-directory|browser-video|browser-fullscreen-video):') { 'recognized' }
                else { 'default' }
            if ($null -eq $pendingMode -or $pendingMode -ne [bool]$intent.Enabled -or $pendingPolicy -ne $policy) {
                $pendingMode = [bool]$intent.Enabled
                $pendingPolicy = $policy
                $pendingSince = $now
            }
            $ready = ($now - $pendingSince).TotalSeconds -ge [double]$intent.HoldSeconds
            # Recheck every 15 seconds for newly connected displays or another app changing HDR.
            $verifyDue = ($now - $lastVerify).TotalSeconds -ge 15
            if ($ready -and ($lastMode -ne $pendingMode -or $verifyDue)) {
                $result = @(Set-HdrMode $pendingMode)
                $lastVerify = Get-Date
                $lastSuccess = $true
                $lastError = $null
                $lastMode = $pendingMode
                if (@($result | Where-Object {$_.Changed}).Count -gt 0) {
                    Write-HdrLog ((@('SDR','HDR')[[int]$lastMode]) + ' reason=' + $intent.Reason + ' process=' + $process)
                }
            }
            if (($now - $lastStatus).TotalSeconds -ge 5 -or $lastReason -ne $intent.Reason) {
                Write-HdrState ([pscustomobject]@{
                    Running=$true; ProcessId=$PID; Updated=(Get-Date).ToString('o'); Mode=(@('SDR','HDR')[[int]$lastMode]);
                    DesiredMode=(@('SDR','HDR')[[int]$pendingMode]); Reason=$intent.Reason; ForegroundProcess=$process;
                    MediaDetectionAvailable=$script:HdrMediaAvailable; LastSwitchSucceeded=$lastSuccess; LastError=$lastError
                })
                $lastStatus = $now
                $lastReason = $intent.Reason
            }
        } catch {
            $lastSuccess = $false
            $lastError = $_.Exception.Message
            Write-HdrLog ('error: ' + $lastError)
            try {
                Write-HdrState ([pscustomobject]@{Running=$true; ProcessId=$PID; Updated=(Get-Date).ToString('o'); LastSwitchSucceeded=$false; LastError=$lastError})
            } catch { }
            Start-Sleep -Seconds 4
        }
        Start-Sleep -Milliseconds ([int]$config.PollMs)
    }
} catch {
    $exitReason = $_.Exception.Message
    Write-HdrLog ('worker stopped: ' + $exitReason)
} finally {
    try { Write-HdrState ([pscustomobject]@{Running=$false; ProcessId=$PID; Updated=(Get-Date).ToString('o'); Reason=$exitReason}) } catch { }
    if ($owned) { $owner.ReleaseMutex() }; $owner.Dispose()
}
