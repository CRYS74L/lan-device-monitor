param(
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $projectRoot 'src\LanDeviceMonitor.App\Assets\AppIcon.ico'
}

Add-Type -AssemblyName System.Drawing
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $rect = [System.Drawing.RectangleF]::new(0.75, 0.75, $size - 1.5, $size - 1.5)
        $radius = [Math]::Max(3, $size * 0.24)
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        try {
            $diameter = $radius * 2
            $path.AddArc($rect.Left, $rect.Top, $diameter, $diameter, 180, 90)
            $path.AddArc($rect.Right - $diameter, $rect.Top, $diameter, $diameter, 270, 90)
            $path.AddArc($rect.Right - $diameter, $rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
            $path.AddArc($rect.Left, $rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
            $path.CloseFigure()

            $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                $rect,
                [System.Drawing.Color]::FromArgb(255, 35, 91, 238),
                [System.Drawing.Color]::FromArgb(255, 87, 60, 225),
                45)
            try {
                $graphics.FillPath($gradient, $path)
            }
            finally {
                $gradient.Dispose()
            }
        }
        finally {
            $path.Dispose()
        }

        $lineWidth = [Math]::Max(1.15, $size * 0.05)
        $antennaPen = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::FromArgb(245, 255, 255, 255),
            $lineWidth)
        $antennaPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $antennaPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $routerBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
        $indicatorBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 48, 197, 170))
        try {
            $graphics.DrawLine(
                $antennaPen,
                $size * 0.31,
                $size * 0.49,
                $size * 0.29,
                $size * 0.25)
            $graphics.DrawLine(
                $antennaPen,
                $size * 0.69,
                $size * 0.49,
                $size * 0.71,
                $size * 0.25)

            $routerRect = [System.Drawing.RectangleF]::new(
                $size * 0.20,
                $size * 0.43,
                $size * 0.60,
                $size * 0.30)
            $routerRadius = [Math]::Max(1.5, $size * 0.075)
            $routerPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
            try {
                $routerDiameter = $routerRadius * 2
                $routerPath.AddArc(
                    $routerRect.Left,
                    $routerRect.Top,
                    $routerDiameter,
                    $routerDiameter,
                    180,
                    90)
                $routerPath.AddArc(
                    $routerRect.Right - $routerDiameter,
                    $routerRect.Top,
                    $routerDiameter,
                    $routerDiameter,
                    270,
                    90)
                $routerPath.AddArc(
                    $routerRect.Right - $routerDiameter,
                    $routerRect.Bottom - $routerDiameter,
                    $routerDiameter,
                    $routerDiameter,
                    0,
                    90)
                $routerPath.AddArc(
                    $routerRect.Left,
                    $routerRect.Bottom - $routerDiameter,
                    $routerDiameter,
                    $routerDiameter,
                    90,
                    90)
                $routerPath.CloseFigure()
                $graphics.FillPath($routerBrush, $routerPath)
            }
            finally {
                $routerPath.Dispose()
            }

            $indicatorSize = [Math]::Max(1.1, $size * 0.042)
            foreach ($indicatorX in @(0.34, 0.43, 0.52)) {
                $graphics.FillEllipse(
                    $indicatorBrush,
                    ($size * $indicatorX) - ($indicatorSize / 2),
                    ($size * 0.62) - ($indicatorSize / 2),
                    $indicatorSize,
                    $indicatorSize)
            }
        }
        finally {
            $antennaPen.Dispose()
            $routerBrush.Dispose()
            $indicatorBrush.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add($stream.ToArray())
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
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

Write-Output "Icon generated: $OutputPath"
