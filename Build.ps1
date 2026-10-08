$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$iconPath = Join-Path $PSScriptRoot 'HdrFluentTray.ico'
Add-Type -AssemblyName System.Drawing
$images = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $font = [System.Drawing.Font]::new('Segoe Fluent Icons',[single]($size*0.78),[System.Drawing.FontStyle]::Regular,[System.Drawing.GraphicsUnit]::Pixel)
    $foreground = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(72,114,219))
    $dot = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(145,114,237))
    $format = [System.Drawing.StringFormat]::new()
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $graphics.DrawString([string][char]0xE7F4,$font,$foreground,[System.Drawing.RectangleF]::new(0,0,$size,$size),$format)
    $graphics.FillEllipse($dot,[single]($size*.76),[single]($size*.76),[single]($size*.19),[single]($size*.19))
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $images += @{Size=$size;Bytes=$stream.ToArray()}
    $stream.Dispose(); $format.Dispose(); $dot.Dispose(); $foreground.Dispose(); $font.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16*$images.Count
    foreach ($image in $images) {
        $dimension = if ($image.Size -eq 256) {0} else {$image.Size}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length); $writer.Write([uint32]$offset); $offset += $image.Bytes.Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
$argsForCompiler = @('/nologo','/target:winexe','/platform:x64','/codepage:65001',
    ('/out:' + (Join-Path $PSScriptRoot 'HdrFluentTray.exe')),
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'HdrFluentTray.manifest')),
    ('/win32icon:' + $iconPath),
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll','/reference:Microsoft.CSharp.dll',
    ('/reference:' + (Join-Path $wpf 'WindowsBase.dll')),
    ('/reference:' + (Join-Path $wpf 'PresentationCore.dll')),
    ('/reference:' + (Join-Path $wpf 'PresentationFramework.dll')),
    ('/reference:' + (Join-Path $framework 'System.Xaml.dll')),
    (Join-Path $PSScriptRoot 'HdrFluentTray.cs'),(Join-Path $PSScriptRoot 'HdrDisplay.cs'))
& $compiler @argsForCompiler
if ($LASTEXITCODE -ne 0) { throw 'Fluent tray compilation failed.' }
Get-Item -LiteralPath (Join-Path $PSScriptRoot 'HdrFluentTray.exe') | Select-Object FullName,Length
