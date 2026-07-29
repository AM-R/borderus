param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\assets\Borderus.ico'),
    [string]$PreviewPath = (Join-Path $PSScriptRoot '..\assets\Borderus-preview.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRectangle([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $inset = [float]($size * 0.07)
        $radius = [float]($size * 0.22)
        $background = New-RoundedRectangle $inset $inset ($size - $inset * 2) ($size - $inset * 2) $radius
        $dark = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 31, 31, 31))
        try { $graphics.FillPath($dark, $background) } finally { $dark.Dispose(); $background.Dispose() }

        $gridInset = [float]($size * 0.23)
        $span = [float]($size - $gridInset * 2)
        $stroke = [float][Math]::Max(1.5, $size * 0.065)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0, 120, 215), $stroke)
        try {
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            foreach ($i in 1, 2) {
                $position = [float]($gridInset + $span * $i / 3)
                $graphics.DrawLine($pen, $position, $gridInset, $position, $size - $gridInset)
                $graphics.DrawLine($pen, $gridInset, $position, $size - $gridInset, $position)
            }
        } finally { $pen.Dispose() }
    } finally { $graphics.Dispose() }
    return $bitmap
}

$outputDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$sizes = 16, 24, 32, 48, 64, 256
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    try {
        $stream = New-Object IO.MemoryStream
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add($stream.ToArray())
        } finally { $stream.Dispose() }
        if ($size -eq 256) { $bitmap.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png) }
    } finally { $bitmap.Dispose() }
}

$file = [IO.File]::Create($OutputPath)
$writer = New-Object IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + $sizes.Count * 16
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([int]$images[$i].Length)
        $writer.Write([int]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
} finally { $writer.Dispose(); $file.Dispose() }

Get-Item -LiteralPath $OutputPath, $PreviewPath | Select-Object FullName, Length
