param([ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Version='v0.1.0',[switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1'); & (Join-Path $PSScriptRoot 'tests\Run.ps1') }
$dist = Join-Path $PSScriptRoot 'dist'
$stage = Join-Path $PSScriptRoot ('build\package-' + [guid]::NewGuid().ToString('N') + '\FluentHDR')
$null = New-Item -ItemType Directory -Path $dist,$stage -Force
$files = @('HdrFluentTray.exe','HdrFluentTray.ico','HdrFluentTray.cs','HdrFluentTray.manifest','HdrDisplay.cs','Foreground.cs','HdrSwitch.ps1','Media.ps1','Rules.ps1','Build.ps1','Setup.ps1','Install.cmd','config.json','README.md','CHANGELOG.md',
    'assets\tray-dark.png','assets\settings-dark.png','tests\Run.ps1','tests\TestHdrRules.ps1','tests\TestUi.cs')
foreach ($file in $files) {
    $destination = Join-Path $stage $file
    $null = New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $destination
}
$archive = Join-Path $dist ('FluentHDR-' + $Version + '-win-x64.zip')
Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal -Force
$checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive)
[IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'),$checksum + [Environment]::NewLine,[Text.UTF8Encoding]::new($false))
Get-Item -LiteralPath $archive | Select-Object FullName,Length
