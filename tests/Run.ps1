$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'TestHdrRules.ps1')
if (-not (Test-Path -LiteralPath (Join-Path $repo 'HdrFluentTray.exe'))) { & (Join-Path $repo 'Build.ps1') }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$bin = Join-Path $PSScriptRoot 'bin'
$null = New-Item -ItemType Directory -Path $bin -Force
$arguments = @('/nologo','/target:exe','/platform:x64','/codepage:65001',
    ('/out:' + (Join-Path $bin 'TestUi.exe')),
    ('/reference:' + (Join-Path $repo 'HdrFluentTray.exe')),
    ('/reference:' + (Join-Path $framework 'WPF\WindowsBase.dll')),
    ('/reference:' + (Join-Path $framework 'WPF\PresentationCore.dll')),
    ('/reference:' + (Join-Path $framework 'WPF\PresentationFramework.dll')),
    ('/reference:' + (Join-Path $framework 'System.Xaml.dll')),
    (Join-Path $PSScriptRoot 'TestUi.cs'))
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'UI regression test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $repo 'HdrFluentTray.exe') -Destination (Join-Path $bin 'HdrFluentTray.exe') -Force
& (Join-Path $bin 'TestUi.exe') $repo
if ($LASTEXITCODE -ne 0) { throw 'UI regression test failed.' }
Write-Output 'All tests passed. Tests do not change HDR or start the watcher.'
