# Generates the high-DPI 128x128 Fluent vector-style PNG icons used by the Antigravity Switcher Flow Launcher plugin.
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

$size = 128

function New-IconCanvas {
    param([int]$CanvasSize = 128)
    $bitmap = [System.Drawing.Bitmap]::new($CanvasSize, $CanvasSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
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

function Get-RoundedTrianglePath {
    param([single]$Radius = 14)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $cx = [single]64
    $d = [single]38
    $yt = [single]30
    $yb = [single]92
    $r = [single]$Radius

    # Top arc
    $path.AddArc([single]($cx - $r), [single]($yt - $r), [single]($r * 2), [single]($r * 2), [single]211.5, [single]117.0)
    # Bottom-right arc
    $path.AddArc([single]($cx + $d - $r), [single]($yb - $r), [single]($r * 2), [single]($r * 2), [single]-31.5, [single]121.5)
    # Bottom-left arc
    $path.AddArc([single]($cx - $d - $r), [single]($yb - $r), [single]($r * 2), [single]($r * 2), [single]90.0, [single]121.5)
    $path.CloseFigure()
    return $path
}

# Fluent Palette Colors
$white = [System.Drawing.Color]::White
$whiteHighlight = [System.Drawing.Color]::FromArgb(45, 255, 255, 255)

$indigoDark = [System.Drawing.Color]::FromArgb(255, 79, 70, 229)    # #4F46E5
$indigoLight = [System.Drawing.Color]::FromArgb(255, 99, 102, 241)  # #6366F1
$cyanDeep = [System.Drawing.Color]::FromArgb(255, 6, 182, 212)      # #06B6D4
$cyanLight = [System.Drawing.Color]::FromArgb(255, 34, 211, 238)    # #22D3EE
$cyanSoft = [System.Drawing.Color]::FromArgb(255, 125, 211, 252)    # #7DD3FC

$emeraldLight = [System.Drawing.Color]::FromArgb(255, 52, 211, 153) # #34D399
$emeraldDark = [System.Drawing.Color]::FromArgb(255, 5, 150, 105)   # #059669

$slateLight = [System.Drawing.Color]::FromArgb(255, 148, 163, 184)  # #94A3B8
$slateDark = [System.Drawing.Color]::FromArgb(255, 71, 85, 105)     # #475569

$amber = [System.Drawing.Color]::FromArgb(255, 245, 158, 11)        # #F59E0B
$crimson = [System.Drawing.Color]::FromArgb(255, 220, 38, 38)       # #DC2626

$azureLight = [System.Drawing.Color]::FromArgb(255, 59, 130, 246)   # #3B82F6
$azureDark = [System.Drawing.Color]::FromArgb(255, 29, 78, 216)     # #1D4ED8

$roseLight = [System.Drawing.Color]::FromArgb(255, 244, 63, 94)     # #F43F5E
$roseDark = [System.Drawing.Color]::FromArgb(255, 225, 29, 72)      # #E11D48

# ==============================================================================
# 1. app.png - Dual concentric gravitational rings with indigo-to-cyan gradient
#    and central nucleus on transparent canvas
# ==============================================================================
$canvas = New-IconCanvas
$orbitBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(16, 16),
    [System.Drawing.PointF]::new(112, 112),
    $indigoLight,
    $cyanLight
)

# Outer gravitational ring
$penOuter = [System.Drawing.Pen]::new($orbitBrush, 7.0)
$canvas.Graphics.DrawEllipse($penOuter, 16, 16, 96, 96)
$penOuter.Dispose()

# Inner gravitational ring
$penInner = [System.Drawing.Pen]::new($orbitBrush, 5.5)
$canvas.Graphics.DrawEllipse($penInner, 33, 33, 62, 62)
$penInner.Dispose()

# Gravitational orbital nodes / particles
$nodeBrushOuter = [System.Drawing.SolidBrush]::new($cyanLight)
$canvas.Graphics.FillEllipse($nodeBrushOuter, 94.0, 26.0, 9.0, 9.0)
$nodeBrushOuter.Dispose()

$nodeBrushInner = [System.Drawing.SolidBrush]::new($indigoLight)
$canvas.Graphics.FillEllipse($nodeBrushInner, 25.0, 59.0, 7.0, 7.0)
$nodeBrushInner.Dispose()

# Central nucleus with gradient fill
$nucleusBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(49, 49),
    [System.Drawing.PointF]::new(79, 79),
    $cyanLight,
    $indigoDark
)
$canvas.Graphics.FillEllipse($nucleusBrush, 49, 49, 30, 30)
$nucleusBrush.Dispose()

