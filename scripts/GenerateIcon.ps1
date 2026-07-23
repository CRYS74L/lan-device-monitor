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
M686.404267 458.052267c-13.755733 0-26.709333-4.0448-36.420267-12.1344-9.710933-8.0896-15.377067-19.421867-15.377067-30.754134 0-57.480533-55.8592-103.611733-123.050666-103.611733-67.9936 0-123.050667 46.148267-123.050667 103.611733 0 16.196267-9.710933 29.952-26.709333 38.0416-16.9984 8.0896-35.618133 8.0896-51.8144 0-16.196267-8.0896-26.709333-21.8624-26.709334-38.0416 0-105.233067 101.9904-189.422933 226.6624-189.422933s227.464533 85.8112 227.464534 190.242133c0.8192 22.6304-22.664533 42.069333-50.9952 42.069334z
'@,
    @'
M845.8752 415.9488c-13.755733 0-26.709333-4.0448-36.420267-12.1344-9.710933-8.9088-15.377067-19.421867-15.377066-30.754133 0-84.992-53.435733-162.7136-140.8512-204.8s-195.089067-42.0864-282.5216 0-140.8512 119.808-140.8512 204.8c0 23.483733-23.483733 42.9056-51.8144 42.9056s-51.8144-19.421867-51.8144-42.9056c0-115.7632 73.659733-222.6176 193.467733-280.081067 119.005867-57.480533 267.144533-57.480533 386.133333 0s191.8464 163.515733 191.8464 279.278933c0.017067 24.2688-23.466667 43.690667-51.797333 43.690667z m16.9984 153.8048H159.419733c-27.528533 0-50.193067 26.709333-50.193066 59.904v265.5232c0 32.375467 22.664533 59.904 50.193066 59.904h703.453867c27.528533 0 50.193067-26.709333 50.193067-59.904V629.6576c0-33.194667-22.664533-59.904-50.193067-59.904zM283.272533 790.749867c-8.9088 8.0896-20.241067 12.9536-30.754133 12.9536-24.285867 0-44.526933-20.241067-44.526933-44.526934s20.241067-44.526933 44.526933-44.526933 44.526933 20.241067 44.526933 44.526933c-0.8192 11.332267-5.6832 22.664533-13.7728 31.573334z m134.382934 0c-8.9088 8.0896-20.241067 12.9536-30.754134 12.9536-25.088 0-44.526933-20.241067-44.526933-44.526934s20.241067-44.526933 44.526933-44.526933c25.088 0 44.526933 20.241067 44.526934 44.526933-0.8192 11.332267-5.6832 22.664533-13.7728 31.573334z m136.8064 0c-8.9088 8.0896-20.241067 12.9536-30.754134 12.9536-25.088 0-44.526933-20.241067-44.526934-44.526934s20.241067-44.526933 44.526934-44.526933c25.088 0 44.526933 20.241067 44.526934 44.526933-0.017067 11.332267-4.881067 22.664533-13.7728 31.573334z m-102.007467-310.852267c0 31.573333 25.9072 56.661333 58.282667 56.661333s58.282667-25.088 58.282666-56.661333c0-31.573333-25.9072-56.661333-58.282666-56.661333-32.375467 0.802133-58.282667 25.9072-58.282667 56.661333z
'@
)

$sourceBounds = [System.Windows.Rect]::new(
    109.226669311523,
    49.8688011169434,
    803.839981079102,
    905.215976715088)
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
            $brush = if ($Variant -eq 'System') {
                [System.Windows.Media.Brushes]::White
            }
            else {
                New-BlueBrush
            }
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
