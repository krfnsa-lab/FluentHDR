# Pure classification rules. This file performs no display or system changes.
function Get-HdrProcessName {
    param([AllowNull()][string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return '' }
    $leaf = ($Name.Trim() -split '[\\/]')[-1]
    return ($leaf -replace '(?i)\.exe$', '').ToLowerInvariant()
}

function Test-HdrProcessList {
    param([string]$ProcessName, [AllowNull()]$Names)
    foreach ($entry in @($Names)) {
        if ($ProcessName -eq (Get-HdrProcessName ([string]$entry))) { return $true }
    }
    return $false
}

function ConvertTo-HdrNormalizedPath {
    param([AllowNull()][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return '' }
    $normalized = $Path.Trim().Trim('"').Replace('/', '\')
    try { $normalized = [System.IO.Path]::GetFullPath($normalized) } catch { return '' }
    return $normalized.TrimEnd('\').ToLowerInvariant()
}

function Test-HdrGameRoot {
    param([AllowNull()][string]$Path, $Config)
    $normalized = ConvertTo-HdrNormalizedPath $Path
    if (-not $normalized) { return $false }
    $segments = $normalized -split '\\'
    foreach ($excluded in @($Config.GameExcludedDirectoryNames)) {
        if ($segments -contains ([string]$excluded).ToLowerInvariant()) { return $false }
    }
    foreach ($root in @($Config.GameRoots)) {
        $normalizedRoot = ConvertTo-HdrNormalizedPath ([string]$root)
        if ($normalizedRoot -and $normalized.StartsWith($normalizedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function ConvertTo-HdrTitle {
    param([AllowNull()][string]$Title)
    if ([string]::IsNullOrWhiteSpace($Title)) { return '' }
    $clean = $Title.Normalize([Text.NormalizationForm]::FormKC) -replace '[\u200B-\u200F\uFEFF]', ''
    $clean = $clean -replace '(?i)\s+[-–—|]\s+(Microsoft Edge|Google Chrome|Mozilla Firefox|Brave|Vivaldi|Opera|Arc|Zen Browser)(\s.*)?$', ''
    return (($clean -replace '\s+', ' ').Trim()).ToLowerInvariant()
}

function Test-HdrMediaTitleMatch {
    param([AllowNull()][string]$ForegroundTitle, [AllowNull()][string]$MediaTitle)
    $foregroundText = ConvertTo-HdrTitle $ForegroundTitle
    $mediaText = ConvertTo-HdrTitle $MediaTitle
    if (-not $foregroundText -or -not $mediaText) { return $false }
    if ($foregroundText -eq $mediaText) { return $true }
    # Match the complete session title in the foreground tab. A site name alone
    # must not match a longer session title in an unrelated background tab.
    if ($mediaText.Length -lt 3) { return $false }
    return $foregroundText.IndexOf($mediaText, [StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Test-HdrBrowserSource {
    param([string]$ProcessName, [AllowNull()][string]$SourceApp, $Config)
    if ([string]::IsNullOrWhiteSpace($SourceApp)) { return $false }
    if ((Get-HdrProcessName $SourceApp) -eq $ProcessName) { return $true }
    $aliasProperty = $Config.BrowserSourceAliases.PSObject.Properties | Where-Object { $_.Name -eq $ProcessName } | Select-Object -First 1
    $aliases = @($ProcessName)
    if ($aliasProperty) { $aliases += @($aliasProperty.Value) }
    foreach ($alias in $aliases) {
        if ([string]::IsNullOrWhiteSpace([string]$alias)) { continue }
        $pattern = '(?i)(^|[.!/\\_-])' + [regex]::Escape([string]$alias) + '(\.exe)?($|[.!/\\_-])'
        if ($SourceApp -match $pattern) { return $true }
    }
    return $false
}

function New-HdrIntent {
    param([bool]$Enabled, [string]$Reason, [double]$HoldSeconds)
    return [pscustomobject]@{ Enabled = $Enabled; Reason = $Reason; HoldSeconds = $HoldSeconds }
}

function Resolve-HdrIntent {
    param(
        [Parameter(Mandatory = $true)]$Foreground,
        [AllowNull()][object[]]$Media = @(),
        [Parameter(Mandatory = $true)]$Config
    )
    $process = Get-HdrProcessName ([string]$Foreground.ProcessName)
    if (-not $process -and $Foreground.Path) { $process = Get-HdrProcessName ([string]$Foreground.Path) }
    $fastHold = [Math]::Max(0, [double]$Config.SwitchDelayMs / 1000.0)
    $defaultHold = [Math]::Max(0, [double]$Config.DefaultDelayMs / 1000.0)

    # Coding wins even if a browser or player has a background video session.
    if (Test-HdrProcessList $process $Config.EditorProcesses) {
        return New-HdrIntent $false ('editor:' + $process) $fastHold
    }
    if (Test-HdrProcessList $process $Config.MediaProcesses) {
        return New-HdrIntent $true ('media-app:' + $process) $fastHold
    }
    if (Test-HdrProcessList $process $Config.GameExcludedProcesses) {
        return New-HdrIntent $false ('utility:' + $process) $defaultHold
    }
    $pathSegments = (ConvertTo-HdrNormalizedPath ([string]$Foreground.Path)) -split '\\'
    foreach ($excluded in @($Config.GameExcludedDirectoryNames)) {
        if ($pathSegments -contains ([string]$excluded).ToLowerInvariant()) {
            return New-HdrIntent $false ('utility-directory:' + [string]$excluded) $defaultHold
        }
    }
    if (Test-HdrProcessList $process $Config.GameProcesses) {
        return New-HdrIntent $true ('game:' + $process) $fastHold
    }
    if (Test-HdrGameRoot ([string]$Foreground.Path) $Config) {
        return New-HdrIntent $true ('game-directory:' + $process) $fastHold
    }
    if (Test-HdrProcessList $process $Config.BrowserProcesses) {
        $matchingMusic = $false
        foreach ($session in @($Media)) {
            if (-not $session) { continue }
            if ([string]$session.Status -notin @('Playing', 'Paused')) { continue }
            if (-not (Test-HdrBrowserSource $process ([string]$session.App) $Config)) { continue }
            if (Test-HdrMediaTitleMatch ([string]$Foreground.Title) ([string]$session.Title)) {
                if ([string]$session.PlaybackType -eq 'Music') { $matchingMusic = $true; continue }
                return New-HdrIntent $true ('browser-video:' + $process) $fastHold
            }
        }
        if ($matchingMusic) { return New-HdrIntent $false ('browser-music:' + $process) $defaultHold }
        if ($Foreground.IsFullscreen) {
            foreach ($pattern in @($Config.VideoTitlePatterns)) {
                if ([string]$Foreground.Title -match [string]$pattern) {
                    return New-HdrIntent $true ('browser-fullscreen-video:' + $process) $fastHold
                }
            }
        }
    }
    return New-HdrIntent $false ('default:' + $process) $defaultHold
}
