# Generates the PNG icons used by the Antigravity Switcher Flow Launcher plugin.
# Requires Windows PowerShell / PowerShell 7 on Windows (GDI+ / System.Drawing).
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\Images')
)

Add-Type -AssemblyName System.Drawing

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$size = 64

function New-IconCanvas {
    param([int]$Size = 64)
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([System.Drawing.Color]::Transparent)
    return @{ Bitmap = $bitmap; Graphics = $graphics }
}

function Save-Icon {
    param($Canvas, [string]$Name)
    $path = Join-Path $OutputDirectory $Name
    $Canvas.Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $Canvas.Graphics.Dispose()
    $Canvas.Bitmap.Dispose()
    Write-Host "Wrote $path"
}

function Add-FilledCircle {
    param($Graphics, [single]$Cx, [single]$Cy, [single]$Radius, [System.Drawing.Color]$Color)
    $brush = [System.Drawing.SolidBrush]::new($Color)
    $Graphics.FillEllipse($brush, $Cx - $Radius, $Cy - $Radius, $Radius * 2, $Radius * 2)
    $brush.Dispose()
}

function Get-RoundedRectPath {
    param([single]$X, [single]$Y, [single]$Width, [single]$Height, [single]$Radius)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Font {
    param([single]$EmSize, [System.Drawing.FontStyle]$Style = [System.Drawing.FontStyle]::Bold)
    foreach ($family in 'Segoe UI', 'Arial') {
        try {
            return [System.Drawing.Font]::new($family, $EmSize, $Style, [System.Drawing.GraphicsUnit]::Pixel)
        } catch { }
    }
    return [System.Drawing.Font]::new([System.Drawing.FontFamily]::GenericSansSerif, $EmSize, $Style, [System.Drawing.GraphicsUnit]::Pixel)
}

function Add-CenteredText {
    param($Graphics, [string]$Text, [single]$Cx, [single]$Cy, [single]$EmSize, [System.Drawing.Color]$Color)
    $font = New-Font -EmSize $EmSize
    $brush = [System.Drawing.SolidBrush]::new($Color)
    $format = [System.Drawing.StringFormat]::new()
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $Graphics.DrawString($Text, $font, $brush, $Cx, $Cy, $format)
    $format.Dispose()
    $brush.Dispose()
    $font.Dispose()
}

$white = [System.Drawing.Color]::White
$green = [System.Drawing.Color]::FromArgb(255, 34, 197, 94)
$red = [System.Drawing.Color]::FromArgb(255, 239, 68, 68)
$slate = [System.Drawing.Color]::FromArgb(255, 148, 163, 184)
$blue = [System.Drawing.Color]::FromArgb(255, 59, 130, 246)
$cyan = [System.Drawing.Color]::FromArgb(255, 34, 211, 238)
$indigo = [System.Drawing.Color]::FromArgb(255, 79, 70, 229)
$amber = [System.Drawing.Color]::FromArgb(255, 245, 158, 11)

# app.png - rounded gradient tile with a white "A"
$canvas = New-IconCanvas
$rect = [System.Drawing.Rectangle]::new(4, 4, $size - 8, $size - 8)
$path = Get-RoundedRectPath -X 4 -Y 4 -Width ($size - 8) -Height ($size - 8) -Radius 14
$gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rect, $indigo, $cyan, 45.0)
$canvas.Graphics.FillPath($gradient, $path)
$gradient.Dispose()
$path.Dispose()
Add-CenteredText -Graphics $canvas.Graphics -Text 'A' -Cx ($size / 2) -Cy ($size / 2) -EmSize 34 -Color $white
Save-Icon -Canvas $canvas -Name 'app.png'

# next.png - blue disc with a right arrow
$canvas = New-IconCanvas
Add-FilledCircle -Graphics $canvas.Graphics -Cx ($size / 2) -Cy ($size / 2) -Radius 29 -Color $blue
$pen = [System.Drawing.Pen]::new($white, 6)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$arrow = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(26, 20),
    [System.Drawing.PointF]::new(42, 32),
    [System.Drawing.PointF]::new(26, 44)
)
$canvas.Graphics.DrawLines($pen, $arrow)
$pen.Dispose()
Save-Icon -Canvas $canvas -Name 'next.png'

