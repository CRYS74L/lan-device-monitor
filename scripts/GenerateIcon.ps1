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

        $lineWidth = [Math]::Max(1.15, $size * 0.055)
        $linePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(230, 255, 255, 255), $lineWidth)
        $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $nodeBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
        $accentBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 56, 220, 192))
        try {
            $center = [System.Drawing.PointF]::new($size * 0.50, $size * 0.52)
            $left = [System.Drawing.PointF]::new($size * 0.27, $size * 0.35)
            $right = [System.Drawing.PointF]::new($size * 0.73, $size * 0.35)
            $bottom = [System.Drawing.PointF]::new($size * 0.50, $size * 0.76)
            $graphics.DrawLine($linePen, $center, $left)
            $graphics.DrawLine($linePen, $center, $right)
            $graphics.DrawLine($linePen, $center, $bottom)

            $nodeSize = [Math]::Max(2.2, $size * 0.145)
            foreach ($point in @($left, $right, $bottom)) {
                $graphics.FillEllipse(
                    $nodeBrush,
                    $point.X - ($nodeSize / 2),
                    $point.Y - ($nodeSize / 2),
                    $nodeSize,
                    $nodeSize)
            }

            $centerSize = [Math]::Max(2.8, $size * 0.19)
            $graphics.FillEllipse(
                $accentBrush,
                $center.X - ($centerSize / 2),
                $center.Y - ($centerSize / 2),
                $centerSize,
                $centerSize)
        }
        finally {
            $linePen.Dispose()
            $nodeBrush.Dispose()
            $accentBrush.Dispose()
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
