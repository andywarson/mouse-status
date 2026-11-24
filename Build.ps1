param([switch]$Test)
$ErrorActionPreference = 'Stop'
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$meta = Join-Path $env:WINDIR 'System32\WinMetadata'
$references = @(
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    "/r:$fx\WPF\WindowsBase.dll", "/r:$fx\WPF\UIAutomationClient.dll", "/r:$fx\WPF\UIAutomationTypes.dll",
    "/r:$fx\System.Runtime.dll",
    "/r:$meta\Windows.Foundation.winmd", "/r:$meta\Windows.Devices.winmd", "/r:$meta\Windows.Storage.winmd"
)
$names = 'Program.cs','TaskbarStatus.cs','SettingsWindow.cs','Appearance.cs','History.cs','StatisticsView.cs','BluetoothStatus.cs','Receiver.cs','MouseProtocol.cs'
$sources = $names | ForEach-Object { Join-Path $PSScriptRoot $_ }
$compiler = Join-Path $fx 'csc.exe'
if ($Test) {
    $testPath = Join-Path $PSScriptRoot 'ProtocolTests.exe'
    & $compiler /nologo /codepage:65001 /platform:x64 /main:ProtocolTests "/out:$testPath" @references @sources (Join-Path $PSScriptRoot 'ProtocolTests.cs')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $testPath
    exit $LASTEXITCODE
}
$binary = Join-Path $PSScriptRoot 'MaicongMouseStatus.exe'
& $compiler /nologo /codepage:65001 /platform:x64 /target:winexe "/out:$binary" @references @sources
exit $LASTEXITCODE
