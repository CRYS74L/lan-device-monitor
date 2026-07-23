param(
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $projectRoot 'src\LanDeviceMonitor.App\Assets\AppIcon.ico'
}

$outputDirectory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$pngOutputPath = [System.IO.Path]::ChangeExtension($OutputPath, '.png')

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Drawing

$lobePath = @'
M128,18
C187,18 233,64 238,122
C242,171 216,216 173,238
C193,204 192,167 170,139
C158,123 143,112 126,105
C143,89 153,70 153,49
C153,34 144,22 128,18 Z
'@

$highlightPath = @'
M137,27
C150,39 155,57 150,75
C147,86 141,95 134,102
'@

function New-GradientBrush {
    param(
        [string]$StartColor,
        [string]$MiddleColor,
        [string]$EndColor
    )

    $brush = [System.Windows.Media.LinearGradientBrush]::new()
    $brush.StartPoint = [System.Windows.Point]::new(0.28, 0.08)
    $brush.EndPoint = [System.Windows.Point]::new(0.82, 0.9)
    $brush.GradientStops.Add(
        [System.Windows.Media.GradientStop]::new(
            [System.Windows.Media.ColorConverter]::ConvertFromString($StartColor),
            0))
    $brush.GradientStops.Add(
        [System.Windows.Media.GradientStop]::new(
            [System.Windows.Media.ColorConverter]::ConvertFromString($MiddleColor),
            0.5))
    $brush.GradientStops.Add(
        [System.Windows.Media.GradientStop]::new(
            [System.Windows.Media.ColorConverter]::ConvertFromString($EndColor),
            1))
    $brush.Freeze()
    return $brush
}

function New-RotatedGeometry {
    param(
        [string]$PathData,
        [double]$Angle
    )

    $geometry = [System.Windows.Media.Geometry]::Parse($PathData).Clone()
    if ([Math]::Abs($Angle) -gt 0.001) {
        $geometry.Transform =
            [System.Windows.Media.RotateTransform]::new($Angle, 128, 128)
    }
    $geometry.Freeze()
    return $geometry
}

function Get-VisibleInkBounds {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [byte]$AlphaThreshold = 8
    )

    $minimumX = $Bitmap.Width
    $minimumY = $Bitmap.Height
    $maximumX = -1
    $maximumY = -1

    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -lt $AlphaThreshold) {
                continue
            }

            $minimumX = [Math]::Min($minimumX, $x)
            $minimumY = [Math]::Min($minimumY, $y)
            $maximumX = [Math]::Max($maximumX, $x)
            $maximumY = [Math]::Max($maximumY, $y)
        }
    }

    if ($maximumX -lt 0 -or $maximumY -lt 0) {
        throw 'The generated icon glyph is empty.'
    }

    return [System.Drawing.Rectangle]::FromLTRB(
        $minimumX,
        $minimumY,
        $maximumX + 1,
        $maximumY + 1)
}