# Nucleus inner specular highlight
$specularBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(200, 255, 255, 255))
$canvas.Graphics.FillEllipse($specularBrush, 56, 56, 7, 7)
$specularBrush.Dispose()

$orbitBrush.Dispose()
Save-Icon -Canvas $canvas -Name 'app.png'

# ==============================================================================
# 2. active.png - Soft emerald rounded badge with clean checkmark lines
# ==============================================================================
$canvas = New-IconCanvas
$badgePath = Get-RoundedRectPath -X 14 -Y 14 -Width 100 -Height 100 -Radius 28
$badgeBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(14, 14),
    [System.Drawing.PointF]::new(114, 114),
    $emeraldLight,
    $emeraldDark
)
$canvas.Graphics.FillPath($badgeBrush, $badgePath)

# Subtle highlight stroke along top rim
$highlightPen = [System.Drawing.Pen]::new($whiteHighlight, 1.5)
$canvas.Graphics.DrawPath($highlightPen, $badgePath)
$highlightPen.Dispose()
$badgeBrush.Dispose()
$badgePath.Dispose()

# Clean checkmark lines
$checkPen = [System.Drawing.Pen]::new($white, 10.0)
$checkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$checkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$checkPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$checkPoints = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(38, 64),
    [System.Drawing.PointF]::new(54, 80),
    [System.Drawing.PointF]::new(90, 44)
)
$canvas.Graphics.DrawLines($checkPen, $checkPoints)
$checkPen.Dispose()
Save-Icon -Canvas $canvas -Name 'active.png'

# ==============================================================================
# 3. standby.png - Slate pill container with vertical pause bars
# ==============================================================================
$canvas = New-IconCanvas
$pillPath = Get-RoundedRectPath -X 14 -Y 14 -Width 100 -Height 100 -Radius 28
$pillBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(14, 14),
    [System.Drawing.PointF]::new(114, 114),
    $slateLight,
    $slateDark
)
$canvas.Graphics.FillPath($pillBrush, $pillPath)

# Subtle highlight stroke
$highlightPen = [System.Drawing.Pen]::new($whiteHighlight, 1.5)
$canvas.Graphics.DrawPath($highlightPen, $pillPath)
$highlightPen.Dispose()
$pillBrush.Dispose()
$pillPath.Dispose()

# Vertical pause bars (pill capsules)
$pauseBrush = [System.Drawing.SolidBrush]::new($white)
$bar1Path = Get-RoundedRectPath -X 43 -Y 38 -Width 12 -Height 52 -Radius 6
$bar2Path = Get-RoundedRectPath -X 73 -Y 38 -Width 12 -Height 52 -Radius 6
$canvas.Graphics.FillPath($pauseBrush, $bar1Path)
$canvas.Graphics.FillPath($pauseBrush, $bar2Path)
$bar1Path.Dispose()
$bar2Path.Dispose()
$pauseBrush.Dispose()
Save-Icon -Canvas $canvas -Name 'standby.png'

# ==============================================================================
# 4. exhausted.png - Rounded amber/crimson triangle with exclamation mark
# ==============================================================================
$canvas = New-IconCanvas
$triPath = Get-RoundedTrianglePath -Radius 14
$triBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(64, 16),
    [System.Drawing.PointF]::new(64, 106),
    $amber,
    $crimson
)
$canvas.Graphics.FillPath($triBrush, $triPath)

# Subtle highlight stroke
$highlightPen = [System.Drawing.Pen]::new($whiteHighlight, 1.5)
$canvas.Graphics.DrawPath($highlightPen, $triPath)
$highlightPen.Dispose()
$triBrush.Dispose()
$triPath.Dispose()

# Exclamation mark
$exclamBrush = [System.Drawing.SolidBrush]::new($white)
$exclamStem = Get-RoundedRectPath -X 58.5 -Y 46 -Width 11 -Height 28 -Radius 5.5
$canvas.Graphics.FillPath($exclamBrush, $exclamStem)
$exclamStem.Dispose()

