param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$assetsDirectory =
    Join-Path $projectRoot 'src\LanDeviceMonitor.App\Assets'
[System.IO.Directory]::CreateDirectory($assetsDirectory) | Out-Null

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Drawing

$sourcePaths = @(
    @'
M532.038 0.41c-72.843-2.902-147.462 9.781-219.07 40.003-71.612 30.223-132.753 74.836-181.491 129.051 72.432-1.391 145.775-16.28 216.488-46.125C418.679 93.494 480.512 51.335 532.038 0.41zM441.627 345.266c140.577-59.33 256.27-153.169 341.414-267.521a514.78 514.78 0 0 0-132.678-58.566 690.967 690.967 0 0 1-37.939 42.388c-64.186 66.02-140.084 117.793-225.586 153.878-85.501 36.086-175.545 54.347-267.624 54.274a690.536 690.536 0 0 1-56.838-2.386 514.877 514.877 0 0 0-50.598 135.916c141.329 18.772 289.272 1.346 429.849-57.983z
'@,
    @'
M535.289 567.192c169.034-71.339 310.196-181.335 417.572-315.38-25.017-42.507-55.54-80.322-90.25-112.904a928.479 928.479 0 0 1-77.503 90.688c-86.659 89.14-189.144 159.047-304.608 207.777-115.463 48.729-237.044 73.388-361.367 73.29A929.085 929.085 0 0 1 0.091 502.928a508.255 508.255 0 0 0 17.933 143.423c170.947 16.582 348.231-7.821 517.265-79.159z
'@,
    @'
M998.663 353.31a1163.002 1163.002 0 0 1-40.869 44.315c-109.135 112.26-238.211 200.3-383.632 261.674-145.423 61.376-298.545 92.431-455.112 92.308a1159.323 1159.323 0 0 1-60.26-1.633c28.541 54.499 65.835 101.974 109.398 141.4 154.353-5.572 310.229-38.724 460.763-102.255 150.535-63.533 283.03-152.085 394.704-258.787 2.141-58.714-5.854-118.554-24.992-177.022zM305.351 980.291c124.479 54.902 270.44 60.368 405.675 3.294 135.234-57.073 233.14-165.464 280.646-292.949-98.29 77.292-206.657 141.131-323.847 190.59-117.19 49.456-238.526 82.564-362.474 99.065z
'@
)

$sourceBounds = [System.Windows.Rect]::new(
    0.00602184282615781,
    0.00628873892128468,
    1023.98702014936,
    1023.98687532358)
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-SourceGeometry {
    param([double]$Margin)

    $geometryGroup = [System.Windows.Media.GeometryGroup]::new()
    foreach ($pathData in $sourcePaths) {
        $geometryGroup.Children.Add(
            [System.Windows.Media.Geometry]::Parse($pathData))
    }

    $available = 256 - ($Margin * 2)
    $scaleX = $available / $sourceBounds.Width
    $scaleY = $available / $sourceBounds.Height
    $offsetX = $Margin - ($sourceBounds.X * $scaleX)
    $offsetY = $Margin - ($sourceBounds.Y * $scaleY)
    $geometryGroup.Transform =
        [System.Windows.Media.MatrixTransform]::new(
            [System.Windows.Media.Matrix]::new(
                $scaleX,
                0,
                0,
                $scaleY,
                $offsetX,
                $offsetY))
    $geometryGroup.Freeze()
    return $geometryGroup
}

function New-BlueBrush {
    $brush = [System.Windows.Media.LinearGradientBrush]::new()
    $brush.StartPoint = [System.Windows.Point]::new(0.15, 0.05)
    $brush.EndPoint = [System.Windows.Point]::new(0.85, 0.95)
    $brush.GradientStops.Add(
        [System.Windows.Media.GradientStop]::new(
            [System.Windows.Media.Color]::FromRgb(112, 190, 255),
            0))
    $brush.GradientStops.Add(
        [System.Windows.Media.GradientStop]::new(
            [System.Windows.Media.Color]::FromRgb(72, 143, 245),
            0.56))
    $brush.GradientStops.Add(
        [System.Windows.Media.GradientStop]::new(
            [System.Windows.Media.Color]::FromRgb(86, 111, 230),
            1))
    $brush.Freeze()
    return $brush
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
        throw 'The generated icon is empty.'
    }

    return [System.Drawing.Rectangle]::FromLTRB(
        $minimumX,
        $minimumY,
        $maximumX + 1,
        $maximumY + 1)
}

