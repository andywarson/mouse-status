using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class AppPreferences
{
    internal const string SettingsKey = @"Software\MaicongMouseStatus";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupName = "MaicongMouseStatus";
    internal const int MinimumSeconds = 5;
    internal const int MaximumSeconds = 300;

    internal static int RefreshSeconds(object value)
    {
        if (!(value is int)) return 30;
        int seconds = (int)value;
        return seconds >= MinimumSeconds && seconds <= MaximumSeconds ? seconds : 30;
    }

    internal static bool StartsWithWindows()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
            return run != null && run.GetValue(StartupName) is string;
    }

    internal static void Save(int seconds, bool startup, BluetoothMouse device, DisplayPreferences display)
    {
        using (var settings = Registry.CurrentUser.CreateSubKey(SettingsKey))
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (settings == null || run == null) throw new InvalidOperationException("Windows 设置无法写入。");
            if (startup) run.SetValue(StartupName, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
            else run.DeleteValue(StartupName, false);

            settings.SetValue("RefreshSeconds", RefreshSeconds(seconds), RegistryValueKind.DWord);
            if (device != null)
            {
                settings.SetValue("BluetoothId", device.Id);
                settings.SetValue("BluetoothName", device.Name);
            }
            else
            {
                settings.DeleteValue("BluetoothId", false);
                settings.DeleteValue("BluetoothName", false);
            }
            display.Save(settings);
        }
    }
}

internal sealed class SettingsWindow : Form
{
    private readonly Font bodyFont = new Font("Microsoft YaHei UI", 10);
    private readonly Font titleFont = new Font("Microsoft YaHei UI", 17, FontStyle.Bold);
    private readonly DisplayPreferences draft;
    private readonly PictureBox avatarPreview;
    private readonly Panel themePreview;
    private readonly Label previewText;
    private readonly Button accentButton;
    private readonly Button save;
    private readonly TabPage generalPage;
    private readonly TabPage appearancePage;
    private readonly TabPage statisticsPage;
    private readonly StatisticsView statistics;
    private string statisticsDevice;

