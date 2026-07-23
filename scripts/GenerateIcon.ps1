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
            $tileBrush = [System.Drawing.SolidBrush]::new(
                [System.Drawing.Color]::FromArgb(255, 77, 126, 242))
            try {
                $graphics.FillPath($tileBrush, $tilePath)
            }
            finally {
                $tileBrush.Dispose()
            }
        }
        finally {
            $tilePath.Dispose()
        }

        # Match the original in-app icon exactly: the Fluent router/monitor glyph.
        $graphics.TextRenderingHint =
            [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $iconFont = [System.Drawing.Font]::new(
            'Segoe Fluent Icons',
            [float]($size * 0.50),
            [System.Drawing.FontStyle]::Regular,
            [System.Drawing.GraphicsUnit]::Pixel)
        $whiteBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 255, 255, 255))
        $format = [System.Drawing.StringFormat]::new()
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        try {
            $glyphRect = [System.Drawing.RectangleF]::new(
                0,
                [float](-$size * 0.012),
                $size,
                $size)
            $graphics.DrawString(
                [string][char]0xE8CE,
                $iconFont,
                $whiteBrush,
                $glyphRect,
                $format)
        }
        finally {
            $format.Dispose()
            $whiteBrush.Dispose()
            $iconFont.Dispose()
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
