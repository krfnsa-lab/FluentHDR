# Read-only Windows media-session helper, compatible with Windows PowerShell 5.1.
# Only Get-HdrMediaSessions emits data. Titles stay in memory and are not logged.
$script:HdrMediaAvailable = $false
$script:HrdMediaAvailable = $false # Compatibility alias for callers using the older spelling.
$script:HdrMediaError = ''
$script:HdrMediaManager = $null
$script:HdrMediaManagerTask = $null
$script:HdrMediaAsTaskMethod = $null
$script:HdrMediaPropertiesTaskMethod = $null
$script:HdrMediaPropertiesType = $null
$script:HdrMediaInitializationAttempt = [DateTime]::MinValue
$script:HdrMediaPropertyCache = @{}

function Initialize-HdrMedia {
    [CmdletBinding()]
    param([switch]$Force)

    if ($script:HdrMediaAvailable -and -not $Force) { return }
    $now = [DateTime]::UtcNow
    if (-not $Force -and ($now - $script:HdrMediaInitializationAttempt).TotalSeconds -lt 30) { return }
    $script:HdrMediaInitializationAttempt = $now
    $script:HdrMediaAvailable = $false
    $script:HrdMediaAvailable = $false
    try {
        Add-Type -AssemblyName System.Runtime.WindowsRuntime -ErrorAction Stop
        $managerType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media,ContentType=WindowsRuntime]
        $script:HdrMediaPropertiesType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties,Windows.Media,ContentType=WindowsRuntime]
        $script:HdrMediaAsTaskMethod = [System.WindowsRuntimeSystemExtensions].GetMethods() |
            Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } |
            Select-Object -First 1
        if ($null -eq $script:HdrMediaAsTaskMethod) { throw 'WinRT async task projection is unavailable.' }
        $script:HdrMediaPropertiesTaskMethod = $script:HdrMediaAsTaskMethod.MakeGenericMethod($script:HdrMediaPropertiesType)
        if ($Force -or $null -eq $script:HdrMediaManagerTask -or $script:HdrMediaManagerTask.IsFaulted -or $script:HdrMediaManagerTask.IsCanceled) {
            $script:HdrMediaManagerTask = $script:HdrMediaAsTaskMethod.MakeGenericMethod($managerType).Invoke($null, @($managerType::RequestAsync()))
        }
        if (-not $script:HdrMediaManagerTask.Wait(1500)) { throw 'Windows media session manager timed out (1500 ms).' }
        $script:HdrMediaManager = $script:HdrMediaManagerTask.Result
        if ($null -eq $script:HdrMediaManager) { throw 'Windows media session manager returned no manager.' }
        $script:HdrMediaAvailable = $true
        $script:HrdMediaAvailable = $true
        $script:HdrMediaError = ''
    }
    catch {
        $script:HdrMediaManager = $null
        $script:HdrMediaAvailable = $false
        $script:HrdMediaAvailable = $false
        $script:HdrMediaError = $_.Exception.Message
    }
}

function ConvertTo-HdrMediaPlaybackType {
    param($Value)
    if ($null -eq $Value) { return '' }
    # The .NET projection normally unwraps Nullable<MediaPlaybackType>; handle
    # an IReference/Nullable wrapper as well without assuming it has a Value.
    $wrappedValue = $Value.PSObject.Properties['Value']
    if ($null -ne $wrappedValue) { $Value = $wrappedValue.Value }
    switch ([string]$Value) {
        'Music' { return 'Music' }
        'Video' { return 'Video' }
        'Image' { return 'Image' }
        '1' { return 'Music' }
        '2' { return 'Video' }
        '3' { return 'Image' }
        default { return '' }
    }
}

function Test-HdrMediaBrowserApp {
    param([string]$App, [string]$BrowserProcess)
    if ([string]::IsNullOrWhiteSpace($BrowserProcess)) { return $true }
    $browser = [IO.Path]::GetFileNameWithoutExtension($BrowserProcess).ToLowerInvariant()
    if ($browser -eq 'edge') { $browser = 'msedge' }
    if ($browser -eq 'msedge' -and $App -match '(?i)^Microsoft\.MicrosoftEdge') { return $true }
    if ($browser -eq 'firefox' -and $App -match '(?i)^Mozilla\.Firefox') { return $true }
    if ($browser -eq 'chrome' -and $App -match '(?i)^Google\.Chrome') { return $true }
    $pattern = '(?i)(^|[\\/:!.\-_])' + [regex]::Escape($browser) + '(\.exe)?($|[\\/:!.\-_])'
    return [regex]::IsMatch($App, $pattern)
}

