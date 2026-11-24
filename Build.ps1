param([switch]$Test, [string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'

$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$meta = Join-Path $env:WINDIR 'System32\WinMetadata'
$srcDir = Join-Path $PSScriptRoot 'src'
$testDir = Join-Path $PSScriptRoot 'tests'

function Ensure-AppIcon([string]$iconPath) {
    if (Test-Path $iconPath) { return }
    Add-Type -AssemblyName System.Drawing
    $sizes = @(16, 32, 48, 64)
    $frames = New-Object System.Collections.Generic.List[byte[]]
    foreach ($sz in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $ms = New-Object System.IO.MemoryStream
        try {
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $g.Clear([System.Drawing.Color]::Transparent)
            $brushBg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                (New-Object System.Drawing.Rectangle(0, 0, $sz, $sz)),
                [System.Drawing.Color]::FromArgb(99, 102, 241),
                [System.Drawing.Color]::FromArgb(59, 130, 246),
                45.0)
            $pad = [float]($sz * 0.06)
            $g.FillEllipse($brushBg, $pad, $pad, [float]($sz - 2 * $pad), [float]($sz - 2 * $pad))
            $brushBg.Dispose()

            $mw = [float]($sz * 0.42)
            $mh = [float]($sz * 0.62)
            $mx = [float](($sz - $mw) / 2.0)
            $my = [float](($sz - $mh) / 2.0)
            $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(1.4, $sz * 0.07))
            $path = New-Object System.Drawing.Drawing2D.GraphicsPath
            $r = $mw
            $path.AddArc($mx, $my, $r, $r, 180, 180)
            $path.AddArc($mx, $my + $mh - $r, $r, $r, 0, 180)
            $path.CloseFigure()
            $g.DrawPath($pen, $path)
            $wheelY1 = $my + $mh * 0.22
            $wheelY2 = $my + $mh * 0.40
            $g.DrawLine($pen, ($sz / 2.0), $wheelY1, ($sz / 2.0), $wheelY2)
            $path.Dispose()
            $pen.Dispose()

            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($ms.ToArray())
        } finally {
            $ms.Dispose()
            $g.Dispose()
            $bmp.Dispose()
        }
    }

    $out = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($out)
    try {
        $bw.Write([uint16]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dim = if ($sizes[$i] -ge 256) { [byte]0 } else { [byte]$sizes[$i] }
            $bytes = $frames[$i]
            $bw.Write($dim)
            $bw.Write($dim)
            $bw.Write([byte]0)
            $bw.Write([byte]0)
            $bw.Write([uint16]1)
            $bw.Write([uint16]32)
            $bw.Write([uint32]$bytes.Length)
            $bw.Write([uint32]$offset)
            $offset += $bytes.Length
        }
        foreach ($bytes in $frames) { $bw.Write($bytes) }
        [System.IO.File]::WriteAllBytes($iconPath, $out.ToArray())
    } finally {
        $bw.Dispose()
        $out.Dispose()
    }
}

$references = @(
    '/r:System.Windows.Forms.dll',
    '/r:System.Drawing.dll',
    "/r:$fx\WPF\WindowsBase.dll",
    "/r:$fx\WPF\UIAutomationClient.dll",
    "/r:$fx\WPF\UIAutomationTypes.dll",
    "/r:$fx\System.Runtime.dll",
    "/r:$meta\Windows.Foundation.winmd",
    "/r:$meta\Windows.Devices.winmd",
    "/r:$meta\Windows.Storage.winmd"
)

$names = @(
    'Program.cs',
    'UiKit.cs',
    'Appearance.cs',
    'Notifications.cs',
    'TaskbarStatus.cs',
    'SettingsWindow.cs',
    'History.cs',
    'StatisticsView.cs',
    'BluetoothStatus.cs',
    'Receiver.cs',
    'MouseProtocol.cs'
)
$sources = $names | ForEach-Object { Join-Path $srcDir $_ }
$compiler = Join-Path $fx 'csc.exe'

if ($Test) {
    $testExe = Join-Path ([System.IO.Path]::GetTempPath()) ('MaicongProtocolTests-' + [guid]::NewGuid().ToString('N') + '.exe')
    $testSrc = Join-Path $testDir 'ProtocolTests.cs'
    try {
        & $compiler /nologo /codepage:65001 /platform:x64 /main:ProtocolTests "/out:$testExe" @references @sources $testSrc
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & $testExe
        exit $LASTEXITCODE
    } finally {
        Remove-Item $testExe -ErrorAction SilentlyContinue
    }
}

$iconFile = Join-Path $srcDir 'app.ico'
Ensure-AppIcon $iconFile

$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$' -or
    ($version.Split('.') | Where-Object { [int]$_ -gt 65534 })) {
    throw 'VERSION must contain a numeric semantic version supported by Windows file metadata.'
}
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$binary = Join-Path (Resolve-Path -LiteralPath $OutputDirectory).Path 'MaicongMouseStatus.exe'
$assemblyInfo = Join-Path ([IO.Path]::GetTempPath()) ('MaicongVersion-' + [Guid]::NewGuid().ToString('N') + '.cs')
try {
    @"
[assembly: System.Reflection.AssemblyTitle("MaicongMouseStatus")]
[assembly: System.Reflection.AssemblyProduct("MaicongMouseStatus")]
[assembly: System.Reflection.AssemblyVersion("$version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$version.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$version")]
"@ | Set-Content -LiteralPath $assemblyInfo -Encoding ASCII
    & $compiler /nologo /codepage:65001 /platform:x64 /target:winexe "/win32icon:$iconFile" "/out:$binary" @references @sources $assemblyInfo
    exit $LASTEXITCODE
} finally {
    Remove-Item -LiteralPath $assemblyInfo -ErrorAction SilentlyContinue
}
