using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

internal sealed class TaskbarStatus : Form
{
    private const uint RaiseWithoutActivation = 0x0213;
    private readonly Label upper;
    private readonly Label lower;
    private readonly PictureBox picture;
    private readonly ToolTip help = new ToolTip();
    private readonly Timer layoutTimer;
    private readonly Font statusFont = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
    private readonly Font detailFont = new Font("Microsoft YaHei UI", 8);
    private bool active;
    private bool locating;

    internal TaskbarStatus(Action openSettings)
    {
        Text = "鼠标任务栏状态";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        upper = MakeReadout("—", statusFont, openSettings);
        lower = MakeReadout("读取中", detailFont, openSettings);
        picture = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Hand };
        picture.Click += delegate { openSettings(); };
        Controls.Add(picture);
        layoutTimer = new Timer { Interval = 2000 };
        layoutTimer.Tick += async delegate { await UpdatePosition(); };
    }

    private Label MakeReadout(string value, Font font, Action openSettings)
    {
        var label = new Label {
            TextAlign = ContentAlignment.MiddleCenter, Font = font, Text = value, Cursor = Cursors.Hand
        };
        label.Click += delegate { openSettings(); };
        Controls.Add(label);
        return label;
    }

    internal void ApplyAppearance(ThemePalette theme, Image image)
    {
        int oldWidth = Width;
        int bodyLeft = image == null ? 3 : 28;
        ClientSize = new Size(image == null ? 78 : 105, 39);
        if (Visible) Left += oldWidth - Width;
        BackColor = theme.AccentTile;
        upper.ForeColor = theme.Ink;
        lower.ForeColor = theme.Muted;
        upper.SetBounds(bodyLeft, 2, Width - bodyLeft - 3, 19);
        lower.SetBounds(bodyLeft, 19, Width - bodyLeft - 3, 17);
        picture.SetBounds(5, 9, 22, 22);
        picture.Image = image;
        picture.Visible = image != null;
        StatusWidget.SetShape(picture, Math.Min(theme.TileRadius, 8));
        StatusWidget.SetShape(this, Math.Min(theme.Radius, 15));
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080;
            return parameters;
        }
    }

    internal async void SetActive(bool value)
    {
        if (active == value) return;
        active = value;
        if (active)
        {
            layoutTimer.Start();
            await UpdatePosition();
        }
        else
        {
            layoutTimer.Stop();
            Hide();
        }
    }

    internal void SetStatus(string first, string second, string tooltip)
    {
        upper.Text = first;
        lower.Text = second;
        help.SetToolTip(upper, tooltip);
        help.SetToolTip(lower, tooltip);
        help.SetToolTip(picture, tooltip);
    }

    private async Task UpdatePosition()
    {
        if (!active || locating || IsDisposed) return;
        locating = true;
        try
        {
            var location = await Task.Run(() => Locate());
            if (!active || IsDisposed) return;
            if (location == null) { Hide(); return; }
            NativePoint anchor = location.Anchor;
            NativePoint limit = location.Limit;
            var hwnd = Handle;
            PhysicalToLogicalPointForPerMonitorDPI(hwnd, ref anchor);
            PhysicalToLogicalPointForPerMonitorDPI(hwnd, ref limit);
            int left = anchor.X - Width - 7;
            if (left < limit.X + 8) { Hide(); return; }
            Location = new Point(left, anchor.Y - Height / 2);
            if (!Visible) Show();
            // Explorer can raise the taskbar when it receives input.
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, RaiseWithoutActivation);
        }
        finally { locating = false; }
    }

    private sealed class LocationData
    {
        internal NativePoint Anchor;
        internal NativePoint Limit;
    }

    private static LocationData Locate()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !IsWindowVisible(taskbar)) return null;
        try
        {
            var nativeTaskbar = AutomationElement.FromHandle(taskbar);
            var bar = nativeTaskbar.Current.BoundingRectangle;
            if (bar.IsEmpty || bar.Height < 20 || bar.Width < bar.Height * 3) return null;
            var foreground = GetForegroundWindow();
            var monitor = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            NativeRect bounds;
            if (foreground != taskbar && GetWindowRect(foreground, out bounds) &&
                bounds.Left <= monitor.Left && bounds.Right >= monitor.Right &&
                bounds.Top <= monitor.Top && bounds.Bottom >= monitor.Bottom) return null;

            var children = nativeTaskbar.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            System.Windows.Rect trayStart = System.Windows.Rect.Empty;
            System.Windows.Rect caret = System.Windows.Rect.Empty;
            double occupied = bar.Left;
            foreach (AutomationElement element in children)
            {
                var item = element.Current;
                var rect = item.BoundingRectangle;
                if (rect.IsEmpty || rect.Width <= 0 || item.IsOffscreen) continue;
                if (item.ClassName.StartsWith("SystemTray.", StringComparison.Ordinal))
                {
                    if (trayStart.IsEmpty || rect.Left < trayStart.Left) trayStart = rect;
                    if (item.Name == "显示隐藏的图标" || item.Name == "Show hidden icons") caret = rect;
                }
                else if (item.ClassName.StartsWith("Taskbar.TaskListButton", StringComparison.Ordinal) ||
                         item.ClassName == "ToggleButton")
                    occupied = Math.Max(occupied, rect.Right);
            }
            var edge = caret.IsEmpty ? trayStart : caret;
            if (edge.IsEmpty) return null;
            return new LocationData {
                Anchor = new NativePoint { X = (int)edge.Left, Y = (int)(edge.Top + edge.Height / 2) },
                Limit = new NativePoint { X = (int)occupied }
            };
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (COMException) { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { internal int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string name, string title);
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
            statusFont.Dispose();
            detailFont.Dispose();
        }
    }
}
