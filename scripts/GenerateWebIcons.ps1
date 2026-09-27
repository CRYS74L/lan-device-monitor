$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'src\LanDeviceMonitor.App\Assets\SourceIcon.svg'
$destination = Join-Path $root 'src\LanDeviceMonitor.App\Web\icons'
[IO.Directory]::CreateDirectory($destination) | Out-Null
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
[xml]$svg = [IO.File]::ReadAllText($source)
$shapes = $svg.SelectNodes("//*[local-name()='path']")
# Redrawn from the editable vector source: full square opaque canvas and optical
# inset. Corner masking belongs to the OS. No screenshot measurements are used.
foreach ($size in @(1024,512,192,180,167,152,32)) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.PushTransform([Windows.Media.ScaleTransform]::new($size/1024.0,$size/1024.0))
    $background = [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString('#F9FBFF'))
    $context.DrawRectangle($background,$null,[Windows.Rect]::new(0,0,1024,1024))
    $context.PushTransform([Windows.Media.TranslateTransform]::new(61.44,61.44))
    $context.PushTransform([Windows.Media.ScaleTransform]::new(0.88,0.88))
    foreach($shape in $shapes) {
        $fill = [string]$shape.GetAttribute('fill')
        if(!$fill){continue}
        $color = switch($fill.ToUpperInvariant()) { '#6380FC' {'#1469E8'} '#7B94FE' {'#3887F5'} '#9AB1FF' {'#70AEFF'} default {$fill} }
        $brush = [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($color))
        $geometry = [Windows.Media.Geometry]::Parse([string]$shape.GetAttribute('d'))
        $context.DrawGeometry($brush,$null,$geometry)
    }
    $context.Pop();$context.Pop();$context.Pop();$context.Close()
    $image = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $image.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $opaque = [Windows.Media.Imaging.FormatConvertedBitmap]::new($image,[Windows.Media.PixelFormats]::Bgr24,$null,0)
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($opaque))
    $path = Join-Path $destination "network-v195-$size.png"
    $stream = [IO.File]::Create($path)
    try {$encoder.Save($stream)} finally {$stream.Dispose()}
}
Write-Output "Generated opaque web icons at: $destination"