# Exclamation dot
$canvas.Graphics.FillEllipse($exclamBrush, 58.5, 82.5, 11.0, 11.0)
$exclamBrush.Dispose()
Save-Icon -Canvas $canvas -Name 'exhausted.png'

# ==============================================================================
# 5. next.png - Azure/cyan fast-forward chevron
# ==============================================================================
$canvas = New-IconCanvas
$ffBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(28, 28),
    [System.Drawing.PointF]::new(100, 100),
    $azureLight,
    $cyanDeep
)
$ffPen = [System.Drawing.Pen]::new($ffBrush, 13.0)
$ffPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$ffPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$ffPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

# Chevron 1 (left)
$chevron1 = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(36, 36),
    [System.Drawing.PointF]::new(62, 64),
    [System.Drawing.PointF]::new(36, 92)
)
$canvas.Graphics.DrawLines($ffPen, $chevron1)

# Chevron 2 (right)
$chevron2 = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(66, 36),
    [System.Drawing.PointF]::new(92, 64),
    [System.Drawing.PointF]::new(66, 92)
)
$canvas.Graphics.DrawLines($ffPen, $chevron2)

$ffPen.Dispose()
$ffBrush.Dispose()
Save-Icon -Canvas $canvas -Name 'next.png'

# ==============================================================================
# 6. refresh.png - Dual-arc circular reload arrows with cyan accent
# ==============================================================================
$canvas = New-IconCanvas
$refreshBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(24, 24),
    [System.Drawing.PointF]::new(104, 104),
    $cyanLight,
    $cyanDeep
)
$arcPen = [System.Drawing.Pen]::new($refreshBrush, 9.5)
$arcPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$arcPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

# Arc 1: top clockwise from 195 to 330 (sweep 135)
$canvas.Graphics.DrawArc($arcPen, 28, 28, 72, 72, 195, 135)
# Arc 2: bottom clockwise from 15 to 150 (sweep 135)
$canvas.Graphics.DrawArc($arcPen, 28, 28, 72, 72, 15, 135)
$arcPen.Dispose()

# Tangential reload arrow heads
$headBrush = [System.Drawing.SolidBrush]::new($cyanDeep)

# Arrow 1 (top right)
$arrow1 = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(103, 56),
    [System.Drawing.PointF]::new(103, 34),
    [System.Drawing.PointF]::new(83, 46)
)
$canvas.Graphics.FillPolygon($headBrush, $arrow1)

# Arrow 2 (bottom left, 180 deg symmetry: (128-x, 128-y))
$arrow2 = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(128 - 103, 128 - 56),
    [System.Drawing.PointF]::new(128 - 103, 128 - 34),
    [System.Drawing.PointF]::new(128 - 83, 128 - 46)
)
$canvas.Graphics.FillPolygon($headBrush, $arrow2)

$headBrush.Dispose()
$refreshBrush.Dispose()
Save-Icon -Canvas $canvas -Name 'refresh.png'

# ==============================================================================
# 7. add.png - Clean emerald plus glyph
# ==============================================================================
$canvas = New-IconCanvas
$plusBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(28, 28),
    [System.Drawing.PointF]::new(100, 100),
    $emeraldLight,
    $emeraldDark
)
$plusPen = [System.Drawing.Pen]::new($plusBrush, 14.0)
$plusPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$plusPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

# Horizontal bar
$canvas.Graphics.DrawLine($plusPen, 32.0, 64.0, 96.0, 64.0)
# Vertical bar
$canvas.Graphics.DrawLine($plusPen, 64.0, 32.0, 64.0, 96.0)

$plusPen.Dispose()
$plusBrush.Dispose()
Save-Icon -Canvas $canvas -Name 'add.png'

# ==============================================================================
# 8. remove.png - Clean rose minus glyph
# ==============================================================================
$canvas = New-IconCanvas
$minusBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(32, 64),
    [System.Drawing.PointF]::new(96, 64),
    $roseLight,
    $roseDark
)
$minusPen = [System.Drawing.Pen]::new($minusBrush, 14.0)
$minusPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$minusPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

# Horizontal bar
$canvas.Graphics.DrawLine($minusPen, 32.0, 64.0, 96.0, 64.0)

$minusPen.Dispose()
$minusBrush.Dispose()
Save-Icon -Canvas $canvas -Name 'remove.png'

Write-Host "Fluent 128x128 icon generation complete."
