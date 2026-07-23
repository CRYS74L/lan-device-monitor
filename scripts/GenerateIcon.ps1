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

Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param(
        [System.Drawing.RectangleF]$Rectangle,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($Rectangle.Left, $Rectangle.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.Left, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        # The tile nearly fills the canvas so the mark remains legible in the tray.
        $tileInset = [Math]::Max(0.25, $size * 0.012)
        $tileRect = [System.Drawing.RectangleF]::new(
            $tileInset,
            $tileInset,
            $size - ($tileInset * 2),
            $size - ($tileInset * 2))
        $tilePath = New-RoundedPath $tileRect ([float]($size * 0.245))
        try {
            $tileGradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                $tileRect,
                [System.Drawing.Color]::FromArgb(255, 76, 124, 255),
                [System.Drawing.Color]::FromArgb(255, 37, 67, 210),
                48)
            try {
                $blend = [System.Drawing.Drawing2D.ColorBlend]::new()
                $blend.Colors = @(
                    [System.Drawing.Color]::FromArgb(255, 86, 139, 255),
                    [System.Drawing.Color]::FromArgb(255, 54, 91, 231),
                    [System.Drawing.Color]::FromArgb(255, 39, 61, 190)
                )
                $blend.Positions = @(0.0, 0.58, 1.0)
                $tileGradient.InterpolationColors = $blend
                $graphics.FillPath($tileGradient, $tilePath)
            }
            finally {
                $tileGradient.Dispose()
            }

            # Soft glass highlight, clipped inside the rounded tile.
            $graphics.SetClip($tilePath)
            $highlightRect = [System.Drawing.RectangleF]::new(
                -$size * 0.10,
                -$size * 0.30,
                $size * 1.18,
                $size * 0.84)
            $highlight = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                $highlightRect,
                [System.Drawing.Color]::FromArgb(92, 255, 255, 255),
                [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
                90)
            try {
                $graphics.FillEllipse($highlight, $highlightRect)
            }
            finally {
                $highlight.Dispose()
                $graphics.ResetClip()
            }

            $edgePen = [System.Drawing.Pen]::new(
                [System.Drawing.Color]::FromArgb(112, 255, 255, 255),
                [Math]::Max(0.7, $size * 0.012))
            try {
                $graphics.DrawPath($edgePen, $tilePath)
            }
            finally {
                $edgePen.Dispose()
            }
        }
        finally {
            $tilePath.Dispose()
        }

        $lineWidth = [Math]::Max(1.05, $size * 0.044)
        $whitePen = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::FromArgb(252, 255, 255, 255),
            $lineWidth)
        $whitePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $whitePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $nodeBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(252, 255, 255, 255))
        try {
            # Monitor outline.
            $monitorRect = [System.Drawing.RectangleF]::new(
                $size * 0.20,
                $size * 0.22,
                $size * 0.60,
                $size * 0.31)
            $monitorPath = New-RoundedPath $monitorRect ([float]($size * 0.055))
            try {
                $graphics.DrawPath($whitePen, $monitorPath)
            }
            finally {
                $monitorPath.Dispose()
            }

            # A tiny status pill inside the display.
            $graphics.DrawLine(
                $whitePen,
                $size * 0.45,
                $size * 0.325,
                $size * 0.55,
                $size * 0.325)

            # Stand and network topology.
            $graphics.DrawLine($whitePen, $size * 0.50, $size * 0.53, $size * 0.50, $size * 0.63)
            $graphics.DrawLine($whitePen, $size * 0.40, $size * 0.63, $size * 0.60, $size * 0.63)
            $graphics.DrawLine($whitePen, $size * 0.50, $size * 0.64, $size * 0.50, $size * 0.73)
            $graphics.DrawLine($whitePen, $size * 0.32, $size * 0.73, $size * 0.68, $size * 0.73)
            $graphics.DrawLine($whitePen, $size * 0.32, $size * 0.73, $size * 0.32, $size * 0.80)
            $graphics.DrawLine($whitePen, $size * 0.68, $size * 0.73, $size * 0.68, $size * 0.80)
            $graphics.DrawLine($whitePen, $size * 0.50, $size * 0.73, $size * 0.50, $size * 0.80)

            $nodeSize = [Math]::Max(1.6, $size * 0.065)
            foreach ($nodeX in @(0.32, 0.50, 0.68)) {
                $graphics.FillEllipse(
                    $nodeBrush,
                    ($size * $nodeX) - ($nodeSize / 2),
                    ($size * 0.82) - ($nodeSize / 2),
                    $nodeSize,
                    $nodeSize)
            }
        }
        finally {
            $whitePen.Dispose()
            $nodeBrush.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        try {
            if ($size -eq 256) {
                $bitmap.Save($pngOutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
            }

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