# active.png - green disc with a check mark
$canvas = New-IconCanvas
Add-FilledCircle -Graphics $canvas.Graphics -Cx ($size / 2) -Cy ($size / 2) -Radius 29 -Color $green
$pen = [System.Drawing.Pen]::new($white, 7)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$check = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(19, 33),
    [System.Drawing.PointF]::new(28, 43),
    [System.Drawing.PointF]::new(45, 22)
)
$canvas.Graphics.DrawLines($pen, $check)
$pen.Dispose()
Save-Icon -Canvas $canvas -Name 'active.png'

# standby.png - slate disc with a pause glyph
$canvas = New-IconCanvas
Add-FilledCircle -Graphics $canvas.Graphics -Cx ($size / 2) -Cy ($size / 2) -Radius 29 -Color $slate
$brush = [System.Drawing.SolidBrush]::new($white)
$canvas.Graphics.FillRectangle($brush, 24, 22, 6, 20)
$canvas.Graphics.FillRectangle($brush, 34, 22, 6, 20)
$brush.Dispose()
Save-Icon -Canvas $canvas -Name 'standby.png'

# exhausted.png - red disc with an exclamation mark
$canvas = New-IconCanvas
Add-FilledCircle -Graphics $canvas.Graphics -Cx ($size / 2) -Cy ($size / 2) -Radius 29 -Color $red
$brush = [System.Drawing.SolidBrush]::new($white)
$canvas.Graphics.FillRectangle($brush, 29, 18, 6, 20)
$canvas.Graphics.FillEllipse($brush, 29, 42, 6, 6)
$brush.Dispose()
Save-Icon -Canvas $canvas -Name 'exhausted.png'

# add.png - green disc with a plus
$canvas = New-IconCanvas
Add-FilledCircle -Graphics $canvas.Graphics -Cx ($size / 2) -Cy ($size / 2) -Radius 29 -Color $green
$brush = [System.Drawing.SolidBrush]::new($white)
$canvas.Graphics.FillRectangle($brush, 28, 18, 8, 28)
$canvas.Graphics.FillRectangle($brush, 18, 28, 28, 8)
$brush.Dispose()
Save-Icon -Canvas $canvas -Name 'add.png'

# remove.png - red disc with a minus
$canvas = New-IconCanvas
Add-FilledCircle -Graphics $canvas.Graphics -Cx ($size / 2) -Cy ($size / 2) -Radius 29 -Color $red
$brush = [System.Drawing.SolidBrush]::new($white)
$canvas.Graphics.FillRectangle($brush, 18, 28, 28, 8)
$brush.Dispose()
Save-Icon -Canvas $canvas -Name 'remove.png'

# refresh.png - cyan disc with a circular arrow
$canvas = New-IconCanvas
Add-FilledCircle -Graphics $canvas.Graphics -Cx ($size / 2) -Cy ($size / 2) -Radius 29 -Color $cyan
$pen = [System.Drawing.Pen]::new($white, 6)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$canvas.Graphics.DrawArc($pen, 16, 16, 32, 32, -60, 300)
$head = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(41, 14),
    [System.Drawing.PointF]::new(48, 24),
    [System.Drawing.PointF]::new(36, 26)
)
$headBrush = [System.Drawing.SolidBrush]::new($white)
$canvas.Graphics.FillPolygon($headBrush, $head)
$headBrush.Dispose()
$pen.Dispose()
Save-Icon -Canvas $canvas -Name 'refresh.png'

# web.png - blue disc with a globe
$canvas = New-IconCanvas
Add-FilledCircle -Graphics $canvas.Graphics -Cx ($size / 2) -Cy ($size / 2) -Radius 29 -Color $blue
$pen = [System.Drawing.Pen]::new($white, 3)
$canvas.Graphics.DrawEllipse($pen, 17, 17, 30, 30)
$canvas.Graphics.DrawLine($pen, 17, 32, 47, 32)
$canvas.Graphics.DrawEllipse($pen, 27, 17, 10, 30)
$pen.Dispose()
Save-Icon -Canvas $canvas -Name 'web.png'

Write-Host "Icon generation complete."
