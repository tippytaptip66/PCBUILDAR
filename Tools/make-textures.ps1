param([string]$OutDir = "C:\AR\BuildAR\Assets\_Project\Art\Textures")

# Generates the surface textures for BuildAR's built-in component models: circuit board, brushed metal,
# painted steel, dark plastic and gold contacts. 512x512 PNGs, tuned to read well on a phone.

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$rand = New-Object System.Random 20260920
function C([int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb(255, [Math]::Max(0,[Math]::Min(255,$r)), [Math]::Max(0,[Math]::Min(255,$g)), [Math]::Max(0,[Math]::Min(255,$b))) }

function New-Canvas([int]$size, $base) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear($base)
    @{ bmp = $bmp; g = $g }
}

function Add-Speckle($bmp, [int]$amount, [int]$count) {
    for ($i = 0; $i -lt $count; $i++) {
        $x = $rand.Next($bmp.Width); $y = $rand.Next($bmp.Height)
        $p = $bmp.GetPixel($x, $y)
        $d = $rand.Next(-$amount, $amount + 1)
        $bmp.SetPixel($x, $y, (C ($p.R + $d) ($p.G + $d) ($p.B + $d)))
    }
}

# ---------------------------------------------------------------- circuit board
function New-Pcb($path, $base, $trace, $silk) {
    $c = New-Canvas 512 $base
    $g = $c.g

    # traces: mostly straight runs with right-angle turns, like a real board
    $tracePen = New-Object System.Drawing.Pen $trace, 3
    for ($i = 0; $i -lt 70; $i++) {
        $x = $rand.Next(512); $y = $rand.Next(512)
        $len = 40 + $rand.Next(150)
        if ($rand.Next(2) -eq 0) {
            $g.DrawLine($tracePen, $x, $y, [Math]::Min(511, $x + $len), $y)
            $g.DrawLine($tracePen, [Math]::Min(511, $x + $len), $y, [Math]::Min(511, $x + $len), [Math]::Min(511, $y + $rand.Next(80)))
        } else {
            $g.DrawLine($tracePen, $x, $y, $x, [Math]::Min(511, $y + $len))
            $g.DrawLine($tracePen, $x, [Math]::Min(511, $y + $len), [Math]::Min(511, $x + $rand.Next(80)), [Math]::Min(511, $y + $len))
        }
    }

    # solder pads and vias
    $padBrush = New-Object System.Drawing.SolidBrush (C 198 156 74)
    $holeBrush = New-Object System.Drawing.SolidBrush $base
    for ($i = 0; $i -lt 150; $i++) {
        $x = $rand.Next(500); $y = $rand.Next(500); $r = 4 + $rand.Next(4)
        $g.FillEllipse($padBrush, $x, $y, $r * 2, $r * 2)
        $g.FillEllipse($holeBrush, $x + $r - 2, $y + $r - 2, 4, 4)
    }

    # silkscreen marks
    $silkPen = New-Object System.Drawing.Pen $silk, 2
    for ($i = 0; $i -lt 22; $i++) {
        $x = $rand.Next(460); $y = $rand.Next(460)
        $g.DrawRectangle($silkPen, $x, $y, 18 + $rand.Next(40), 10 + $rand.Next(26))
    }

    Add-Speckle $c.bmp 10 24000
    $c.bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $c.bmp.Dispose()
    "  $([System.IO.Path]::GetFileName($path))"
}

# ---------------------------------------------------------------- brushed metal
function New-Metal($path, $base) {
    $c = New-Canvas 512 $base
    for ($i = 0; $i -lt 9000; $i++) {
        $y = $rand.Next(512); $x = $rand.Next(400); $len = 20 + $rand.Next(112)
        $d = $rand.Next(-14, 15)
        $pen = New-Object System.Drawing.Pen (C ($base.R + $d) ($base.G + $d) ($base.B + $d)), 1
        $c.g.DrawLine($pen, $x, $y, [Math]::Min(511, $x + $len), $y)
        $pen.Dispose()
    }
    $c.bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $c.g.Dispose(); $c.bmp.Dispose()
    "  $([System.IO.Path]::GetFileName($path))"
}

# ---------------------------------------------------------------- painted steel / plastic
function New-Grain($path, $base, [int]$amount, [int]$brush) {
    $c = New-Canvas 512 $base
    for ($i = 0; $i -lt $brush; $i++) {
        $y = $rand.Next(512); $x = $rand.Next(460); $d = $rand.Next(-6, 7)
        $pen = New-Object System.Drawing.Pen (C ($base.R + $d) ($base.G + $d) ($base.B + $d)), 1
        $c.g.DrawLine($pen, $x, $y, [Math]::Min(511, $x + 20 + $rand.Next(50)), $y)
        $pen.Dispose()
    }
    Add-Speckle $c.bmp $amount 45000
    $c.bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $c.g.Dispose(); $c.bmp.Dispose()
    "  $([System.IO.Path]::GetFileName($path))"
}

# ---------------------------------------------------------------- gold contacts
function New-Gold($path, $base) {
    $c = New-Canvas 512 $base
    for ($x = 0; $x -lt 512; $x += 16) {
        $d = if ((($x / 16) % 2) -eq 0) { 22 } else { -18 }
        $brush = New-Object System.Drawing.SolidBrush (C ($base.R + $d) ($base.G + $d) ($base.B + $d))
        $c.g.FillRectangle($brush, $x, 0, 12, 512)
        $brush.Dispose()
    }
    Add-Speckle $c.bmp 8 20000
    $c.bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $c.g.Dispose(); $c.bmp.Dispose()
    "  $([System.IO.Path]::GetFileName($path))"
}

"generated:"
New-Pcb   (Join-Path $OutDir "T_PCB_Green.png") (C 31 94 58)  (C 46 122 78)  (C 214 224 214)
New-Pcb   (Join-Path $OutDir "T_PCB_Black.png") (C 27 31 39)  (C 46 52 64)   (C 196 204 216)
New-Metal (Join-Path $OutDir "T_Metal.png")     (C 185 192 204)
New-Grain (Join-Path $OutDir "T_Case.png")      (C 18 20 23)  6  5000
New-Grain (Join-Path $OutDir "T_Plastic.png")   (C 34 38 46)   8  3000
New-Gold  (Join-Path $OutDir "T_Gold.png")      (C 212 166 58)