function Get-HdrMediaSessions {
    [CmdletBinding()]
    param(
        # Omit this argument to read all media sessions. Supplying the foreground
        # browser process limits results to that browser's published sessions.
        [string]$BrowserProcess = ''
    )
    if (-not $script:HdrMediaAvailable) { Initialize-HdrMedia }
    if (-not $script:HdrMediaAvailable -or $null -eq $script:HdrMediaManager) { return }

    $clock = [Diagnostics.Stopwatch]::StartNew()
    $now = [DateTime]::UtcNow
    $pendingRows = New-Object 'System.Collections.Generic.List[object]'
    try {
        $sessions = @($script:HdrMediaManager.GetSessions())
    }
    catch {
        $script:HdrMediaAvailable = $false
        $script:HrdMediaAvailable = $false
        $script:HdrMediaError = $_.Exception.Message
        $script:HdrMediaManager = $null
        $script:HdrMediaManagerTask = $null
        return
    }

    foreach ($session in $sessions) {
        try {
            $app = [string]$session.SourceAppUserModelId
            if (-not (Test-HdrMediaBrowserApp -App $app -BrowserProcess $BrowserProcess)) { continue }
            $playback = $session.GetPlaybackInfo()
            $status = [string]$playback.PlaybackStatus
            $playbackType = ConvertTo-HdrMediaPlaybackType $playback.PlaybackType
            $identity = [Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($session)
            $key = $app + '|' + [string]$identity
            if (-not $script:HdrMediaPropertyCache.ContainsKey($key)) {
                $script:HdrMediaPropertyCache[$key] = [pscustomobject]@{
                    Session = $session
                    Title = ''
                    PlaybackType = ''
                    Task = $null
                    RequestedAt = [DateTime]::MinValue
                    SeenAt = $now
                }
            }
            $entry = $script:HdrMediaPropertyCache[$key]
            $entry.SeenAt = $now
            # Keep outstanding requests rather than launching a new async request
            # on every monitor tick. A stalled request can be retried after 15 s.
            if ($null -ne $entry.Task -and -not $entry.Task.IsCompleted -and ($now - $entry.RequestedAt).TotalSeconds -ge 15) {
                $entry.Task = $null
            }
            if ($null -eq $entry.Task -and ($now - $entry.RequestedAt).TotalSeconds -ge 5) {
                $entry.RequestedAt = $now
                try {
                    $entry.Task = $script:HdrMediaPropertiesTaskMethod.Invoke($null, @($session.TryGetMediaPropertiesAsync()))
                }
                catch {
                    $entry.Task = $null
                    $script:HdrMediaError = $_.Exception.Message
                }
            }
            $pendingRows.Add([pscustomobject]@{
                App = $app
                Status = $status
                CurrentPlaybackType = $playbackType
                Entry = $entry
            })
        }
        catch {
            # A session can disappear between enumeration and reading. Skip it;
            # the manager and other sessions remain usable.
            $script:HdrMediaError = $_.Exception.Message
        }
    }

    foreach ($row in $pendingRows) {
        $entry = $row.Entry
        if ($null -ne $entry.Task) {
            try {
                # The entire poll has one 500 ms budget, not 500 ms per session.
                $remaining = [int][Math]::Max(0, 500 - $clock.ElapsedMilliseconds)
                if ($entry.Task.IsCompleted -or ($remaining -gt 0 -and $entry.Task.Wait($remaining))) {
                    $properties = $entry.Task.Result
                    if ($null -ne $properties) {
                        $entry.Title = [string]$properties.Title
                        $entry.PlaybackType = ConvertTo-HdrMediaPlaybackType $properties.PlaybackType
                    }
                    $entry.Task = $null
                }
            }
            catch {
                $entry.Task = $null
                $script:HdrMediaError = $_.Exception.Message
            }
        }
        # PlaybackInfo is current. Media-property type only fills in an omitted
        # current type; Unknown stays blank and is not guessed to be video.
        $type = $row.CurrentPlaybackType
        if ([string]::IsNullOrEmpty($type)) { $type = $entry.PlaybackType }
        [pscustomobject]@{
            App = $row.App
            Status = $row.Status
            Title = $entry.Title
            PlaybackType = $type
        }
    }

    foreach ($key in @($script:HdrMediaPropertyCache.Keys)) {
        if (($now - $script:HdrMediaPropertyCache[$key].SeenAt).TotalSeconds -gt 60) {
            $script:HdrMediaPropertyCache.Remove($key)
        }
    }
}
