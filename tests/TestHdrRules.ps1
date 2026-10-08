$ErrorActionPreference = 'Stop'
$rulesRoot = Join-Path $PSScriptRoot '..'
. (Join-Path $rulesRoot 'Rules.ps1')
$config = Get-Content -LiteralPath (Join-Path $rulesRoot 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$script:checked = 0
function Assert-HdrRule {
    param([string]$Label, [string]$Process, [bool]$Enabled, [double]$Hold, [string]$Path = '', [string]$Title = '', [bool]$Fullscreen = $false, [object[]]$Media = @(), [string]$ReasonPrefix = '')
    $foreground = [pscustomobject]@{ProcessName=$Process; Path=$Path; Title=$Title; IsFullscreen=$Fullscreen}
    $intent = Resolve-HdrIntent -Foreground $foreground -Media $Media -Config $config
    if ($intent.Enabled -ne $Enabled -or $intent.HoldSeconds -ne $Hold -or ($ReasonPrefix -and -not $intent.Reason.StartsWith($ReasonPrefix))) {
        throw ('FAIL ' + $Label + ': ' + ($intent | ConvertTo-Json -Compress))
    }
    $script:checked++
}
$videoSession = [pscustomobject]@{App='Microsoft.MSEdge_8wekyb3d8bbwe!App'; Status='Playing'; Title='显示器 HDR 测试电影'; PlaybackType='Video'}
$musicSession = [pscustomobject]@{App='msedge.exe'; Status='Playing'; Title='音乐精选'; PlaybackType='Music'}
foreach ($editor in @('codex.exe','Code','Cursor','devenv','WindowsTerminal','pwsh','cmd','powershell','notepad++','pycharm64','idea64','MATLAB')) {
    Assert-HdrRule -Label ('editor ' + $editor) -Process $editor -Enabled $false -Hold 2 -Media @($videoSession) -Title '显示器 HDR 测试电影' -ReasonPrefix 'editor:'
}
foreach ($player in @('PotPlayerMini64.exe','哔哩哔哩','Video.UI','Microsoft.Media.Player','vlc','mpv','mpc-hc64')) {
    Assert-HdrRule -Label ('player ' + $player) -Process $player -Enabled $true -Hold 2 -ReasonPrefix 'media-app:'
}
foreach ($game in @('bg3','bg3_dx11','ChainedTogether','ChainedTogether-Win64-Shipping','dontstarve_steam','dontstarve_steam_x64','hollow_knight','Overcooked2','REPO','League of Legends')) {
    Assert-HdrRule -Label ('game ' + $game) -Process $game -Enabled $true -Hold 2 -ReasonPrefix 'game:'
}
Assert-HdrRule -Label 'Editor takes priority inside game directory' -Process 'Code' -Enabled $false -Hold 2 -Path 'D:\SteamLibrary\steamapps\common\FutureGame\Code.exe'
Assert-HdrRule -Label 'New game installed in Steam library' -Process 'futuregame' -Enabled $true -Hold 2 -Path 'D:\SteamLibrary\steamapps\common\FutureGame\bin\futuregame.exe' -ReasonPrefix 'game-directory:'
Assert-HdrRule -Label 'Root normalization forward slash/case' -Process 'futuregame' -Enabled $true -Hold 2 -Path 'd:/STEAMLIBRARY/steamapps/common/FutureGame/futuregame.exe'
Assert-HdrRule -Label 'Root false positive prefix' -Process 'tool' -Enabled $false -Hold 8 -Path 'D:\SteamLibrary\steamapps\common-backup\tool.exe' -ReasonPrefix 'default:'
Assert-HdrRule -Label 'Root false positive sibling' -Process 'tool' -Enabled $false -Hold 8 -Path 'D:\SteamLibraryOther\steamapps\common\tool.exe'
Assert-HdrRule -Label 'Root traversal out of common' -Process 'tool' -Enabled $false -Hold 8 -Path 'D:\SteamLibrary\steamapps\common\..\tools\tool.exe'
Assert-HdrRule -Label 'Steam launcher' -Process 'steam' -Enabled $false -Hold 8 -ReasonPrefix 'utility:'
Assert-HdrRule -Label 'Steam helper' -Process 'steamwebhelper' -Enabled $false -Hold 8
Assert-HdrRule -Label 'Game launcher inside game root' -Process 'LariLauncher' -Enabled $false -Hold 8 -Path 'C:\Program Files (x86)\Steam\steamapps\common\Baldurs Gate 3\Launcher\LariLauncher.exe'
Assert-HdrRule -Label 'Anti-cheat inside game root' -Process 'ACE-Helper' -Enabled $false -Hold 8 -Path 'C:\Program Files (x86)\Riot Games\League of Legends\Game\AntiCheatExpert\ACE-Helper.exe'
Assert-HdrRule -Label 'Unknown utility in Wallpaper Engine root' -Process 'new-wallpaper-tool' -Enabled $false -Hold 8 -Path 'C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\new-wallpaper-tool.exe' -ReasonPrefix 'utility-directory:'
Assert-HdrRule -Label 'Unknown utility in Lossless Scaling root' -Process 'new-scaler-tool' -Enabled $false -Hold 8 -Path 'C:\Program Files (x86)\Steam\steamapps\common\Lossless Scaling\new-scaler-tool.exe'
Assert-HdrRule -Label 'Known game name inside excluded directory' -Process 'bg3' -Enabled $false -Hold 8 -Path 'D:\SteamLibrary\steamapps\common\wallpaper_engine\bg3.exe'
Assert-HdrRule -Label 'Known Wallpaper Engine process' -Process 'wallpaper64' -Enabled $false -Hold 8
Assert-HdrRule -Label 'Known Lossless Scaling process' -Process 'LosslessScaling' -Enabled $false -Hold 8
Assert-HdrRule -Label 'Foreground Edge video playing' -Process 'msedge' -Enabled $true -Hold 2 -Title '显示器 HDR 测试电影 - YouTube - Microsoft Edge' -Media @($videoSession) -ReasonPrefix 'browser-video:'
$pausedSession = [pscustomobject]@{App='msedge.exe'; Status='Paused'; Title='显示器 HDR 测试电影'; PlaybackType='Video'}
Assert-HdrRule -Label 'Foreground Edge video paused' -Process 'msedge' -Enabled $true -Hold 2 -Title '显示器 HDR 测试电影 - 哔哩哔哩' -Media @($pausedSession)
Assert-HdrRule -Label 'Background Edge video unrelated foreground tab' -Process 'msedge' -Enabled $false -Hold 8 -Title 'Pull requests - GitHub - Microsoft Edge' -Media @($videoSession) -ReasonPrefix 'default:'
Assert-HdrRule -Label 'Chrome session cannot match Edge' -Process 'msedge' -Enabled $false -Hold 8 -Title '显示器 HDR 测试电影' -Media @([pscustomobject]@{App='chrome.exe'; Status='Playing'; Title='显示器 HDR 测试电影'; PlaybackType='Video'})
Assert-HdrRule -Label 'Background music ignored' -Process 'msedge' -Enabled $false -Hold 8 -Title '音乐精选 - YouTube Music' -Media @($musicSession)
Assert-HdrRule -Label 'Fullscreen matching music prevents video-site fallback' -Process 'msedge' -Enabled $false -Hold 8 -Title '音乐精选 - YouTube Music' -Fullscreen $true -Media @($musicSession) -ReasonPrefix 'browser-music:'
Assert-HdrRule -Label 'Background music cannot suppress unrelated fullscreen video fallback' -Process 'msedge' -Enabled $true -Hold 2 -Title 'HDR电影 - 哔哩哔哩' -Fullscreen $true -Media @($musicSession)
Assert-HdrRule -Label 'Stopped video ignored' -Process 'msedge' -Enabled $false -Hold 8 -Title '显示器 HDR 测试电影' -Media @([pscustomobject]@{App='msedge.exe'; Status='Stopped'; Title='显示器 HDR 测试电影'; PlaybackType='Video'})
Assert-HdrRule -Label 'Blank media title ignored' -Process 'msedge' -Enabled $false -Hold 8 -Title 'YouTube - Microsoft Edge' -Media @([pscustomobject]@{App='msedge.exe'; Status='Playing'; Title=''; PlaybackType='Video'})
Assert-HdrRule -Label 'Site homepage is not correlated to background title' -Process 'msedge' -Enabled $false -Hold 8 -Title 'YouTube - Microsoft Edge' -Media @([pscustomobject]@{App='msedge.exe'; Status='Playing'; Title='Best YouTube movie'; PlaybackType='Video'})
Assert-HdrRule -Label 'Fullscreen video site fallback' -Process 'msedge' -Enabled $true -Hold 2 -Title 'HDR电影 - 哔哩哔哩 - Microsoft Edge' -Fullscreen $true -ReasonPrefix 'browser-fullscreen-video:'
Assert-HdrRule -Label 'Windowed video site no sessions' -Process 'msedge' -Enabled $false -Hold 8 -Title '哔哩哔哩 - Microsoft Edge'
Assert-HdrRule -Label 'Fullscreen ordinary browser page' -Process 'msedge' -Enabled $false -Hold 8 -Title 'Pull requests - GitHub' -Fullscreen $true
Assert-HdrRule -Label 'Chrome alias video' -Process 'chrome' -Enabled $true -Hold 2 -Title 'Test Movie - YouTube - Google Chrome' -Media @([pscustomobject]@{App='Google.Chrome'; Status='Playing'; Title='Test Movie'; PlaybackType='Video'})
Assert-HdrRule -Label 'Firefox exact source video' -Process 'firefox' -Enabled $true -Hold 2 -Title 'Test Movie - Mozilla Firefox' -Media @([pscustomobject]@{App='firefox.exe'; Status='Paused'; Title='Test Movie'; PlaybackType='Video'})
Assert-HdrRule -Label 'Empty foreground process defaults SDR' -Process '' -Enabled $false -Hold 8
Assert-HdrRule -Label 'Fallback process from path' -Process '' -Enabled $false -Hold 2 -Path 'C:\software\Microsoft VS Code\Code.exe' -ReasonPrefix 'editor:'
Assert-HdrRule -Label 'Desktop returns SDR with longer delay' -Process 'explorer' -Enabled $false -Hold 8
if ($config.PollMs -ne 1000 -or $config.SwitchDelayMs -ne 2000 -or $config.DefaultDelayMs -ne 8000 -or @($config.TargetDisplays).Count -ne 0) { throw 'Default config values incorrect' }
Write-Output ('PASS: ' + $script:checked + ' pure HDR rule cases, default configuration validated; no display state changes.')
