using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class AlertNotice
{
    internal readonly string Title;
    internal readonly string Body;
    internal readonly ToolTipIcon Level;

    internal AlertNotice(string title, string body, ToolTipIcon level)
    {
        Title = title;
        Body = body;
        Level = level;
    }
}

internal sealed class BatteryAlertTracker
{
    private string trackedDevice;
    private bool lowWarned;
    private bool criticalWarned;
    private bool fullNotified;
    private bool hasBaselineReading;
    private string baselineDpi;
    private int? baselineStage;
    private int? baselineStageCount;
    private int? baselineRate;

    internal AlertNotice EvaluateBattery(string deviceId, string deviceTitle, MouseStatus status, int lowThreshold, bool fullAlertEnabled)
    {
        if (status == null || !status.Battery.HasValue || string.IsNullOrEmpty(deviceId))
            return null;

        if (!string.Equals(trackedDevice, deviceId, StringComparison.Ordinal))
        {
            trackedDevice = deviceId;
            lowWarned = false;
            criticalWarned = false;
            fullNotified = false;
            hasBaselineReading = false;
        }

        int pct = status.Battery.Value;
        bool charging = status.Charging == true;
        string name = string.IsNullOrWhiteSpace(deviceTitle) ? "鼠标" : deviceTitle;

        if (charging)
        {
            lowWarned = false;
            criticalWarned = false;
            if (pct < 95) fullNotified = false;
            if (fullAlertEnabled && pct >= 100 && !fullNotified)
            {
                fullNotified = true;
                return new AlertNotice(name + " 已充满电", "当前电量 100%，可拔下充电线使用无线模式。", ToolTipIcon.Info);
            }
            return null;
        }

        fullNotified = false;
        if (lowThreshold <= 0) return null;
        if (pct >= lowThreshold + 5)
        {
            lowWarned = false;
            criticalWarned = false;
            return null;
        }

        if (pct <= 10 && lowThreshold > 10 && !criticalWarned)
        {
            criticalWarned = true;
            lowWarned = true;
            return new AlertNotice(name + " 电量极低 (" + pct.ToString(CultureInfo.InvariantCulture) + "%)",
                "鼠标即将耗尽断连，建议立即连接充电线。", ToolTipIcon.Warning);
        }

        if (pct <= lowThreshold && !lowWarned)
        {
            lowWarned = true;
            if (pct <= 10) criticalWarned = true;
            return new AlertNotice(name + " 电量偏低 (" + pct.ToString(CultureInfo.InvariantCulture) + "%)",
                "已低于设定的 " + lowThreshold.ToString(CultureInfo.InvariantCulture) + "% 提醒阈值，请注意及时充电。", ToolTipIcon.Warning);
        }

        return null;
    }

    internal bool EvaluateGearChange(string deviceId, MouseStatus status, out string headline, out string detail)
    {
        headline = null;
        detail = null;
        if (status == null || string.IsNullOrEmpty(deviceId) || (!status.Dpi.HasValue && !status.Rate.HasValue))
            return false;

        string currentDpi = status.DpiDisplay;
        int? currentRate = status.Rate;

        if (!string.Equals(trackedDevice, deviceId, StringComparison.Ordinal))
        {
            trackedDevice = deviceId;
            hasBaselineReading = false;
        }

        if (!hasBaselineReading)
        {
            hasBaselineReading = true;
            baselineDpi = currentDpi;
            baselineStage = status.DpiStage;
            baselineStageCount = status.DpiStageCount;
            baselineRate = currentRate;
            return false;
        }

        bool dpiChanged = currentDpi != null && baselineDpi != null && !string.Equals(currentDpi, baselineDpi, StringComparison.Ordinal);
        bool rateChanged = currentRate.HasValue && baselineRate.HasValue && currentRate.Value != baselineRate.Value;

        if (currentDpi != null)
        {
            baselineDpi = currentDpi;
            baselineStage = status.DpiStage;
            baselineStageCount = status.DpiStageCount;
        }
        if (currentRate.HasValue) baselineRate = currentRate;

        if (!dpiChanged && !rateChanged) return false;

        if (dpiChanged && rateChanged)
        {
            headline = currentDpi + " DPI · " + currentRate.Value.ToString(CultureInfo.InvariantCulture) + " Hz";
            detail = FormatStageSubtitle(status);
        }
        else if (dpiChanged)
        {
            headline = "灵敏度 · " + currentDpi + " DPI";
            detail = FormatStageSubtitle(status);
            if (currentRate.HasValue)
                detail = string.IsNullOrEmpty(detail) ? (currentRate.Value.ToString(CultureInfo.InvariantCulture) + " Hz") : (detail + " · " + currentRate.Value.ToString(CultureInfo.InvariantCulture) + " Hz");
        }
        else
        {
            headline = "回报率 · " + currentRate.Value.ToString(CultureInfo.InvariantCulture) + " Hz";
            detail = currentDpi != null ? ("当前灵敏度 " + currentDpi + " DPI") : "无线配置已更新";
        }
        return true;
    }

