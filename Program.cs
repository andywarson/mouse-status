using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool firstInstance;
        using (var instance = new Mutex(true, @"Local\MaicongMouseStatus", out firstInstance))
        {
            if (!firstInstance) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var context = new ApplicationContext())
            using (var widget = new StatusWidget())
            {
                widget.FormClosed += delegate { context.ExitThread(); };
                widget.Start();
                Application.Run(context);
            }
        }
    }
}

internal sealed class StatusWidget : Form
{
    private readonly Font captionFont = new Font("Microsoft YaHei UI", 9);
    private readonly Font titleFont = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
    private readonly Font numberFont = new Font("Segoe UI", 21, FontStyle.Bold);
    private readonly Font compactFont = new Font("Segoe UI", 14, FontStyle.Bold);
    private readonly Font rateFont = new Font("Segoe UI", 17, FontStyle.Bold);
    private readonly Font compactRateFont = new Font("Segoe UI", 12, FontStyle.Bold);
    private readonly Label battery;
    private readonly Label rate;
    private readonly Label state;
    private readonly Label updated;
    private readonly Label title;
    private readonly PictureBox avatar;
    private readonly Button hideButton;
    private readonly Button settingsButton;
    private readonly ToolTip help = new ToolTip();
    private readonly TaskbarStatus taskbar;
    private readonly ToolStripMenuItem summary;
    private readonly ToolStripMenuItem floatingMode;
    private readonly ToolStripMenuItem trayMode;
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private readonly System.Windows.Forms.Timer timer;
    private readonly HistoryStore history = new HistoryStore(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaicongMouseStatus", "history"));
    private Icon statusIcon;
    private Point? savedLocation;
    private Point dragOffset;
    private int refreshSeconds = 30;
    private SettingsWindow settingsWindow;
    private BluetoothMouse bluetoothDevice;
    private DisplayPreferences display;
    private ThemePalette palette;
    private Bitmap customPicture;
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
        EnableDrag(this);

        title = AddLabel(this, "正在识别鼠标…", 18, 11, 179, 31, titleFont, Color.Empty);
        title.AutoEllipsis = true;
        EnableDrag(title);

        avatar = new PictureBox { Size = new Size(26, 26), SizeMode = PictureBoxSizeMode.Zoom };
        Controls.Add(avatar);
        EnableDrag(avatar);
        hideButton = AddAction("×", "隐藏悬浮窗", delegate { ToggleDisplay(true); });
        settingsButton = AddAction("⚙", "打开设置", OpenSettings);

        battery = AddMetric("电量");
        rate = AddMetric("回报率档位 · Hz");
        state = AddLabel(this, "正在读取设备…", 17, 132, 272, 24, captionFont, Color.Empty);
        EnableDrag(state);
        updated = AddLabel(this, "", 235, 132, 67, 22, captionFont, Color.Empty);
        updated.TextAlign = ContentAlignment.MiddleRight;
        help.SetToolTip(updated, "最近一次查询时间");
        EnableDrag(updated);

        menu = new ContextMenuStrip { Font = captionFont };
        summary = new ToolStripMenuItem("正在读取接收器…") { Enabled = false };
        menu.Items.Add(summary);
        menu.Items.Add(new ToolStripSeparator());
        floatingMode = new ToolStripMenuItem("桌面悬浮窗", null, delegate { ToggleDisplay(true); });
        trayMode = new ToolStripMenuItem("任务栏状态条", null, delegate { ToggleDisplay(false); });
        menu.Items.Add(floatingMode);
        menu.Items.Add(trayMode);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("立即刷新", null, async delegate { await RefreshStatus(); });
        menu.Items.Add("设置…", null, delegate { OpenSettings(); });
        menu.Items.Add("退出", null, delegate { exiting = true; Close(); });
        ContextMenuStrip = menu;
        taskbar = new TaskbarStatus(OpenSettings);
        ApplyAppearance();

        statusIcon = CreateBatteryIcon(null, null, palette.Accent, palette.OnAccent);
        tray = new NotifyIcon { Icon = statusIcon, ContextMenuStrip = menu, Text = Text, Visible = true };
        tray.MouseClick += delegate(object sender, MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) menu.Show(Cursor.Position);
        };

