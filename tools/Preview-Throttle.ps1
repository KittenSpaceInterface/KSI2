param(
    [Parameter(Mandatory = $true)][string]$AssetDirectory,
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts\throttle-preview"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = (Resolve-Path -LiteralPath $AssetDirectory).Path
$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
Copy-Item -LiteralPath (Join-Path $assets 'throttle-assets.json') -Destination $output -Force
Get-ChildItem -LiteralPath $assets -Filter '*.png' | ForEach-Object {
    $bitmap = [Drawing.Bitmap]::new($_.FullName)
    try {
        $rgba = [byte[]]::new($bitmap.Width * $bitmap.Height * 4)
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $pixel = $bitmap.GetPixel($x, $y)
                $i = ($y * $bitmap.Width + $x) * 4
                $rgba[$i] = $pixel.R; $rgba[$i+1] = $pixel.G; $rgba[$i+2] = $pixel.B; $rgba[$i+3] = $pixel.A
            }
        }
        [IO.File]::WriteAllBytes((Join-Path $output ($_.Name + '.rgba')), $rgba)
    } finally { $bitmap.Dispose() }
}
dotnet run --project "$PSScriptRoot\..\tests\KSI2.GeometryTests" -c Release --no-restore -- $output
if ($LASTEXITCODE -ne 0) { throw 'Artwork verification failed.' }
$frames = Get-Content -LiteralPath (Join-Path $output 'preview.json') -Raw | ConvertFrom-Json
$image = [Drawing.Bitmap]::new(1750, 700)
$graphics = [Drawing.Graphics]::FromImage($image)
$font = [Drawing.Font]::new('Consolas', 28, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
try {
    $graphics.Clear([Drawing.Color]::FromArgb(255, 24, 27, 36))
    $index = 0
    foreach ($frame in $frames) {
        foreach ($primitive in $frame.primitives) {
            $packed = [uint32]$primitive.color
            $color = [Drawing.Color]::FromArgb([int](($packed -shr 24) -band 255), [int]($packed -band 255), [int](($packed -shr 8) -band 255), [int](($packed -shr 16) -band 255))
            $brush = [Drawing.SolidBrush]::new($color)
            try {
                $points = [Drawing.PointF[]]@($primitive.points | ForEach-Object { [Drawing.PointF]::new([single](($index * 175 + 25 + $_.X) * 2), [single]((14 + $_.Y) * 2 + 20)) })
                $graphics.FillPolygon($brush, $points)
            } finally { $brush.Dispose() }
        }
        $label = '{0:0}' -f ($frame.throttle * 100)
        $width = $graphics.MeasureString($label, $font).Width
        $state = $graphics.Save()
        $graphics.TranslateTransform([single](($index * 175 + 25 + $frame.handle.X) * 2), [single]((14 + $frame.handle.Y) * 2 + 20))
        $graphics.RotateTransform([single]($frame.angle * 180 / [Math]::PI))
        $graphics.DrawString($label, $font, [Drawing.Brushes]::White, [single](12 - $width / 2), -17)
        $graphics.Restore($state)
        $index++
    }
    $image.Save((Join-Path $output 'throttle-preview.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally { $font.Dispose(); $graphics.Dispose(); $image.Dispose() }
Write-Host "Preview: $output\throttle-preview.png (preview font differs from KSA)"