    internal SettingsWindow(int seconds, BluetoothMouse selected, DisplayPreferences display,
                            HistoryStore history, string deviceName,
                            Action<int, BluetoothMouse, DisplayPreferences> apply)
    {
        draft = display.Copy();
        statisticsDevice = deviceName;
        Text = "鼠标状态设置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = bodyFont;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(452, 365);

        var heading = new Label { Text = "鼠标状态设置", Font = titleFont, AutoSize = true };
        Place(this, heading, 18, 13);
        var pages = new TabControl { Size = new Size(416, 259) };
        Place(this, pages, 18, 55);
        var general = new TabPage("常规");
        var appearance = new TabPage("外观");
        statisticsPage = new TabPage("统计");
        statistics = new StatisticsView(history);
        statisticsPage.Controls.Add(statistics);
        generalPage = general;
        appearancePage = appearance;
        pages.TabPages.AddRange(new[] { general, appearance, statisticsPage });

        var floating = new CheckBox { Text = "桌面悬浮窗", Checked = draft.Floating, AutoSize = true };
        var taskbar = new CheckBox { Text = "任务栏状态条", Checked = draft.Taskbar, AutoSize = true };
        Place(general, floating, 14, 13);
        Place(general, taskbar, 186, 13);

        Place(general, new Label { Text = "悬浮窗内容", AutoSize = true }, 14, 51);
        var variant = NewChoice("悬浮窗内容", 160);
        variant.Items.AddRange(new object[] { "完整版", "精简版" });
        variant.SelectedIndex = draft.Compact ? 1 : 0;
        Place(general, variant, 226, 47);

        Place(general, new Label { Text = "数据来源", AutoSize = true }, 14, 88);
        var source = NewChoice("数据来源", 160);
        source.Items.Add("自动识别 2.4G 接收器");
        if (selected != null) source.Items.Add(selected);
        source.SelectedIndex = selected == null ? 0 : 1;
        Place(general, source, 226, 84);
        Shown += async delegate {
            using (var timeout = new CancellationTokenSource(5000))
            {
                try
                {
                    var devices = await BluetoothStatus.PairedDevices(timeout.Token);
                    if (IsDisposed) return;
                    foreach (var device in devices)
                        if (selected == null || device.Id != selected.Id) source.Items.Add(device);
                }
                catch (OperationCanceledException) { }
                catch (System.Runtime.InteropServices.COMException) { }
                catch (UnauthorizedAccessException) { }
            }
        };

        Place(general, new Label { Text = "刷新间隔（5–300 秒）", AutoSize = true }, 14, 125);
        var interval = new NumericUpDown {
            Minimum = AppPreferences.MinimumSeconds, Maximum = AppPreferences.MaximumSeconds,
            Value = seconds, Width = 96, AccessibleName = "数据刷新间隔（秒）"
        };
        Place(general, interval, 226, 121);
        var startup = new CheckBox { Text = "登录 Windows 后自动启动", AutoSize = true };
        try { startup.Checked = AppPreferences.StartsWithWindows(); }
        catch (UnauthorizedAccessException) { startup.Enabled = false; }
        catch (System.Security.SecurityException) { startup.Enabled = false; }
        catch (IOException) { startup.Enabled = false; }
        Place(general, startup, 14, 168);
        Place(general, new Label { Text = "关闭两处显示后，仍可从托盘图标打开设置。", AutoSize = true }, 14, 199);

        Place(appearance, new Label { Text = "预设风格", AutoSize = true }, 14, 15);
        var style = NewChoice("预设风格", 200);
        style.Items.AddRange(ThemePalette.Names);
        style.SelectedIndex = draft.Theme;
        Place(appearance, style, 183, 11);

        themePreview = new Panel { Size = new Size(367, 40) };
        previewText = new Label { Text = "主题预览", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        themePreview.Controls.Add(previewText);
        Place(appearance, themePreview, 14, 46);

        accentButton = MakeButton("选择强调色", 110);
        accentButton.Click += delegate {
            using (var picker = new ColorDialog { Color = ThemePalette.Create(draft.Theme, draft.Accent).Accent, FullOpen = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                draft.Accent = picker.Color.ToArgb();
                PreviewTheme();
            }
        };
        Place(appearance, accentButton, 14, 97);
        var reset = MakeButton("使用预设颜色", 121);
        reset.Click += delegate { draft.Accent = null; PreviewTheme(); };
        Place(appearance, reset, 132, 97);

        avatarPreview = new PictureBox { Size = new Size(42, 42), SizeMode = PictureBoxSizeMode.Zoom };
        Place(appearance, avatarPreview, 14, 143);
        var pick = MakeButton("选择图片…", 106);
        pick.Click += delegate { ChoosePicture(); };
        Place(appearance, pick, 66, 148);
        var clear = MakeButton("移除图片", 99);
        clear.Click += delegate { draft.Picture = null; PreviewPicture(); };
        Place(appearance, clear, 180, 148);
        var onFloating = new CheckBox { Text = "悬浮窗显示图片", AutoSize = true, Checked = draft.PictureOnFloating };
        var onTaskbar = new CheckBox { Text = "任务栏显示图片", AutoSize = true, Checked = draft.PictureOnTaskbar };
        Place(appearance, onFloating, 14, 196);
        Place(appearance, onTaskbar, 186, 196);

        var cancel = MakeButton("取消", 80);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Click += delegate { Close(); };
        Place(this, cancel, 264, 322);
        CancelButton = cancel;
        save = MakeButton("保存", 80);
        save.Click += delegate {
            int refresh = Decimal.ToInt32(interval.Value);
            var device = source.SelectedItem as BluetoothMouse;
            draft.Floating = floating.Checked;
            draft.Taskbar = taskbar.Checked;
            draft.Compact = variant.SelectedIndex == 1;
            draft.PictureOnFloating = onFloating.Checked;
            draft.PictureOnTaskbar = onTaskbar.Checked;
            try { AppPreferences.Save(refresh, startup.Checked, device, draft); }
            catch (UnauthorizedAccessException) { ShowSaveError(); return; }
            catch (System.Security.SecurityException) { ShowSaveError(); return; }
            catch (IOException) { ShowSaveError(); return; }
            catch (InvalidOperationException) { ShowSaveError(); return; }
            apply(refresh, device, draft);
            Close();
        };
        Place(this, save, 352, 322);
        AcceptButton = save;

        pages.SelectedIndexChanged += delegate {
            bool expanded = pages.SelectedTab == statisticsPage;
            if (expanded) statistics.RefreshData(statisticsDevice);
            ClientSize = expanded ? new Size(750, 455) : new Size(452, 365);
            pages.Size = expanded ? new Size(714, 349) : new Size(416, 259);
            cancel.Location = new Point(ClientSize.Width - 188, ClientSize.Height - 43);
            save.Location = new Point(ClientSize.Width - 100, ClientSize.Height - 43);
            Rectangle desktop = Screen.FromRectangle(Bounds).WorkingArea;
            if (expanded)
                Location = new Point(desktop.Left + Math.Max(0, (desktop.Width - Width) / 2),
                    desktop.Top + Math.Max(0, (desktop.Height - Height) / 2));
        };

        style.SelectedIndexChanged += delegate { draft.Theme = style.SelectedIndex; PreviewTheme(); };
        PreviewTheme();
        PreviewPicture();
    }

    internal void UpdateStatistics(string deviceName)
    {
        statisticsDevice = deviceName;
        if (statisticsPage.Visible) statistics.RefreshData(deviceName);
    }

    private static ComboBox NewChoice(string name, int width)
    {
        return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, AccessibleName = name };
    }

    private static void Place(Control parent, Control control, int x, int y)
    {
        control.Location = new Point(x, y);
        parent.Controls.Add(control);
    }

    private static Button MakeButton(string text, int width)
    {
        var button = new Button {
            Text = text, Size = new Size(width, 31), FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(232, 232, 243), ForeColor = Color.FromArgb(35, 33, 49),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.Resize += delegate { StatusWidget.SetShape(button, 11); };
        StatusWidget.SetShape(button, 11);
        return button;
    }

    private void PreviewTheme()
    {
        var palette = ThemePalette.Create(draft.Theme, draft.Accent);
        BackColor = palette.Surface;
        ForeColor = palette.Ink;
        generalPage.BackColor = appearancePage.BackColor = palette.Surface;
        generalPage.ForeColor = appearancePage.ForeColor = palette.Ink;
        statisticsPage.BackColor = palette.Surface;
        statistics.ApplyTheme(palette);
        themePreview.BackColor = palette.AccentTile;
        previewText.ForeColor = palette.Ink;
        StatusWidget.SetShape(themePreview, palette.TileRadius);
        accentButton.BackColor = save.BackColor = palette.Accent;
        accentButton.ForeColor = save.ForeColor = palette.OnAccent;
    }

    private void ChoosePicture()
    {
        using (var picker = new OpenFileDialog {
            Title = "选择状态图片", Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true
        })
        {
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            try { draft.Picture = DisplayPreferences.ImportPicture(picker.FileName); }
            catch (ArgumentException) { ShowPictureError(); return; }
            catch (OutOfMemoryException) { ShowPictureError(); return; }
            catch (IOException) { ShowPictureError(); return; }
            catch (UnauthorizedAccessException) { ShowPictureError(); return; }
            catch (System.Runtime.InteropServices.ExternalException) { ShowPictureError(); return; }
            PreviewPicture();
        }
    }

    private void ShowPictureError()
    {
        MessageBox.Show(this, "无法读取这张图片。请选择可打开的 PNG、JPEG 或 BMP 文件。", Text,
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void PreviewPicture()
    {
        var old = avatarPreview.Image;
        avatarPreview.Image = DisplayPreferences.OpenPicture(draft.Picture);
        avatarPreview.BackColor = Color.FromArgb(232, 232, 243);
        StatusWidget.SetShape(avatarPreview, 10);
        if (old != null) old.Dispose();
    }

    private void ShowSaveError()
    {
        MessageBox.Show(this, "Windows 无法保存设置，请检查当前账户的权限。", Text,
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && avatarPreview.Image != null) avatarPreview.Image.Dispose();
        base.Dispose(disposing);
        if (disposing) { bodyFont.Dispose(); titleFont.Dispose(); }
    }
}