        timer = new System.Windows.Forms.Timer { Interval = refreshSeconds * 1000 };
        timer.Tick += async delegate { await RefreshStatus(); };
    }

    internal void Start()
    {
        CreateHandle();
        BeginInvoke(new Action(async delegate {
            ApplyMode();
            timer.Start();
            await RefreshStatus();
        }));
    }

    private Button AddAction(string symbol, string name, Action action)
    {
        var button = new Button {
            Text = symbol, AccessibleName = name, FlatStyle = FlatStyle.Flat,
            Font = titleFont, UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += delegate { action(); };
        Controls.Add(button);
        help.SetToolTip(button, name);
        return button;
    }

    private Label AddMetric(string name)
    {
        var tile = new Panel();
        Controls.Add(tile);
        var label = AddLabel(tile, name, 11, 6, 116, 21, captionFont, Color.Empty);
        var value = AddLabel(tile, "—", 10, 24, 124, 46, numberFont, Color.Empty);
        value.Tag = label;
        EnableDrag(tile);
        EnableDrag(label);
        EnableDrag(value);
        return value;
    }

    private void ApplyAppearance()
    {
        palette = ThemePalette.Create(display.Theme, display.Accent);
        bool compact = display.Compact;
        int width = compact ? 248 : 316;
        int height = compact ? 105 : 166;
        var oldPicture = customPicture;
        customPicture = DisplayPreferences.OpenPicture(display.Picture);
        var floatPicture = display.PictureOnFloating ? customPicture : null;
        var barPicture = display.PictureOnTaskbar ? customPicture : null;
        bool hasAvatar = floatPicture != null;

        SuspendLayout();
        ClientSize = new Size(width, height);
        BackColor = palette.Surface;
        ForeColor = palette.Ink;
        SetShape(this, compact ? Math.Min(palette.Radius, 17) : palette.Radius);
        avatar.Image = floatPicture;
        avatar.SetBounds(12, 8, 26, 26);
        avatar.Visible = hasAvatar;
        SetShape(avatar, palette.TileRadius);
        int titleLeft = hasAvatar ? 46 : 13;
        title.SetBounds(titleLeft, 6, width - titleLeft - 72, 31);
        title.ForeColor = palette.Ink;
        hideButton.SetBounds(width - 35, 7, 27, 28);
        settingsButton.SetBounds(width - 66, 7, 27, 28);
        foreach (var action in new[] {hideButton, settingsButton})
        {
            SetShape(action, Math.Min(palette.TileRadius, 13));
            action.BackColor = palette.NeutralTile;
            action.ForeColor = palette.Ink;
        }

        int tileWidth = (width - 30) / 2;
        LayoutMetric(battery, 11, tileWidth, palette.AccentTile);
        LayoutMetric(rate, 19 + tileWidth, tileWidth, palette.NeutralTile);
        state.SetBounds(13, compact ? 78 : 132, compact ? width - 25 : width - 95, 22);
        state.ForeColor = palette.Muted;
        updated.SetBounds(width - 79, 132, 66, 22);
        updated.ForeColor = palette.Muted;
        updated.Visible = !compact;
        ResumeLayout();
        taskbar.ApplyAppearance(palette, barPicture);
        if (oldPicture != null) oldPicture.Dispose();
    }

    private void LayoutMetric(Label value, int left, int width, Color color)
    {
        bool compact = display.Compact;
        var tile = value.Parent;
        tile.SetBounds(left, compact ? 38 : 44, width, compact ? 37 : 79);
        tile.BackColor = color;
        SetShape(tile, palette.TileRadius);
        var caption = (Label)value.Tag;
        caption.Visible = !compact;
        caption.ForeColor = palette.Muted;
        value.Font = value == rate ? (compact ? compactRateFont : rateFont) : (compact ? compactFont : numberFont);
        value.ForeColor = palette.Ink;
        value.SetBounds(10, compact ? 1 : 25, width - 17, compact ? 34 : 46);
    }

    private static Label AddLabel(Control parent, string text, int x, int y, int width, int height, Font font, Color color)
    {
        var label = new Label {
            Text = text, Location = new Point(x, y), Size = new Size(width, height),
            Font = font, ForeColor = color, TextAlign = ContentAlignment.MiddleLeft
        };
        parent.Controls.Add(label);
        return label;
    }

    internal static void SetShape(Control control, int radius)
    {
        if (control.Width < radius * 2 || control.Height < radius * 2)
            radius = Math.Min(control.Width, control.Height) / 2;
        if (radius <= 0) return;
        using (var path = new GraphicsPath())
        {
            int diameter = radius * 2;
            path.AddArc(0, 0, diameter, diameter, 180, 90);
            path.AddArc(control.Width - diameter, 0, diameter, diameter, 270, 90);
            path.AddArc(control.Width - diameter, control.Height - diameter, diameter, diameter, 0, 90);
            path.AddArc(0, control.Height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            Region previous = control.Region;
            control.Region = new Region(path);
            if (previous != null) previous.Dispose();
        }
    }

    private void EnableDrag(Control control)
    {
        control.Cursor = Cursors.SizeAll;
        control.MouseDown += delegate(object sender, MouseEventArgs e) {
            if (e.Button != MouseButtons.Left) return;
            Point pointer = control.PointToScreen(e.Location);
            dragOffset = new Point(pointer.X - Left, pointer.Y - Top);
            control.Capture = true;
        };
        control.MouseMove += delegate(object sender, MouseEventArgs e) {
            if (!control.Capture || e.Button != MouseButtons.Left) return;
            Point pointer = control.PointToScreen(e.Location);
            Location = new Point(pointer.X - dragOffset.X, pointer.Y - dragOffset.Y);
        };
        control.MouseCaptureChanged += delegate {
            if (control.Capture || !Visible) return;
            Rectangle desktop = Screen.FromRectangle(Bounds).WorkingArea;
            Location = ClampToScreen(Location, desktop);
            savedLocation = Location;
            SaveSettings();
        };
        control.MouseUp += delegate(object sender, MouseEventArgs e) {
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
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
    }

    private void ApplyMode()
    {
        floatingMode.Checked = display.Floating;
        trayMode.Checked = display.Taskbar;
        taskbar.SetActive(display.Taskbar);
        if (!display.Floating) Hide();
        else { PositionWidget(); Show(); }
    }

    private void ToggleDisplay(bool floating)
    {
        if (Visible) savedLocation = Location;
        if (floating) display.Floating = !display.Floating;
        else display.Taskbar = !display.Taskbar;
        ApplyMode();
        SaveSettings();
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
        catch (System.IO.IOException) { }
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
        catch (System.IO.IOException) { }
    }

    private void OpenSettings()
    {
        if (settingsWindow != null)
        {
            settingsWindow.Activate();
            return;
        }
        settingsWindow = new SettingsWindow(refreshSeconds, bluetoothDevice, display, history, StatisticsDeviceName(),
            (seconds, device, nextDisplay) => {
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
        if (Visible) settingsWindow.Show(this);
        else settingsWindow.Show();
        settingsWindow.Activate();
    }

    private string StatisticsDeviceName()
    {
        return bluetoothDevice == null && title.Text == "未检测到鼠标" ? "上次读取的 2.4G 设备" : title.Text;
    }

    private async Task RefreshStatus()
    {
        if (reading || exiting) return;
        reading = true;
        var source = bluetoothDevice;
        try
        {
            var current = source == null ? await Task.Run(() => Receiver.ReadStatus(null))
                                         : await BluetoothStatus.Read(source.Id);
            if (exiting || IsDisposed) return;
            if (source != bluetoothDevice)
            {
                BeginInvoke(new Action(async delegate { await RefreshStatus(); }));
                return;
            }
            title.Text = current.DeviceName ?? (source == null
                ? (current.ReceiverFound ? "2.4G 接收器" : "未检测到鼠标")
                : source.Name);
            string sourceKey = source == null ? "usb" : "ble:" + source.Id;
            history.Record(sourceKey, source == null ? current.DeviceKey : sourceKey, current, refreshSeconds, DateTime.UtcNow);
            if (settingsWindow != null) settingsWindow.UpdateStatistics(StatisticsDeviceName());
            help.SetToolTip(title, title.Text);
            battery.Text = current.Battery.HasValue ? current.Battery.Value.ToString(CultureInfo.InvariantCulture) + "%" : "—";
            string rateValue = current.Rate.HasValue ? current.Rate.Value.ToString(CultureInfo.InvariantCulture) : "—";
            string rateWithUnit = current.Rate.HasValue ? rateValue + " Hz" : rateValue;
            rate.Text = display.Compact ? rateWithUnit : rateValue;
            updated.Text = DateTime.Now.ToString("HH:mm:ss");
            if (current.IsBluetooth)
            {
                if (current.BluetoothConnected == false) state.Text = "蓝牙未连接";
                else if (current.BluetoothConnected != true) state.Text = "蓝牙状态未知";
                else state.Text = current.Battery.HasValue ? "蓝牙已连接" : "蓝牙已连接 · 电量不可读";
            }
            else if (!current.ReceiverFound) state.Text = "接收器已断开";
            else if (!current.Battery.HasValue) state.Text = "鼠标未响应 · 移动后可重试";
            else if (current.Charging == true) state.Text = "2.4G · 充电中";
            else if (current.Charging == false) state.Text = "2.4G · 未充电";
            else state.Text = "2.4G · 充电状态未知";
            string details = "电量 " + battery.Text + " · 档位 " + rateWithUnit + " · " + state.Text;
            summary.Text = details;
            tray.Text = details;
            string upper = battery.Text + (current.Charging == true ? " 充电" : "");
            string lower = rateWithUnit;
            if (current.IsBluetooth)
                lower = current.BluetoothConnected == true ? "蓝牙已连" : current.BluetoothConnected == false ? "蓝牙断开" : "蓝牙未知";
            else if (!current.ReceiverFound) lower = "接收器断开";
            else if (!current.Battery.HasValue) lower = "鼠标未响应";
            taskbar.SetStatus(upper, lower, title.Text + Environment.NewLine + details);
            Icon next = CreateBatteryIcon(current.Battery, current.Charging, palette.Accent, palette.OnAccent);
            tray.Icon = next;
            statusIcon.Dispose();
            statusIcon = next;
        }
        finally { reading = false; }
    }

    private static Icon CreateBatteryIcon(int? level, bool? charging, Color accent, Color foreground)
    {
        using (var image = new Bitmap(32, 32))
        using (var paint = Graphics.FromImage(image))
        using (var background = new SolidBrush(accent))
        using (var white = new SolidBrush(foreground))
        using (var outline = new Pen(foreground, 1.7f))
        {
            paint.SmoothingMode = SmoothingMode.AntiAlias;
            paint.FillEllipse(background, 0, 0, 31, 31);
            paint.DrawRectangle(outline, 5, 10, 19, 11);
            paint.FillRectangle(white, 25, 13, 2, 5);
            if (charging == true)
                paint.FillPolygon(white, new[] { new Point(16, 7), new Point(11, 16), new Point(15, 16),
                    new Point(13, 25), new Point(20, 13), new Point(16, 13) });
            else if (level.HasValue)
                paint.FillRectangle(white, 8, 13, 13 * level.Value / 100f, 5);
            else
                paint.DrawLine(outline, 11, 16, 18, 16);
            IntPtr native = image.GetHicon();
            try { using (var icon = Icon.FromHandle(native)) return (Icon)icon.Clone(); }
            finally { DestroyIcon(native); }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

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
            if (settingsWindow != null) settingsWindow.Close();
            taskbar.Dispose();
            help.Dispose();
            timer.Dispose();
            tray.Dispose();
            statusIcon.Dispose();
            menu.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            numberFont.Dispose();
            compactFont.Dispose();
            rateFont.Dispose();
            compactRateFont.Dispose();
            titleFont.Dispose();
            captionFont.Dispose();
            if (customPicture != null) customPicture.Dispose();
        }
    }
}