function New-IconFrame {
    param(
        [int]$Size,
        [ValidateSet('Interface', 'System', 'Executable')]
        [string]$Variant,
        [System.Collections.Generic.List[object]]$Reports
    )

    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    try {
        $scale = $Size / 256.0
        $context.PushTransform(
            [System.Windows.Media.ScaleTransform]::new($scale, $scale))
        try {
            if ($Variant -eq 'Executable') {
                $context.DrawRoundedRectangle(
                    [System.Windows.Media.Brushes]::White,
                    $null,
                    [System.Windows.Rect]::new(0, 0, 256, 256),
                    52,
                    52)
            }

            $margin = if ($Variant -eq 'Executable') { 24 } else { 2 }
            $geometry = New-SourceGeometry $margin
            $brush = New-BlueBrush
            $context.DrawGeometry($brush, $null, $geometry)
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
    $pngBytes = Convert-RenderTargetToPngBytes $target

    $stream = [System.IO.MemoryStream]::new($pngBytes)
    try {
        $loadedBitmap = [System.Drawing.Bitmap]::FromStream($stream)
        try {
            $bitmap = [System.Drawing.Bitmap]::new($loadedBitmap)
        }
        finally {
            $loadedBitmap.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    try {
        $bounds = Get-VisibleInkBounds $bitmap
        $targetCenter = ($Size - 1) / 2
        $centerX = ($bounds.Left + $bounds.Right - 1) / 2
        $centerY = ($bounds.Top + $bounds.Bottom - 1) / 2
        $deltaX = $centerX - $targetCenter
        $deltaY = $centerY - $targetCenter
        if ([Math]::Abs($deltaX) -gt 0.5 -or
            [Math]::Abs($deltaY) -gt 0.5) {
            throw "$Variant ${Size}px icon is not centered: X=$deltaX, Y=$deltaY"
        }

        $Reports.Add([pscustomobject]@{
                Variant = $Variant
                Size = $Size
                Bounds = "$($bounds.Left),$($bounds.Top)-$($bounds.Right - 1),$($bounds.Bottom - 1)"
                DeltaX = $deltaX
                DeltaY = $deltaY
            })
    }
    finally {
        $bitmap.Dispose()
    }

    return $pngBytes
}

function Write-Ico {
    param(
        [string]$Path,
        [System.Collections.Generic.List[byte[]]]$Images
    )

    $file = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$Images.Count)
        $offset = 6 + (16 * $Images.Count)
        for ($index = 0; $index -lt $Images.Count; $index++) {
            $size = $sizes[$index]
            $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$Images[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $Images[$index].Length
        }

        foreach ($image in $Images) {
            $writer.Write($image)
        }
    }
    finally {
        $writer.Dispose()
    }
}

$reports = [System.Collections.Generic.List[object]]::new()
$variants = @(
    [pscustomobject]@{
        Name = 'Interface'
        Ico = $null
        Png = Join-Path $assetsDirectory 'AppIcon.png'
    },
    [pscustomobject]@{
        Name = 'System'
        Ico = Join-Path $assetsDirectory 'SystemIcon.ico'
        Png = Join-Path $assetsDirectory 'SystemIcon.png'
    },
    [pscustomobject]@{
        Name = 'Executable'
        Ico = Join-Path $assetsDirectory 'AppIcon.ico'
        Png = Join-Path $assetsDirectory 'ExecutableIcon.png'
    }
)

foreach ($variant in $variants) {
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $frame = New-IconFrame $size $variant.Name $reports
        $frames.Add($frame)
        if ($size -eq 256) {
            [System.IO.File]::WriteAllBytes($variant.Png, $frame)
        }
    }

    if ($null -ne $variant.Ico) {
        Write-Ico $variant.Ico $frames
    }
}

Write-Output 'Icon variants generated.'
$reports | Format-Table -AutoSize | Out-String | Write-Output