    private static string FormatStageSubtitle(MouseStatus status)
    {
        if (status.DpiStage.HasValue && status.DpiStageCount.HasValue)
            return string.Format(CultureInfo.InvariantCulture, "档位 {0}/{1}", status.DpiStage.Value, status.DpiStageCount.Value);
        return "鼠标档位已切换";
    }
}

internal static class TrayIconRenderer
{
    internal static Icon Create(int? level, bool? charging, ThemePalette palette, bool numberMode, int lowThreshold)
    {
        return numberMode
            ? CreateNumberIcon(level, charging, palette, lowThreshold)
            : CreateBatteryIcon(level, charging, palette);
    }

    private static Icon CreateBatteryIcon(int? level, bool? charging, ThemePalette palette)
    {
        Color badgeBg = charging == true ? palette.Success
                      : (level.HasValue && level.Value <= 15 ? palette.Danger : palette.Accent);
        Color fg = (charging == true || (level.HasValue && level.Value <= 15))
            ? (IsLight(badgeBg) ? Color.FromArgb(20, 22, 28) : Color.White)
            : palette.OnAccent;

        using (var image = new Bitmap(32, 32))
        using (var paint = Graphics.FromImage(image))
        using (var background = new SolidBrush(badgeBg))
        using (var inkBrush = new SolidBrush(fg))
        using (var outline = new Pen(fg, 1.8f))
        {
            paint.SmoothingMode = SmoothingMode.AntiAlias;
            paint.FillEllipse(background, 0, 0, 31, 31);
            paint.DrawRectangle(outline, 5, 10, 19, 11);
            paint.FillRectangle(inkBrush, 25, 13, 2, 5);
            if (charging == true)
            {
                paint.FillPolygon(inkBrush, new[]
                {
                    new Point(16, 7), new Point(11, 16), new Point(15, 16),
                    new Point(13, 25), new Point(20, 13), new Point(16, 13)
                });
            }
            else if (level.HasValue)
            {
                float w = Math.Max(1.5f, 13f * Math.Max(0, Math.Min(100, level.Value)) / 100f);
                paint.FillRectangle(inkBrush, 8, 13, w, 5);
            }
            else
            {
                paint.DrawLine(outline, 11, 15.5f, 18, 15.5f);
            }
            return ToManagedIcon(image);
        }
    }

