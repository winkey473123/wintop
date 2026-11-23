param([switch]$Install, [switch]$Icon)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'wintop.exe'
$iconPath = Join-Path $root 'app.ico'
$svg = Join-Path $root 'thumbtack.svg'

# 从 thumbtack.svg 生成 app.ico：16~64 是 32bpp BMP 条目（System.Drawing.Icon 只认这种，托盘要用），256 是 PNG 条目。
function New-Icon {
    Add-Type -AssemblyName PresentationCore, WindowsBase
    Add-Type -TypeDefinition @'
using System;
using System.IO;

public static class IcoBmp
{
    public static byte[] Entry(byte[] pbgra, int size)
    {
        int stride = size * 4;
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(40); bw.Write(size); bw.Write(size * 2);
        bw.Write((short)1); bw.Write((short)32);
        bw.Write(0); bw.Write(size * size * 4);
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

        for (int y = size - 1; y >= 0; y--)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * stride + x * 4;
                byte b = pbgra[i], g = pbgra[i + 1], r = pbgra[i + 2], a = pbgra[i + 3];
                if (a > 0 && a < 255)   // 反预乘
                {
                    r = (byte)Math.Min(255, r * 255 / a);
                    g = (byte)Math.Min(255, g * 255 / a);
                    b = (byte)Math.Min(255, b * 255 / a);
                }
                bw.Write(b); bw.Write(g); bw.Write(r); bw.Write(a);
            }
        }
        byte[] maskRow = new byte[((size + 31) / 32) * 4];
        for (int y = 0; y < size; y++) bw.Write(maskRow);
        bw.Flush();
        return ms.ToArray();
    }
}
'@

    $text = Get-Content -Raw -Path $svg
    $d = [regex]::Match($text, '<path\b[^>]*\bd="([^"]+)"')
    if (-not $d.Success) { throw "SVG 里没找到 path d=：$svg" }
    $sw = [regex]::Match($text, 'stroke-width="([\d.]+)"')
    $strokeWidth = if ($sw.Success) { [double]$sw.Groups[1].Value } else { 2.0 }
    $vb = [regex]::Match($text, 'viewBox="([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)"')
    $boxW = if ($vb.Success) { [double]$vb.Groups[3].Value } else { 24.0 }
    $boxH = if ($vb.Success) { [double]$vb.Groups[4].Value } else { 24.0 }

    $brush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0, 120, 212))
    $brush.Freeze()
    $geo = [System.Windows.Media.Geometry]::Parse($d.Groups[1].Value)
    $geo.Freeze()

    function Draw([int]$size, [bool]$png) {
        $visual = New-Object System.Windows.Media.DrawingVisual
        $dc = $visual.RenderOpen()
        $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform (($size - 1) / $boxW), (($size - 1) / $boxH)))
        $pen = New-Object System.Windows.Media.Pen $brush, $strokeWidth
        $pen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
        $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
        $pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
        $dc.DrawGeometry($null, $pen, $geo)
        $dc.Pop(); $dc.Close()

        $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
        $rtb.Render($visual)

        if ($png) {
            $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
            $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
            $ms = New-Object System.IO.MemoryStream
            $enc.Save($ms)
            return ,$ms.ToArray()
        }
        $buf = New-Object byte[] ($size * $size * 4)
        $rtb.CopyPixels((New-Object System.Windows.Int32Rect 0, 0, $size, $size), $buf, $size * 4, 0)
        return ,[IcoBmp]::Entry($buf, $size)
    }

    $sizes = @(16, 20, 24, 32, 40, 48, 64)
    $blobs = New-Object System.Collections.ArrayList
    $sizes | ForEach-Object { $null = $blobs.Add((Draw $_ $false)) }
    $null = $blobs.Add((Draw 256 $true))
    $sizes += 256

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
        $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
        $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$blobs[$i].Length); $bw.Write([uint32]$offset)
        $offset += $blobs[$i].Length
    }
    foreach ($b in $blobs) { $bw.Write($b) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($iconPath, $ms.ToArray())
    $bw.Dispose(); $ms.Dispose()
    '图标已生成：{0}（{1:N0} 字节）' -f $iconPath, (Get-Item $iconPath).Length
}

if ($Icon -or -not (Test-Path $iconPath)) { New-Icon }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '没找到 csc.exe（.NET Framework 4.x 缺失）' }

Get-Process wintop -ErrorAction SilentlyContinue | Stop-Process -Force   # 运行中的 exe 无法被覆盖
Start-Sleep -Milliseconds 300

& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/win32icon:$iconPath" "/out:$out" `
    /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    (Join-Path $root 'wintop.cs') 2>&1 | Where-Object { $_ -notmatch 'warning CS' }
if ($LASTEXITCODE) { throw '编译失败' }

$p = Start-Process -FilePath $out -ArgumentList '--self-test' -Wait -PassThru
if ($p.ExitCode) { throw "自检失败，退出码 $($p.ExitCode)" }
'编译完成：{0}（{1:N0} 字节），自检通过' -f $out, (Get-Item $out).Length

if ($Install) {
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name WindowTop -ErrorAction SilentlyContinue
    Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name wintop -Value ('"' + $out + '"')
    Start-Process -FilePath $out
    '已注册开机自启（wintop）并启动'
}
