$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class VerifyWin32 {
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
'@

$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = if ($args.Count -gt 0) { (Resolve-Path -LiteralPath $args[0]).Path }
           else { Join-Path $repoRoot 'MouseStatus.exe' }
if (!(Test-Path $exePath)) {
    throw "Mouse Status executable not found at $exePath. Run .\Build.ps1 first."
}

$root = [Windows.Automation.AutomationElement]::RootElement
$descendants = [Windows.Automation.TreeScope]::Descendants
$children = [Windows.Automation.TreeScope]::Children

function Find-ByName($parent, $scope, $name) {
    $cond = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $name)
    $parent.FindFirst($scope, $cond)
}

function Find-SettingsWindow {
    $win = Find-ByName $root $children '鼠标状态设置'
    if (!$win) {
        $owner = Find-ByName $root $children '鼠标状态'
        if ($owner) { $win = Find-ByName $owner $descendants '鼠标状态设置' }
    }
    $win
}

function Invoke-Button($el) {
    if (!$el) { throw 'Target button element not found.' }
    $inv = $null
    if ($el.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) {
        ([Windows.Automation.InvokePattern]$inv).Invoke()
    } elseif ($el.Current.NativeWindowHandle -ne 0) {
        [void][VerifyWin32]::SendMessage([IntPtr]$el.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    } else {
        throw "Element '$($el.Current.Name)' has neither InvokePattern nor HWND."
    }
}

$existing = Get-Process -Name 'MaicongMouseStatus','MouseStatus','MouseStatus-*-portable' -ErrorAction SilentlyContinue
if ($existing) {
    throw 'Close any running Mouse Status instance before starting the interactive test.'
}

$proc = Start-Process -FilePath $exePath -PassThru -WindowStyle Hidden
$second = $null
try {
    Start-Sleep -Milliseconds 1500

    # Launching a second instance should wake and open Settings window automatically
    $second = Start-Process -FilePath $exePath -PassThru -WindowStyle Hidden
    if (!$second.WaitForExit(5000)) { throw 'Second instance did not exit after signaling.' }
    Start-Sleep -Milliseconds 900

    $settings = Find-SettingsWindow
    if (!$settings) { throw 'Second-instance wake did not open SettingsWindow.' }
    Write-Host "Verified: Second-instance wake opened SettingsWindow ($($settings.Current.BoundingRectangle))"

    # Switch to Appearance tab
    Invoke-Button (Find-ByName $settings $descendants '外观')
    Start-Sleep -Milliseconds 400
    Write-Host 'Verified: Appearance tab rendered'

    # Switch to Statistics tab and verify expansion
    Invoke-Button (Find-ByName $settings $descendants '统计')
    Start-Sleep -Milliseconds 500
    $settings = Find-SettingsWindow
    if ($settings.Current.BoundingRectangle.Width -lt 700) {
        throw "Statistics tab did not expand window width: $($settings.Current.BoundingRectangle)"
    }
    $lineChart = Find-ByName $settings $descendants '电量变化折线图'
    $rateDonut = Find-ByName $settings $descendants '回报率档位使用比例环形图'
    if (!$lineChart -or !$rateDonut) { throw 'Charts missing on Statistics tab.' }

    Invoke-Button (Find-ByName $settings $descendants '切换灵敏度占比图')
    Start-Sleep -Milliseconds 300
    $dpiDonut = Find-ByName $settings $descendants '灵敏度档位使用比例环形图'
    if (!$dpiDonut) { throw 'DPI donut mode did not switch.' }
    Write-Host 'Verified: Statistics tab Rate/DPI toggle & charts'

    Invoke-Button (Find-ByName $settings $descendants '取消')
    Start-Sleep -Milliseconds 300
    Write-Host 'UI verification succeeded.'
}
finally {
    if ($second -and !$second.HasExited) {
        $second | Stop-Process -Force
    }
    if ($proc -and !$proc.HasExited) {
        $proc | Stop-Process -Force
    }
}