    private static Icon CreateNumberIcon(int? level, bool? charging, ThemePalette palette, int lowThreshold)
    {
        int warnAt = lowThreshold > 0 ? lowThreshold : 15;
        Color badgeBg = charging == true ? palette.Success
                      : (level.HasValue && level.Value <= warnAt ? palette.Danger : palette.Accent);
        Color fg = IsLight(badgeBg) ? Color.FromArgb(18, 20, 26) : Color.White;

        string label = !level.HasValue ? "--"
                     : level.Value >= 100 ? "99+"
                     : Math.Max(0, level.Value).ToString(CultureInfo.InvariantCulture);

        float fontSize = label.Length >= 3 ? 10.5f : 14f;
        using (var image = new Bitmap(32, 32))
        using (var paint = Graphics.FromImage(image))
        using (var bgBrush = new SolidBrush(badgeBg))
        using (var fgBrush = new SolidBrush(fg))
        using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
        {
            paint.SmoothingMode = SmoothingMode.AntiAlias;
            paint.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            UiKit.FillRounded(paint, bgBrush, new RectangleF(1f, 2f, 30f, 28f), 8f);
            paint.DrawString(label, font, fgBrush, new RectangleF(0f, charging == true ? 3f : 1.5f, 32f, 29f), fmt);
            if (charging == true)
            {
                using (var dotPen = new Pen(badgeBg, 1.2f))
                {
                    paint.FillEllipse(fgBrush, 22.5f, 3.5f, 5.5f, 5.5f);
                    paint.DrawEllipse(dotPen, 22.5f, 3.5f, 5.5f, 5.5f);
                }
            }
            return ToManagedIcon(image);
        }
    }

    private static bool IsLight(Color c)
    {
        double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return lum > 0.62;
    }

    private static Icon ToManagedIcon(Bitmap bitmap)
    {
        IntPtr native = bitmap.GetHicon();
        try { using (var icon = Icon.FromHandle(native)) return (Icon)icon.Clone(); }
        finally { DestroyIcon(native); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}

internal sealed class OsdToast : Form
{
    private readonly Font headFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
    private readonly Font subFont = new Font("Microsoft YaHei UI", 8.5f);
    private readonly Timer holdTimer;
    private readonly Timer fadeTimer;
    private ThemePalette palette = ThemePalette.Create(5, null);
    private string headline = "";
    private string subtitle = "";
    private double currentAlpha = 0.96;

    internal OsdToast()
    {
        Text = "鼠标档位提示";
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        holdTimer = new Timer { Interval = 1600 };
        holdTimer.Tick += delegate
        {
            holdTimer.Stop();
            fadeTimer.Start();
        };

        fadeTimer = new Timer { Interval = 30 };
        fadeTimer.Tick += delegate
        {
            currentAlpha -= 0.12;
            if (currentAlpha <= 0.05)
            {
                fadeTimer.Stop();
                Hide();
            }
            else
            {
                Opacity = currentAlpha;
            }
        };
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_LAYERED
            cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020 | 0x00080000;
            return cp;
        }
    }

    internal void ShowToast(ThemePalette theme, string title, string detail)
    {
        if (IsDisposed) return;
        palette = theme;
        headline = title ?? "";
        subtitle = detail ?? "";

        int w = UiKit.S(256);
        int h = UiKit.S(56);
        ClientSize = new Size(w, h);
        BackColor = theme.Surface;

        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - UiKit.S(48));

        fadeTimer.Stop();
        holdTimer.Stop();
        currentAlpha = 0.95;
        Opacity = currentAlpha;
        if (!Visible) Show();
        UiKit.ApplyWindowChrome(this, theme, true, false);
        Invalidate();
        holdTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(palette.Surface);

        if (!UiKit.IsWindows11)
        {
            using (var pen = new Pen(palette.AccentBorder, 1f))
                UiKit.DrawRounded(g, pen, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), UiKit.S(12));
        }

        int pad = UiKit.S(12);
        int badgeSz = UiKit.S(32);
        UiKit.DrawMouseBadge(g, new Rectangle(pad, (Height - badgeSz) / 2, badgeSz, badgeSz), palette);

        int textLeft = pad + badgeSz + UiKit.S(10);
        int textWidth = Width - textLeft - pad;
        TextRenderer.DrawText(g, headline, headFont,
            new Rectangle(textLeft, UiKit.S(8), textWidth, UiKit.S(22)),
            palette.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, subtitle, subFont,
            new Rectangle(textLeft, UiKit.S(30), textWidth, UiKit.S(18)),
            palette.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            holdTimer.Dispose();
            fadeTimer.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            headFont.Dispose();
            subFont.Dispose();
        }
    }
}
