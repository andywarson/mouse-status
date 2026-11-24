using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

internal sealed class TaskbarStatus : Form
{
    private const uint RaiseWithoutActivation = 0x0213;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;

    private readonly ToolTip help = new ToolTip();
    private readonly Timer layoutTimer;
    private readonly Font upperFont = new Font("Segoe UI Semibold", 8.8f, FontStyle.Bold);
    private readonly Font lowerFont = new Font("Microsoft YaHei UI", 7.8f);

    private ThemePalette palette = ThemePalette.Create(0, null);
    private Image avatarImage;
    private string upperText = "—";
    private string lowerText = "读取中";
    private int? batteryLevel;
    private bool isCharging;
    private bool hovered;
    private bool active;
    private bool locating;

    private static IntPtr cachedTaskbarHwnd;
    private static AutomationElement cachedAnchor;
    private static int cachedOccupiedX;
    private static int scanTickCounter;

    internal TaskbarStatus(Action openSettings)
    {
        Text = "鼠标任务栏状态";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        Click += delegate { openSettings(); };
        MouseEnter += delegate { hovered = true; Invalidate(); };
        MouseLeave += delegate { hovered = false; Invalidate(); };

        layoutTimer = new Timer { Interval = 2000 };
        layoutTimer.Tick += async delegate { await UpdatePositionSafe(); };
    }