function Convert-RenderTargetToPngBytes {
    param(
        [System.Windows.Media.Imaging.RenderTargetBitmap]$RenderTarget
    )

    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add(
        [System.Windows.Media.Imaging.BitmapFrame]::Create($RenderTarget))
    $stream = [System.IO.MemoryStream]::new()
    try {
        $encoder.Save($stream)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

function Get-NormalizedFrame {
    param(
        [byte[]]$PngBytes,
        [int]$Size,
        [System.Collections.Generic.List[object]]$Reports
    )

    $sourceStream = [System.IO.MemoryStream]::new($PngBytes)
    $sourceBitmap = $null
    try {
        $loadedBitmap = [System.Drawing.Bitmap]::FromStream($sourceStream)
        $sourceBitmap = [System.Drawing.Bitmap]::new($loadedBitmap)
        $loadedBitmap.Dispose()
    }
    finally {
        $sourceStream.Dispose()
    }

    try {
        $bounds = Get-VisibleInkBounds $sourceBitmap
        $targetCenter = ($Size - 1) / 2
        $actualCenterX = ($bounds.Left + $bounds.Right - 1) / 2
        $actualCenterY = ($bounds.Top + $bounds.Bottom - 1) / 2
        $deltaX = $actualCenterX - $targetCenter
        $deltaY = $actualCenterY - $targetCenter

        if ([Math]::Abs($deltaX) -gt 0.5 -or
            [Math]::Abs($deltaY) -gt 0.5) {
            $shiftX = [int][Math]::Round(
                -$deltaX,
                [MidpointRounding]::AwayFromZero)
            $shiftY = [int][Math]::Round(
                -$deltaY,
                [MidpointRounding]::AwayFromZero)
            $corrected = [System.Drawing.Bitmap]::new($Size, $Size)
            $graphics = [System.Drawing.Graphics]::FromImage($corrected)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.DrawImageUnscaled($sourceBitmap, $shiftX, $shiftY)
            }
            finally {
                $graphics.Dispose()
            }
            $sourceBitmap.Dispose()
            $sourceBitmap = $corrected
            $bounds = Get-VisibleInkBounds $sourceBitmap
        }

        $finalCenterX = ($bounds.Left + $bounds.Right - 1) / 2
        $finalCenterY = ($bounds.Top + $bounds.Bottom - 1) / 2
        $finalDeltaX = $finalCenterX - $targetCenter
        $finalDeltaY = $finalCenterY - $targetCenter
        if ([Math]::Abs($finalDeltaX) -gt 0.5 -or
            [Math]::Abs($finalDeltaY) -gt 0.5) {
            throw "The ${Size}px mark is not centered: X=$finalDeltaX, Y=$finalDeltaY"
        }

        $Reports.Add([pscustomobject]@{
                Size = $Size
                Bounds = "$($bounds.Left),$($bounds.Top)-$($bounds.Right - 1),$($bounds.Bottom - 1)"
                DeltaX = $finalDeltaX
                DeltaY = $finalDeltaY
            })

        $outputStream = [System.IO.MemoryStream]::new()
        try {
            $sourceBitmap.Save(
                $outputStream,
                [System.Drawing.Imaging.ImageFormat]::Png)
            return $outputStream.ToArray()
        }
        finally {
            $outputStream.Dispose()
        }
    }
    finally {
        $sourceBitmap.Dispose()
    }
}

function New-VectorFrame {
    param(
        [int]$Size,
        [System.Collections.Generic.List[object]]$Reports
    )

    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    try {
        $scale = $Size / 256.0
        $context.PushTransform(
            [System.Windows.Media.ScaleTransform]::new($scale, $scale))
        try {
            $context.PushClip(
                [System.Windows.Media.EllipseGeometry]::new(
                    [System.Windows.Point]::new(128, 128),
                    110,
                    110))
            try {
            $brushes = @(
                (New-GradientBrush '#9AF6FF' '#37D1F4' '#3195F1'),
                (New-GradientBrush '#57DFFF' '#258EEB' '#3156D9'),
                (New-GradientBrush '#76B9FF' '#6479F2' '#7C65DE')
            )
            $angles = @(0, 120, 240)
            for ($index = 0; $index -lt $angles.Count; $index++) {
                $geometry = New-RotatedGeometry $lobePath $angles[$index]
                $context.DrawGeometry($brushes[$index], $null, $geometry)
            }

            if ($Size -ge 32) {
                $highlightBrush = New-GradientBrush '#AFFFFFFF' '#55E8FFFF' '#00D9FCFF'
                $highlightPen =
                    [System.Windows.Media.Pen]::new($highlightBrush, 3)
                $highlightPen.StartLineCap =
                    [System.Windows.Media.PenLineCap]::Round
                $highlightPen.EndLineCap =
                    [System.Windows.Media.PenLineCap]::Round
                $highlightPen.Freeze()
                foreach ($angle in $angles) {
                    $highlightGeometry =
                        New-RotatedGeometry $highlightPath $angle
                    $context.DrawGeometry(
                        $null,
                        $highlightPen,
                        $highlightGeometry)
                }
            }
            }
            finally {
                $context.Pop()
            }
        }
        finally {
            $context.Pop()
        }
    }
    finally {
        $context.Close()
    }

    $target = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $Size,
        $Size,
        96,
        96,
        [System.Windows.Media.PixelFormats]::Pbgra32)
    $target.Render($visual)
    $rawBytes = Convert-RenderTargetToPngBytes $target
    return Get-NormalizedFrame $rawBytes $Size $Reports
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
$reports = [System.Collections.Generic.List[object]]::new()

foreach ($size in $sizes) {
    $frame = New-VectorFrame $size $reports
    $images.Add($frame)
    if ($size -eq 256) {
        [System.IO.File]::WriteAllBytes($pngOutputPath, $frame)
    }
}

$file = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    for ($index = 0; $index -lt $images.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }

    foreach ($image in $images) {
        $writer.Write($image)
    }
}
finally {
    $writer.Dispose()
}

Write-Output "Icons generated: $OutputPath; $pngOutputPath"
$reports | Format-Table -AutoSize | Out-String | Write-Output
