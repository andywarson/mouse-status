using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

internal static class Program
{
    private const string MutexName = @"Local\MaicongMouseStatus";
    private const string WakeEventName = @"Local\MaicongMouseStatus.Wake";

    [STAThread]
    private static void Main()
    {
        UiKit.EnableHighDpi();
        bool firstInstance;
        using (var instance = new Mutex(true, MutexName, out firstInstance))
        {
            if (!firstInstance)
            {
                try
                {
                    using (var wake = EventWaitHandle.OpenExisting(WakeEventName))
                        wake.Set();
                }
                catch (WaitHandleCannotBeOpenedException) { }
                catch (UnauthorizedAccessException) { }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName))
            using (var context = new ApplicationContext())
            using (var widget = new StatusWidget())
            {
                RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(
                    wakeEvent,
                    delegate { widget.WakeFromSecondInstance(); },
                    null, -1, false);
                try
                {
                    widget.FormClosed += delegate { context.ExitThread(); };
                    widget.Start();
                    Application.Run(context);
                }
                finally
                {
                    registration.Unregister(null);
                }
            }
        }
    }
}

internal sealed class MetricCard : Control
{
    private readonly Font captionFont = new Font("Microsoft YaHei UI", 8.2f);
    private readonly Font valueFont = new Font("Segoe UI Semibold", 17f, FontStyle.Bold);
    private readonly Font mediumValueFont = new Font("Segoe UI Semibold", 13.5f, FontStyle.Bold);
    private readonly Font compactFont = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);

    internal bool IsAccentCard;
    internal bool CompactMode;
    internal string CaptionText = "";
    internal string ValueText = "—";
    internal int? ProgressPercent;
    internal bool ShowChargingBolt;
    private ThemePalette palette = ThemePalette.Create(0, null);

    internal MetricCard(string caption, bool isAccent)
    {
        CaptionText = caption;
        IsAccentCard = isAccent;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        UpdateAccessibility();
    }

    internal void ApplyState(ThemePalette nextPalette, bool compact, string caption, string value, int? progress, bool charging)
    {
        palette = nextPalette;
        CompactMode = compact;
        CaptionText = caption;
        ValueText = value ?? "—";
        ProgressPercent = progress;
        ShowChargingBolt = charging;
        BackColor = nextPalette.Surface;
        UpdateAccessibility();
        Invalidate();
    }

    private void UpdateAccessibility()
    {
        AccessibleName = CompactMode ? ValueText : (CaptionText + " " + ValueText);
        AccessibleDescription = ValueText;
        Text = AccessibleName;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color parentBg = Parent != null ? Parent.BackColor : palette.Surface;
        g.Clear(parentBg);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = Math.Min(UiKit.S(palette.TileRadius), Math.Min(rect.Width, rect.Height) / 2f);
        Color fill = IsAccentCard ? palette.AccentTile : palette.NeutralTile;
        Color stroke = IsAccentCard ? palette.AccentBorder : palette.Border;

        using (var bg = new SolidBrush(fill))
        using (var pen = new Pen(stroke, 1f))
        {
            UiKit.FillRounded(g, bg, rect, radius);
            UiKit.DrawRounded(g, pen, rect, radius);
        }

        if (CompactMode)
        {
            int textLeft = UiKit.S(8);
            if (ShowChargingBolt)
            {
                int bw = UiKit.S(10);
                int bh = UiKit.S(13);
                UiKit.DrawBolt(g, new RectangleF(UiKit.S(7), (Height - bh) / 2f, bw, bh), palette.Success);
                textLeft = UiKit.S(19);
            }
            TextRenderer.DrawText(g, ValueText, compactFont,
                new Rectangle(textLeft, 0, Width - textLeft - UiKit.S(6), Height),
                palette.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            return;
        }

        var capRect = new Rectangle(UiKit.S(10), UiKit.S(6), Width - UiKit.S(16), UiKit.S(18));
        TextRenderer.DrawText(g, CaptionText, captionFont, capRect, palette.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        if (ShowChargingBolt)
        {
            int bw = UiKit.S(11);
            int bh = UiKit.S(14);
            UiKit.DrawBolt(g, new RectangleF(Width - bw - UiKit.S(9), UiKit.S(8), bw, bh), palette.Success);
        }

        Font chosenFont = ValueText.Length >= 6 ? mediumValueFont : valueFont;
        var valRect = new Rectangle(UiKit.S(10), UiKit.S(25), Width - UiKit.S(14), Height - UiKit.S(34));
        TextRenderer.DrawText(g, ValueText, chosenFont, valRect, palette.Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        if (ProgressPercent.HasValue)
        {
            int barX = UiKit.S(10);
            int barW = Width - UiKit.S(20);
            int barY = Height - UiKit.S(7);
            int pct = Math.Max(0, Math.Min(100, ProgressPercent.Value));
            Color barColor = ShowChargingBolt ? palette.Success
                           : pct <= 15 ? palette.Danger
                           : pct <= 25 ? palette.Warning
                           : palette.Accent;
            using (var track = new SolidBrush(Color.FromArgb(42, palette.Muted)))
            using (var active = new SolidBrush(barColor))
            {
                UiKit.FillRounded(g, track, new RectangleF(barX, barY, barW, UiKit.S(3)), 1.5f);
                if (pct > 0)
                    UiKit.FillRounded(g, active, new RectangleF(barX, barY, Math.Max(4f, barW * pct / 100f), UiKit.S(3)), 1.5f);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            captionFont.Dispose();
            valueFont.Dispose();
            mediumValueFont.Dispose();
            compactFont.Dispose();
        }
    }
}

internal sealed class StatusWidget : Form
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WmDeviceChange = 0x0219;
    private const int DbtDevNodesChanged = 0x0007;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private static readonly Guid HidInterfaceGuid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

    private readonly Font captionFont = new Font("Microsoft YaHei UI", 8.8f);
    private readonly Font titleFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);

    private readonly MetricCard batteryCard;
    private readonly MetricCard dpiCard;
    private readonly MetricCard rateCard;
    private readonly Label titleLabel;
    private readonly Label stateLabel;
    private readonly Label updatedLabel;
    private readonly ModernButton refreshButton;
    private readonly ModernButton settingsButton;
    private readonly ModernButton hideButton;

    private readonly ToolTip help = new ToolTip();
    private readonly TaskbarStatus taskbar;
    private readonly OsdToast osdToast = new OsdToast();
    private readonly BatteryAlertTracker alertTracker = new BatteryAlertTracker();
    private readonly ToolStripMenuItem summary;
    private readonly ToolStripMenuItem floatingMode;
    private readonly ToolStripMenuItem trayMode;
    private readonly ToolStripMenuItem compactMenu;
    private readonly ToolStripMenuItem lockMenu;
    private readonly ToolStripMenuItem numberIconMenu;
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private readonly System.Windows.Forms.Timer timer;
    private readonly System.Windows.Forms.Timer usbDebounceTimer;
    private readonly System.Windows.Forms.Timer ghostTimer;
    private readonly HistoryStore history = new HistoryStore(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaicongMouseStatus", "history"));

    private IntPtr deviceNotifyHandle;
    private Icon statusIcon;
    private Point? savedLocation;
    private Point dragOffset;
    private Point dragOrigin;
    private bool dragMoved;
    private bool hoverGhosting;
    private int refreshSeconds = 30;
    private SettingsWindow settingsWindow;
    private BluetoothMouse bluetoothDevice;
    private DisplayPreferences display;
    private ThemePalette palette;
    private Bitmap customPicture;
    private MouseStatus lastStatus;
    private double? lastEstimatedHours;
    private Color statusDotColor = Color.Gray;
    private bool reading;
    private bool exiting;

    internal StatusWidget()
    {
        ReadSettings();
        Text = "鼠标状态";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Font = captionFont;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        EnableDrag(this);

        titleLabel = AddLabel("正在识别鼠标…", titleFont);
        titleLabel.AutoEllipsis = true;
        EnableDrag(titleLabel);

        refreshButton = AddAction(GlyphKind.Refresh, "立即刷新", async delegate { await RefreshStatus(); });
        settingsButton = AddAction(GlyphKind.Settings, "打开设置", OpenSettings);
        hideButton = AddAction(GlyphKind.Close, "隐藏悬浮窗", delegate { ToggleDisplay(true); });

        batteryCard = new MetricCard("电量", true);
        dpiCard = new MetricCard("灵敏度 · DPI", false);
        rateCard = new MetricCard("回报率 · Hz", false);
        foreach (var card in new[] { batteryCard, dpiCard, rateCard })
        {
            Controls.Add(card);
            EnableDrag(card);
        }

        stateLabel = AddLabel("正在读取设备…", captionFont);
        stateLabel.AutoEllipsis = true;
        EnableDrag(stateLabel);

        updatedLabel = AddLabel("", captionFont);
        updatedLabel.TextAlign = ContentAlignment.MiddleRight;
        help.SetToolTip(updatedLabel, "最近一次查询时间");
        EnableDrag(updatedLabel);

        menu = new ContextMenuStrip { Font = captionFont };
        summary = new ToolStripMenuItem("正在读取设备…") { Enabled = false };
        menu.Items.Add(summary);
        menu.Items.Add(new ToolStripSeparator());
        floatingMode = new ToolStripMenuItem("桌面悬浮窗", null, delegate { ToggleDisplay(true); });
        trayMode = new ToolStripMenuItem("任务栏状态条", null, delegate { ToggleDisplay(false); });
        compactMenu = new ToolStripMenuItem("悬浮窗精简模式", null, delegate { ToggleCompact(); });
        lockMenu = new ToolStripMenuItem("锁定悬浮窗（穿透与避让）", null, delegate { ToggleClickThrough(); });
        numberIconMenu = new ToolStripMenuItem("托盘显示数字电量", null, delegate { ToggleTrayNumberIcon(); });
        menu.Items.Add(floatingMode);
        menu.Items.Add(trayMode);
        menu.Items.Add(compactMenu);
        menu.Items.Add(lockMenu);
        menu.Items.Add(numberIconMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("立即刷新", null, async delegate { await RefreshStatus(); });
        menu.Items.Add("设置…", null, delegate { OpenSettings(); });
        menu.Items.Add("退出", null, delegate { exiting = true; Close(); });
        ContextMenuStrip = menu;

        usbDebounceTimer = new System.Windows.Forms.Timer { Interval = 650 };
        usbDebounceTimer.Tick += async delegate
        {
            usbDebounceTimer.Stop();
            await RefreshStatus();
        };

        ghostTimer = new System.Windows.Forms.Timer { Interval = 180 };
        ghostTimer.Tick += delegate { UpdateHoverGhostOpacity(); };

        taskbar = new TaskbarStatus(OpenSettings);
        ApplyAppearance();

        statusIcon = TrayIconRenderer.Create(null, null, palette, display.TrayNumberIcon, display.LowBatteryThreshold);
        tray = new NotifyIcon { Icon = statusIcon, ContextMenuStrip = menu, Text = Text, Visible = true };
        tray.MouseClick += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) OpenSettings();
        };

        timer = new System.Windows.Forms.Timer { Interval = refreshSeconds * 1000 };
        timer.Tick += async delegate { await RefreshStatus(); };

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterUsbNotification();
        if (palette != null)
        {
            UiKit.ApplyWindowChrome(this, palette, true, display.Compact);
            ApplyClickThroughStyle();
        }
    }

    private void RegisterUsbNotification()
    {
        if (deviceNotifyHandle != IntPtr.Zero || !IsHandleCreated) return;
        var filter = new DevBroadcastDeviceInterface
        {
            Size = Marshal.SizeOf(typeof(DevBroadcastDeviceInterface)),
            DeviceType = 5, // DBT_DEVTYP_DEVICEINTERFACE
            ClassGuid = HidInterfaceGuid
        };
        deviceNotifyHandle = RegisterDeviceNotification(Handle, ref filter, 0);
    }

    private void UnregisterUsbNotification()
    {
        if (deviceNotifyHandle == IntPtr.Zero) return;
        try { UnregisterDeviceNotification(deviceNotifyHandle); } catch { }
        deviceNotifyHandle = IntPtr.Zero;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WmDeviceChange && !exiting && !IsDisposed && usbDebounceTimer != null)
        {
            int code = m.WParam.ToInt32();
            if (code == DbtDeviceArrival || code == DbtDeviceRemoveComplete || code == DbtDevNodesChanged)
            {
                usbDebounceTimer.Stop();
                usbDebounceTimer.Start();
            }
        }
    }

    private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume || exiting || IsDisposed) return;
        try
        {
            await Task.Delay(1800);
            if (!exiting && !IsDisposed) await RefreshStatus();
        }
        catch (ObjectDisposedException) { }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (exiting || IsDisposed || !display.FollowSystemTheme) return;
        if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color &&
            e.Category != UserPreferenceCategory.VisualStyle)
            return;
        try
        {
            BeginInvoke(new Action(delegate
            {
                if (exiting || IsDisposed || !display.FollowSystemTheme) return;
                if (palette == null || palette.Index != display.EffectiveTheme())
                    ApplyAppearance();
            }));
        }
        catch (InvalidOperationException) { }
    }

    internal void WakeFromSecondInstance()
    {
        if (exiting || IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(delegate
            {
                if (exiting || IsDisposed) return;
                OpenSettings();
            }));
        }
        catch (InvalidOperationException) { }
    }

    internal void Start()
    {
        CreateHandle();
        BeginInvoke(new Action(async delegate
        {
            ApplyMode();
            timer.Start();
            await RefreshStatus();
        }));
    }

    private ModernButton AddAction(GlyphKind glyph, string name, Action action)
    {
        var button = new ModernButton
        {
            Glyph = glyph,
            AccessibleName = name,
            TabStop = false
        };
        button.Click += delegate { action(); };
        Controls.Add(button);
        help.SetToolTip(button, name);
        return button;
    }

    private Label AddLabel(string text, Font font)
    {
        var label = new Label
        {
            Text = text,
            Font = font,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        Controls.Add(label);
        return label;
    }

    private void ApplyAppearance()
    {
        palette = ThemePalette.Create(display.EffectiveTheme(), display.Accent);
        bool compact = display.Compact;
        bool locked = display.ClickThrough;
        int width = UiKit.S(compact ? 304 : 362);
        int height = UiKit.S(compact ? 104 : 168);

        var oldPicture = customPicture;
        customPicture = DisplayPreferences.OpenPicture(display.Picture);
        var barPicture = display.PictureOnTaskbar ? customPicture : null;

        SuspendLayout();
        ClientSize = new Size(width, height);
        BackColor = palette.Surface;
        ForeColor = palette.Ink;

        int pad = UiKit.S(12);
        int badgeSize = UiKit.S(26);
        int titleLeft = pad + badgeSize + UiKit.S(8);
        int btnSize = UiKit.S(26);
        int btnGap = UiKit.S(4);

        hideButton.SetBounds(width - pad - btnSize, UiKit.S(8), btnSize, btnSize);
        settingsButton.SetBounds(hideButton.Left - btnGap - btnSize, UiKit.S(8), btnSize, btnSize);
        refreshButton.SetBounds(settingsButton.Left - btnGap - btnSize, UiKit.S(8), btnSize, btnSize);

        hideButton.Visible = !locked;
        settingsButton.Visible = !locked;
        refreshButton.Visible = !compact && !locked;

        int rightButtonsLeft = locked ? (width - pad) : (compact ? settingsButton.Left : refreshButton.Left);
        titleLabel.SetBounds(titleLeft, UiKit.S(7), Math.Max(UiKit.S(60), rightButtonsLeft - titleLeft - UiKit.S(6)), UiKit.S(28));
        titleLabel.ForeColor = palette.Ink;

        foreach (var btn in new[] { refreshButton, settingsButton, hideButton })
            btn.ApplyPalette(palette, false, Math.Min(UiKit.S(palette.TileRadius), UiKit.S(8)));

        int cardGap = UiKit.S(8);
        int cardWidth = (width - pad * 2 - cardGap * 2) / 3;
        int cardTop = UiKit.S(compact ? 40 : 44);
        int cardHeight = UiKit.S(compact ? 34 : 84);

        batteryCard.SetBounds(pad, cardTop, cardWidth, cardHeight);
        dpiCard.SetBounds(pad + cardWidth + cardGap, cardTop, cardWidth, cardHeight);
        rateCard.SetBounds(pad + (cardWidth + cardGap) * 2, cardTop, width - pad - (pad + (cardWidth + cardGap) * 2), cardHeight);

        int footerTop = UiKit.S(compact ? 78 : 135);
        int dotSpace = UiKit.S(14);
        stateLabel.SetBounds(pad + dotSpace, footerTop, compact ? (width - pad * 2 - dotSpace) : (width - pad * 2 - dotSpace - UiKit.S(68)), UiKit.S(22));
        stateLabel.ForeColor = palette.Muted;

        updatedLabel.SetBounds(width - pad - UiKit.S(66), footerTop, UiKit.S(66), UiKit.S(22));
        updatedLabel.ForeColor = palette.Muted;
        updatedLabel.Visible = !compact;

        compactMenu.Checked = compact;
        lockMenu.Checked = locked;
        numberIconMenu.Checked = display.TrayNumberIcon;

        ResumeLayout();

        if (IsHandleCreated)
        {
            UiKit.ApplyWindowChrome(this, palette, true, compact);
            ApplyClickThroughStyle();
        }

        RenderStatusValues(lastStatus);
        UpdateTrayIcon();
        taskbar.ApplyAppearance(palette, barPicture);
        if (oldPicture != null) oldPicture.Dispose();
        Invalidate();
    }

    private void ApplyClickThroughStyle()
    {
        if (!IsHandleCreated) return;
        double baseOpacity = DisplayPreferences.NormalizeOpacity(display.OpacityPercent) / 100.0;
        int ex = GetWindowLong(Handle, GwlExStyle);

        if (display.ClickThrough && display.Floating)
        {
            // Keep Opacity slightly under 1.0 so WinForms retains WS_EX_LAYERED on repaints
            Opacity = hoverGhosting ? 0.22 : Math.Min(0.996, baseOpacity);
            SetWindowLong(Handle, GwlExStyle, ex | WsExLayered | WsExTransparent);
            ghostTimer.Start();
        }
        else
        {
            ghostTimer.Stop();
            hoverGhosting = false;
            SetWindowLong(Handle, GwlExStyle, ex & ~WsExTransparent);
            Opacity = baseOpacity;
        }
    }

    private void UpdateHoverGhostOpacity()
    {
        if (!display.ClickThrough || !Visible || IsDisposed)
        {
            ghostTimer.Stop();
            return;
        }
        bool cursorInside = Bounds.Contains(Cursor.Position);
        if (cursorInside != hoverGhosting)
        {
            hoverGhosting = cursorInside;
            double baseOpacity = Math.Min(0.996, DisplayPreferences.NormalizeOpacity(display.OpacityPercent) / 100.0);
            Opacity = hoverGhosting ? 0.22 : baseOpacity;
            int ex = GetWindowLong(Handle, GwlExStyle);
            if ((ex & WsExTransparent) == 0)
                SetWindowLong(Handle, GwlExStyle, ex | WsExLayered | WsExTransparent);
        }
    }

    private void UpdateTrayIcon()
    {
        if (tray == null) return;
        int? level = lastStatus != null ? lastStatus.Battery : null;
        bool? charging = lastStatus != null ? lastStatus.Charging : null;
        Icon next = TrayIconRenderer.Create(level, charging, palette, display.TrayNumberIcon, display.LowBatteryThreshold);
        tray.Icon = next;
        if (statusIcon != null) statusIcon.Dispose();
        statusIcon = next;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(palette.Surface);

        if (!UiKit.IsWindows11)
        {
            using (var border = new Pen(palette.Border, 1f))
                UiKit.DrawRounded(g, border, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), UiKit.S(palette.Radius));
        }

        int pad = UiKit.S(12);
        int badgeSz = UiKit.S(26);
        var badgeRect = new Rectangle(pad, UiKit.S(8), badgeSz, badgeSz);
        Bitmap floatPic = display.PictureOnFloating ? customPicture : null;
        if (floatPic != null)
        {
            using (var clip = UiKit.RoundedPath(badgeRect, Math.Min(UiKit.S(palette.TileRadius), UiKit.S(7))))
            {
                var saved = g.Save();
                g.SetClip(clip);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(floatPic, badgeRect);
                g.Restore(saved);
            }
            using (var border = new Pen(palette.Border, 1f))
                UiKit.DrawRounded(g, border, badgeRect, Math.Min(UiKit.S(palette.TileRadius), UiKit.S(7)));
        }
        else
        {
            UiKit.DrawMouseBadge(g, badgeRect, palette);
        }

        int dotSz = UiKit.S(7);
        int dotX = pad + UiKit.S(2);
        int dotY = stateLabel.Top + (stateLabel.Height - dotSz) / 2;
        using (var brush = new SolidBrush(statusDotColor))
            g.FillEllipse(brush, dotX, dotY, dotSz, dotSz);
    }

    private void EnableDrag(Control control)
    {
        control.Cursor = Cursors.SizeAll;
        control.MouseDown += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || display.ClickThrough) return;
            Point pointer = control.PointToScreen(e.Location);
            dragOrigin = Location;
            dragMoved = false;
            dragOffset = new Point(pointer.X - Left, pointer.Y - Top);
            control.Capture = true;
        };
        control.MouseMove += delegate(object sender, MouseEventArgs e)
        {
            if (!control.Capture || e.Button != MouseButtons.Left || display.ClickThrough) return;
            Point pointer = control.PointToScreen(e.Location);
            Point target = new Point(pointer.X - dragOffset.X, pointer.Y - dragOffset.Y);
            if (!dragMoved && (Math.Abs(target.X - dragOrigin.X) >= 2 || Math.Abs(target.Y - dragOrigin.Y) >= 2))
                dragMoved = true;
            if (dragMoved) Location = target;
        };
        control.MouseCaptureChanged += delegate
        {
            if (control.Capture || !Visible || !dragMoved) return;
            dragMoved = false;
            Rectangle desktop = Screen.FromRectangle(Bounds).WorkingArea;
            Location = ClampToScreen(Location, desktop);
            savedLocation = Location;
            SaveSettings();
        };
        control.MouseUp += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && control.Capture) control.Capture = false;
        };
    }

    private Point ClampToScreen(Point point, Rectangle area)
    {
        return new Point(Math.Max(area.Left, Math.Min(point.X, area.Right - Width)),
                         Math.Max(area.Top, Math.Min(point.Y, area.Bottom - Height)));
    }

    private void PositionWidget()
    {
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        if (savedLocation.HasValue)
        {
            Rectangle previous = new Rectangle(savedLocation.Value, Size);
            if (Array.Exists(Screen.AllScreens, screen => screen.WorkingArea.IntersectsWith(previous)))
            {
                Location = ClampToScreen(savedLocation.Value, Screen.FromRectangle(previous).WorkingArea);
                return;
            }
        }
        Location = new Point(area.Right - Width - UiKit.S(14), area.Bottom - Height - UiKit.S(14));
    }

    private void ApplyMode()
    {
        floatingMode.Checked = display.Floating;
        trayMode.Checked = display.Taskbar;
        compactMenu.Checked = display.Compact;
        lockMenu.Checked = display.ClickThrough;
        numberIconMenu.Checked = display.TrayNumberIcon;
        taskbar.SetActive(display.Taskbar);
        if (!display.Floating)
        {
            ghostTimer.Stop();
            Hide();
        }
        else
        {
            PositionWidget();
            Show();
            UiKit.ApplyWindowChrome(this, palette, true, display.Compact);
            ApplyClickThroughStyle();
        }
    }

    private void ToggleDisplay(bool floating)
    {
        if (Visible) savedLocation = Location;
        if (floating) display.Floating = !display.Floating;
        else display.Taskbar = !display.Taskbar;
        ApplyMode();
        SaveSettings();
    }

    private void ToggleCompact()
    {
        int oldRight = Visible ? Right : -1;
        display.Compact = !display.Compact;
        ApplyAppearance();
        if (Visible && oldRight > 0)
        {
            Rectangle desktop = Screen.FromRectangle(Bounds).WorkingArea;
            savedLocation = ClampToScreen(new Point(oldRight - Width, Top), desktop);
        }
        ApplyMode();
        SaveMainPreferences();
    }

    private void ToggleClickThrough()
    {
        if (Visible) savedLocation = Location;
        display.ClickThrough = !display.ClickThrough;
        ApplyAppearance();
        ApplyMode();
        SaveMainPreferences();
    }

    private void ToggleTrayNumberIcon()
    {
        display.TrayNumberIcon = !display.TrayNumberIcon;
        numberIconMenu.Checked = display.TrayNumberIcon;
        UpdateTrayIcon();
        SaveMainPreferences();
    }

    private void ReadSettings()
    {
        display = DisplayPreferences.Read(null);
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(AppPreferences.SettingsKey))
            {
                if (key == null) return;
                display = DisplayPreferences.Read(key);
                refreshSeconds = AppPreferences.RefreshSeconds(key.GetValue("RefreshSeconds"));
                var id = key.GetValue("BluetoothId") as string;
                if (!string.IsNullOrEmpty(id))
                    bluetoothDevice = new BluetoothMouse(id, key.GetValue("BluetoothName") as string ?? "蓝牙鼠标", false);
                object x = key.GetValue("Left");
                object y = key.GetValue("Top");
                if (x is int && y is int) savedLocation = new Point((int)x, (int)y);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }
    }

    private void SaveSettings()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(AppPreferences.SettingsKey))
            {
                if (key == null) return;
                display.SaveSwitches(key);
                if (!savedLocation.HasValue) return;
                key.SetValue("Left", savedLocation.Value.X);
                key.SetValue("Top", savedLocation.Value.Y);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }
    }

    private void SaveMainPreferences()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(AppPreferences.SettingsKey))
            {
                if (key == null) return;
                display.Save(key);
                if (savedLocation.HasValue)
                {
                    key.SetValue("Left", savedLocation.Value.X);
                    key.SetValue("Top", savedLocation.Value.Y);
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }
    }

    private void OpenSettings()
    {
        if (settingsWindow != null && !settingsWindow.IsDisposed)
        {
            if (settingsWindow.WindowState == FormWindowState.Minimized)
                settingsWindow.WindowState = FormWindowState.Normal;
            settingsWindow.Activate();
            return;
        }
        Icon dialogIcon = statusIcon != null ? (Icon)statusIcon.Clone() : null;
        settingsWindow = new SettingsWindow(
            refreshSeconds, bluetoothDevice, display, history, StatisticsDeviceName(), dialogIcon,
            (seconds, device, nextDisplay) =>
            {
                refreshSeconds = seconds;
                timer.Interval = seconds * 1000;
                bluetoothDevice = device;
                if (Visible) savedLocation = Location;
                display = nextDisplay;
                ApplyAppearance();
                ApplyMode();
                BeginInvoke(new Action(async delegate { await RefreshStatus(); }));
            });
        settingsWindow.FormClosed += delegate { settingsWindow = null; };
        if (Visible && !display.ClickThrough) settingsWindow.Show(this);
        else settingsWindow.Show();
        settingsWindow.Activate();
    }

    private string StatisticsDeviceName()
    {
        return bluetoothDevice == null && titleLabel.Text == "未检测到鼠标"
            ? "上次读取的 2.4G 设备"
            : titleLabel.Text;
    }

    private void RenderStatusValues(MouseStatus current)
    {
        if (current == null)
        {
            batteryCard.ApplyState(palette, display.Compact, "电量", "—", null, false);
            dpiCard.ApplyState(palette, display.Compact, "灵敏度 · DPI", "—", null, false);
            rateCard.ApplyState(palette, display.Compact, "回报率 · Hz", "—", null, false);
            return;
        }

        string batText = current.Battery.HasValue
            ? current.Battery.Value.ToString(CultureInfo.InvariantCulture) + "%"
            : "—";
        string dpiRaw = current.DpiDisplay ?? "—";
        string dpiCompact = current.Dpi.HasValue ? (dpiRaw + " DPI") : "—";
        string dpiCaption = current.DpiStage.HasValue && current.DpiStageCount.HasValue
            ? string.Format(CultureInfo.InvariantCulture, "灵敏度 · {0}/{1}", current.DpiStage.Value, current.DpiStageCount.Value)
            : "灵敏度 · DPI";

        string rateRaw = current.Rate.HasValue ? current.Rate.Value.ToString(CultureInfo.InvariantCulture) : "—";
        string rateWithUnit = current.Rate.HasValue ? (rateRaw + " Hz") : "—";

        batteryCard.ApplyState(palette, display.Compact, "电量", batText, current.Battery, current.Charging == true);
        dpiCard.ApplyState(palette, display.Compact, dpiCaption, display.Compact ? dpiCompact : dpiRaw, null, false);
        rateCard.ApplyState(palette, display.Compact, "回报率 · Hz", display.Compact ? rateWithUnit : rateRaw, null, false);

        string estLabel = HistoryMetrics.FormatRemainingHours(lastEstimatedHours, current.Charging, false);
        string estSuffix = (!string.IsNullOrEmpty(estLabel) && current.Charging != true) ? (" · 续航" + estLabel) : "";

        if (current.IsBluetooth)
        {
            if (current.BluetoothConnected == false) { stateLabel.Text = "蓝牙未连接"; statusDotColor = palette.Muted; }
            else if (current.BluetoothConnected != true) { stateLabel.Text = "蓝牙状态未知"; statusDotColor = palette.Warning; }
            else
            {
                stateLabel.Text = current.Battery.HasValue ? ("蓝牙已连接" + estSuffix) : "蓝牙已连 · 电量不可读";
                statusDotColor = palette.Success;
            }
        }
        else if (!current.ReceiverFound)
        {
            stateLabel.Text = "接收器已断开";
            statusDotColor = palette.Muted;
        }
        else if (!current.Battery.HasValue)
        {
            stateLabel.Text = "鼠标休眠 · 移动可唤醒";
            statusDotColor = palette.Warning;
        }
        else if (current.Charging == true)
        {
            stateLabel.Text = "2.4G · 充电中";
            statusDotColor = palette.Success;
        }
        else if (current.Charging == false)
        {
            stateLabel.Text = "2.4G · 在线" + estSuffix;
            statusDotColor = palette.Success;
        }
        else
        {
            stateLabel.Text = "2.4G · 已连接" + estSuffix;
            statusDotColor = palette.Success;
        }

        if (display.ClickThrough && display.Floating)
            help.SetToolTip(stateLabel, stateLabel.Text);

        string details = "电量 " + batText + " · 灵敏度 " + dpiCompact + " · 回报率 " + rateWithUnit + " · " + stateLabel.Text;
        summary.Text = details;
        if (tray != null) tray.Text = SafeNotifyText(titleLabel.Text + " · " + batText + " · " + dpiCompact);

        string lower = FormatTaskbarSecondary(current, dpiRaw, rateWithUnit);
        taskbar.SetStatus(batText, lower, current.Battery, current.Charging == true,
            titleLabel.Text + Environment.NewLine + details);
        Invalidate();
    }

    private string FormatTaskbarSecondary(MouseStatus current, string dpiRaw, string rateWithUnit)
    {
        if (current.IsBluetooth)
            return current.BluetoothConnected == true ? "蓝牙已连" : current.BluetoothConnected == false ? "蓝牙断开" : "蓝牙未知";
        if (!current.ReceiverFound) return "接收器断开";
        if (!current.Battery.HasValue) return "休眠待唤醒";

        int mode = DisplayPreferences.NormalizeTaskbarContent(display.TaskbarContent);
        if (mode == 1)
            return current.Dpi.HasValue ? (dpiRaw + " DPI") : rateWithUnit;
        if (mode == 2)
            return rateWithUnit;

        if (current.Dpi.HasValue && current.Rate.HasValue)
        {
            string shortRate = current.Rate.Value >= 1000 && current.Rate.Value % 1000 == 0
                ? (current.Rate.Value / 1000).ToString(CultureInfo.InvariantCulture) + "KHz"
                : current.Rate.Value.ToString(CultureInfo.InvariantCulture) + "Hz";
            return dpiRaw + " · " + shortRate;
        }
        if (current.Dpi.HasValue) return dpiRaw + " DPI";
        return rateWithUnit;
    }

    private static string SafeNotifyText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "鼠标状态";
        return text.Length <= 62 ? text : text.Substring(0, 61) + "…";
    }

    private async Task RefreshStatus()
    {
        if (reading || exiting || IsDisposed) return;
        reading = true;
        var source = bluetoothDevice;
        try
        {
            var current = source == null
                ? await Task.Run(() => Receiver.ReadStatus(null))
                : await BluetoothStatus.Read(source.Id);
            if (exiting || IsDisposed) return;
            if (source != bluetoothDevice)
            {
                BeginInvoke(new Action(async delegate { await RefreshStatus(); }));
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            lastStatus = current;
            titleLabel.Text = current.DeviceName ?? (source == null
                ? (current.ReceiverFound ? "2.4G 接收器" : "未检测到鼠标")
                : source.Name);
            help.SetToolTip(titleLabel, titleLabel.Text);

            string sourceKey = source == null ? "usb" : "ble:" + source.Id;
            string identity = source == null ? current.DeviceKey : sourceKey;
            history.Record(sourceKey, identity, current, refreshSeconds, nowUtc);
            lastEstimatedHours = history.EstimateRemainingHours(current.Battery, current.Charging, nowUtc);
            if (settingsWindow != null) settingsWindow.UpdateStatistics(StatisticsDeviceName());

            updatedLabel.Text = DateTime.Now.ToString("HH:mm:ss");
            RenderStatusValues(current);
            UpdateTrayIcon();

            if (!string.IsNullOrEmpty(identity))
            {
                var notice = alertTracker.EvaluateBattery(identity, titleLabel.Text, current,
                    display.LowBatteryThreshold, display.FullBatteryAlert);
                if (notice != null && tray != null)
                    tray.ShowBalloonTip(5000, notice.Title, notice.Body, notice.Level);

                string osdTitle, osdDetail;
                if (alertTracker.EvaluateGearChange(identity, current, out osdTitle, out osdDetail) && display.ShowOsd)
                    osdToast.ShowToast(palette, osdTitle, osdDetail);
            }
        }
        catch (ObjectDisposedException) { }
        finally { reading = false; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (display.Floating) ToggleDisplay(true);
        }
        else exiting = true;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            UnregisterUsbNotification();
            if (settingsWindow != null) settingsWindow.Close();
            osdToast.Dispose();
            taskbar.Dispose();
            help.Dispose();
            usbDebounceTimer.Dispose();
            ghostTimer.Dispose();
            timer.Dispose();
            tray.Dispose();
            if (statusIcon != null) statusIcon.Dispose();
            menu.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            titleFont.Dispose();
            captionFont.Dispose();
            if (customPicture != null) customPicture.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevBroadcastDeviceInterface
    {
        internal int Size;
        internal int DeviceType;
        internal int Reserved;
        internal Guid ClassGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 255)]
        internal string Name;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, ref DevBroadcastDeviceInterface notificationFilter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);
}