    internal void ApplyAppearance(ThemePalette theme, Image image)
    {
        palette = theme;
        avatarImage = image;
        int oldWidth = Width;
        int targetWidth = UiKit.S(image == null ? 92 : 118);
        int targetHeight = UiKit.S(38);
        ClientSize = new Size(targetWidth, targetHeight);
        if (Visible && oldWidth > 0) Left += oldWidth - Width;
        BackColor = theme.AccentTile;
        ForeColor = theme.Ink;
        if (IsHandleCreated) UiKit.ApplyWindowChrome(this, theme, true, true);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiKit.ApplyWindowChrome(this, palette, true, true);
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return parameters;
        }
    }

    internal async void SetActive(bool value)
    {
        if (active == value) return;
        active = value;
        if (active)
        {
            scanTickCounter = 0;
            layoutTimer.Start();
            await UpdatePositionSafe();
        }
        else
        {
            layoutTimer.Stop();
            Hide();
        }
    }

    internal void SetStatus(string first, string second, int? battery, bool charging, string tooltip)
    {
        upperText = first ?? "—";
        lowerText = second ?? "";
        batteryLevel = battery;
        isCharging = charging;
        AccessibleDescription = upperText + " " + lowerText;
        help.SetToolTip(this, tooltip);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color bg = hovered ? palette.Hover : palette.AccentTile;
        g.Clear(bg);

        if (!UiKit.IsWindows11)
        {
            using (var border = new Pen(palette.AccentBorder, 1f))
                UiKit.DrawRounded(g, border, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), UiKit.S(9));
        }

        int contentLeft = UiKit.S(4);
        if (avatarImage != null)
        {
            int imgSz = UiKit.S(22);
            int imgX = UiKit.S(6);
            int imgY = (Height - imgSz) / 2;
            using (var clip = UiKit.RoundedPath(new RectangleF(imgX, imgY, imgSz, imgSz), UiKit.S(5)))
            {
                var state = g.Save();
                g.SetClip(clip);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(avatarImage, new Rectangle(imgX, imgY, imgSz, imgSz));
                g.Restore(state);
            }
            contentLeft = imgX + imgSz + UiKit.S(4);
        }

        int bodyWidth = Width - contentLeft - UiKit.S(4);
        var upperRect = new Rectangle(contentLeft, UiKit.S(2), bodyWidth, UiKit.S(18));
        var lowerRect = new Rectangle(contentLeft, UiKit.S(19), bodyWidth, UiKit.S(16));

        Color upperColor = isCharging ? palette.Success
                         : (batteryLevel.HasValue && batteryLevel.Value <= 15 ? palette.Danger : palette.Ink);

        if (isCharging)
        {
            // GDI has no reliable glyph for U+26A1, so draw the bolt ourselves left of the centred text.
            Size textSize = TextRenderer.MeasureText(g, upperText, upperFont, upperRect.Size, TextFormatFlags.NoPadding);
            int boltW = UiKit.S(7), boltH = UiKit.S(11), gap = UiKit.S(2);
            int groupLeft = upperRect.Left + Math.Max(0, (upperRect.Width - textSize.Width - boltW - gap) / 2);
            UiKit.DrawBolt(g, new RectangleF(groupLeft, upperRect.Top + (upperRect.Height - boltH) / 2f, boltW, boltH), upperColor);
            upperRect = new Rectangle(groupLeft + boltW + gap, upperRect.Top, upperRect.Right - groupLeft - boltW - gap, upperRect.Height);
            TextRenderer.DrawText(g, upperText, upperFont, upperRect, upperColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
        else
        {
            TextRenderer.DrawText(g, upperText, upperFont, upperRect, upperColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
        TextRenderer.DrawText(g, lowerText, lowerFont, lowerRect, palette.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        if (batteryLevel.HasValue)
        {
            int barMargin = UiKit.S(10);
            int barWidth = Width - barMargin * 2;
            if (barWidth > 12)
            {
                int barY = Height - UiKit.S(3);
                Color fillCol = isCharging ? palette.Success
                              : batteryLevel.Value <= 15 ? palette.Danger
                              : batteryLevel.Value <= 25 ? palette.Warning
                              : palette.Accent;
                using (var trackBrush = new SolidBrush(Color.FromArgb(40, palette.Muted)))
                using (var fillBrush = new SolidBrush(fillCol))
                {
                    g.FillRectangle(trackBrush, barMargin, barY, barWidth, UiKit.S(2));
                    int w = Math.Max(2, barWidth * Math.Max(0, Math.Min(100, batteryLevel.Value)) / 100);
                    g.FillRectangle(fillBrush, barMargin, barY, w, UiKit.S(2));
                }
            }
        }
    }

    private async Task UpdatePositionSafe()
    {
        if (!active || locating || IsDisposed) return;
        locating = true;
        try
        {
            var location = await Task.Run(() => Locate()).ConfigureAwait(true);
            if (!active || IsDisposed) return;
            if (location == null) { Hide(); return; }

            NativePoint anchor = location.Anchor;
            NativePoint limit = location.Limit;
            var hwnd = Handle;
            PhysicalToLogicalPointForPerMonitorDPI(hwnd, ref anchor);
            PhysicalToLogicalPointForPerMonitorDPI(hwnd, ref limit);
            int left = anchor.X - Width - UiKit.S(6);
            if (left < limit.X + UiKit.S(8)) { Hide(); return; }

            Point nextLoc = new Point(left, anchor.Y - Height / 2);
            if (Location != nextLoc) Location = nextLoc;
            if (!Visible)
            {
                Show();
                UiKit.ApplyWindowChrome(this, palette, true, true);
            }
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, RaiseWithoutActivation);
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        finally { locating = false; }
    }

    private sealed class LocationData
    {
        internal NativePoint Anchor;
        internal NativePoint Limit;
    }

    private static bool IsFullscreenAppCoveringPrimary(IntPtr taskbar)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == taskbar || !IsWindowVisible(foreground))
            return false;

        var classBuilder = new StringBuilder(64);
        if (GetClassName(foreground, classBuilder, classBuilder.Capacity) > 0)
        {
            string cls = classBuilder.ToString();
            if (cls == "Progman" || cls == "WorkerW" ||
                cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                return false;
        }

        int exStyle = GetWindowLong(foreground, GwlExStyle);
        if ((exStyle & WsExToolWindow) != 0) return false;

        var monitor = Screen.PrimaryScreen.Bounds;
        NativeRect bounds;
        return GetWindowRect(foreground, out bounds) &&
               bounds.Left <= monitor.Left && bounds.Right >= monitor.Right &&
               bounds.Top <= monitor.Top && bounds.Bottom >= monitor.Bottom;
    }

    private static LocationData Locate()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !IsWindowVisible(taskbar)) return null;
        if (IsFullscreenAppCoveringPrimary(taskbar)) return null;

        try
        {
            // Fast path: reuse cached anchor element between full scans
            scanTickCounter++;
            if (taskbar == cachedTaskbarHwnd && cachedAnchor != null && scanTickCounter % 5 != 1)
            {
                try
                {
                    var fastRect = cachedAnchor.Current.BoundingRectangle;
                    if (!fastRect.IsEmpty && fastRect.Width > 0)
                    {
                        return new LocationData
                        {
                            Anchor = new NativePoint { X = (int)fastRect.Left, Y = (int)(fastRect.Top + fastRect.Height / 2) },
                            Limit = new NativePoint { X = cachedOccupiedX }
                        };
                    }
                }
                catch (ElementNotAvailableException) { cachedAnchor = null; }
            }

            var nativeTaskbar = AutomationElement.FromHandle(taskbar);
            var bar = nativeTaskbar.Current.BoundingRectangle;
            if (bar.IsEmpty || bar.Height < 20 || bar.Width < bar.Height * 3) return null;

            var cache = new CacheRequest();
            cache.Add(AutomationElement.BoundingRectangleProperty);
            cache.Add(AutomationElement.IsOffscreenProperty);
            cache.Add(AutomationElement.ClassNameProperty);
            cache.Add(AutomationElement.NameProperty);

            AutomationElementCollection children;
            using (cache.Activate())
            {
                children = nativeTaskbar.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            }

            System.Windows.Rect trayStart = System.Windows.Rect.Empty;
            AutomationElement trayStartEl = null;
            System.Windows.Rect caret = System.Windows.Rect.Empty;
            AutomationElement caretEl = null;
            double occupied = bar.Left;

            foreach (AutomationElement element in children)
            {
                var item = element.Cached;
                var rect = item.BoundingRectangle;
                if (rect.IsEmpty || rect.Width <= 0 || item.IsOffscreen) continue;
                string cls = item.ClassName ?? "";
                if (cls.StartsWith("SystemTray.", StringComparison.Ordinal))
                {
                    if (trayStart.IsEmpty || rect.Left < trayStart.Left)
                    {
                        trayStart = rect;
                        trayStartEl = element;
                    }
                    // Match by localized name only; this lookup was verified on zh-CN/en-US Explorer.
                    string nm = item.Name ?? "";
                    if (nm == "显示隐藏的图标" || nm == "顯示隱藏的圖示" || nm == "Show hidden icons")
                    {
                        caret = rect;
                        caretEl = element;
                    }
                }
                else if (cls.StartsWith("Taskbar.TaskListButton", StringComparison.Ordinal) || cls == "ToggleButton")
                {
                    occupied = Math.Max(occupied, rect.Right);
                }
            }

            var edge = caret.IsEmpty ? trayStart : caret;
            if (edge.IsEmpty) return null;

            cachedTaskbarHwnd = taskbar;
            cachedAnchor = caretEl ?? trayStartEl;
            cachedOccupiedX = (int)occupied;

            return new LocationData
            {
                Anchor = new NativePoint { X = (int)edge.Left, Y = (int)(edge.Top + edge.Height / 2) },
                Limit = new NativePoint { X = cachedOccupiedX }
            };
        }
        catch (ElementNotAvailableException) { cachedAnchor = null; return null; }
        catch (InvalidOperationException) { cachedAnchor = null; return null; }
        catch (COMException) { cachedAnchor = null; return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { internal int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string name, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern bool PhysicalToLogicalPointForPerMonitorDPI(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            layoutTimer.Dispose();
            help.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            upperFont.Dispose();
            lowerFont.Dispose();
        }
    }
}
