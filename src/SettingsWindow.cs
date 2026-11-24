using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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
    private static readonly int[] AlertThresholds = { 0, 15, 20, 30 };

    private readonly Font bodyFont = new Font("Microsoft YaHei UI", 9.5f);
    private readonly Font smallFont = new Font("Microsoft YaHei UI", 8.5f);
    private readonly Font sectionFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
    private readonly Font titleFont = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);

    private readonly DisplayPreferences draft;
    private readonly List<ModernButton> secondaryButtons = new List<ModernButton>();
    private readonly List<SectionCard> cards = new List<SectionCard>();
    private readonly List<Label> primaryLabels = new List<Label>();
    private readonly List<Label> mutedLabels = new List<Label>();
    private readonly List<ModernComboBox> combos = new List<ModernComboBox>();
    private readonly List<CheckBox> checks = new List<CheckBox>();

    private readonly ModernButton tabGeneralBtn;
    private readonly ModernButton tabAppearanceBtn;
    private readonly ModernButton tabStatsBtn;
    private readonly Panel contentHost;
    private readonly Panel generalPage;
    private readonly Panel appearancePage;
    private readonly Panel statisticsPage;
    private readonly StatisticsView statistics;

    private readonly ModernComboBox styleCombo;
    private readonly CheckBox followSystemCheck;
    private readonly ThemeGallery gallery;
    private readonly AccentSwatchBar swatchBar;
    private readonly MiniWidgetPreview livePreview;
    private readonly PictureBox avatarPreview;
    private readonly ModernButton accentButton;
    private readonly ModernButton cancelButton;
    private readonly ModernButton saveButton;
    private readonly NumericUpDown intervalInput;
    private readonly NumericUpDown opacityInput;
    private readonly Icon ownedIcon;

    private int activeTabIndex;
    private bool syncingThemeControls;
    private Bitmap previewAvatarBitmap;
    private string statisticsDevice;

    internal SettingsWindow(int seconds, BluetoothMouse selected, DisplayPreferences display,
                            HistoryStore history, string deviceName, Icon appIcon,
                            Action<int, BluetoothMouse, DisplayPreferences> apply)
    {
        draft = display.Copy();
        statisticsDevice = deviceName;
        ownedIcon = appIcon;
        Text = "鼠标状态设置";
        if (ownedIcon != null) Icon = ownedIcon;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = bodyFont;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        ClientSize = new Size(UiKit.S(512), UiKit.S(532));

        var heading = new Label { Text = "鼠标状态设置", Font = titleFont, AutoSize = true };
        primaryLabels.Add(heading);
        Place(this, heading, UiKit.S(20), UiKit.S(14));

        var subtitle = new Label { Text = "配置悬浮窗穿透、任务栏状态条、智能电量提醒、主题与统计", Font = smallFont, AutoSize = true };
        mutedLabels.Add(subtitle);
        Place(this, subtitle, UiKit.S(22), UiKit.S(44));

        tabGeneralBtn = MakeNavButton("常规", 0);
        tabAppearanceBtn = MakeNavButton("外观", 1);
        tabStatsBtn = MakeNavButton("统计", 2);
        Place(this, tabGeneralBtn, UiKit.S(20), UiKit.S(70));
        Place(this, tabAppearanceBtn, UiKit.S(112), UiKit.S(70));
        Place(this, tabStatsBtn, UiKit.S(204), UiKit.S(70));

        contentHost = new Panel { Location = new Point(UiKit.S(20), UiKit.S(108)), Size = new Size(UiKit.S(472), UiKit.S(366)) };
        Controls.Add(contentHost);

        generalPage = new Panel { Dock = DockStyle.Fill, Visible = true };
        appearancePage = new Panel { Dock = DockStyle.Fill, Visible = false };
        statisticsPage = new Panel { Dock = DockStyle.Fill, Visible = false };
        statistics = new StatisticsView(history);
        statisticsPage.Controls.Add(statistics);
        contentHost.Controls.Add(generalPage);
        contentHost.Controls.Add(appearancePage);
        contentHost.Controls.Add(statisticsPage);

        // --- TAB 1: GENERAL ---
        var displayCard = MakeCard(generalPage, "显示方式与窗口交互", 0, 0, UiKit.S(472), UiKit.S(148));
        var floating = MakeCheck("桌面悬浮窗", draft.Floating);
        var taskbar = MakeCheck("任务栏状态条", draft.Taskbar);
        var clickThrough = MakeCheck("锁定悬浮窗（穿透/避让）", draft.ClickThrough);
        Place(displayCard, floating, UiKit.S(16), UiKit.S(35));
        Place(displayCard, taskbar, UiKit.S(154), UiKit.S(35));
        Place(displayCard, clickThrough, UiKit.S(294), UiKit.S(35));

        Place(displayCard, MakePrimaryLabel("悬浮窗布局"), UiKit.S(16), UiKit.S(71));
        var variant = NewChoice("悬浮窗内容", UiKit.S(226));
        variant.Items.AddRange(new object[] { "完整版", "精简版" });
        variant.SelectedIndex = draft.Compact ? 1 : 0;
        Place(displayCard, variant, UiKit.S(228), UiKit.S(67));

        Place(displayCard, MakePrimaryLabel("任务栏次行内容"), UiKit.S(16), UiKit.S(108));
        var barContent = NewChoice("任务栏次行内容", UiKit.S(226));
        barContent.Items.AddRange(new object[] { "DPI + 回报率", "仅灵敏度 (DPI)", "仅回报率 (Hz)" });
        barContent.SelectedIndex = DisplayPreferences.NormalizeTaskbarContent(draft.TaskbarContent);
        Place(displayCard, barContent, UiKit.S(228), UiKit.S(104));

        var deviceCard = MakeCard(generalPage, "设备来源、智能提醒与系统", 0, UiKit.S(158), UiKit.S(472), UiKit.S(204));
        Place(deviceCard, MakePrimaryLabel("数据来源"), UiKit.S(16), UiKit.S(35));
        var source = NewChoice("数据来源", UiKit.S(226));
        source.Items.Add("自动识别 2.4G 接收器");
        if (selected != null) source.Items.Add(selected);
        source.SelectedIndex = selected == null ? 0 : 1;
        Place(deviceCard, source, UiKit.S(228), UiKit.S(31));

        Shown += async delegate
        {
            using (var timeout = new CancellationTokenSource(5000))
            {
                var devices = await BluetoothStatus.PairedDevices(timeout.Token);
                if (IsDisposed) return;
                foreach (var device in devices)
                {
                    if (selected == null || device.Id != selected.Id)
                        source.Items.Add(device);
                }
            }
        };

        Place(deviceCard, MakePrimaryLabel("刷新间隔（5–300 秒）"), UiKit.S(16), UiKit.S(71));
        intervalInput = new NumericUpDown
        {
            Minimum = AppPreferences.MinimumSeconds,
            Maximum = AppPreferences.MaximumSeconds,
            Value = AppPreferences.RefreshSeconds(seconds),
            Width = UiKit.S(92),
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "数据刷新间隔（秒）"
        };
        Place(deviceCard, intervalInput, UiKit.S(228), UiKit.S(68));

        var usbHint = new Label { Text = "插拔接收器自动即刻刷新", Font = smallFont, AutoSize = true };
        mutedLabels.Add(usbHint);
        Place(deviceCard, usbHint, UiKit.S(328), UiKit.S(72));

        Place(deviceCard, MakePrimaryLabel("低电量通知阈值"), UiKit.S(16), UiKit.S(107));
        var lowAlertCombo = NewChoice("低电量通知阈值", UiKit.S(226));
        lowAlertCombo.Items.AddRange(new object[] { "关闭低电量通知", "低于 15% 时提醒", "低于 20% 时提醒", "低于 30% 时提醒" });
        int currentThresh = DisplayPreferences.NormalizeLowBatteryThreshold(draft.LowBatteryThreshold);
        int threshIdx = Array.IndexOf(AlertThresholds, currentThresh);
        lowAlertCombo.SelectedIndex = threshIdx >= 0 ? threshIdx : 2;
        Place(deviceCard, lowAlertCombo, UiKit.S(228), UiKit.S(103));

        var fullAlertCheck = MakeCheck("充满 100% 提醒拔线", draft.FullBatteryAlert);
        var osdCheck = MakeCheck("档位切换屏幕提示 (OSD)", draft.ShowOsd);
        fullAlertCheck.Font = smallFont;
        osdCheck.Font = smallFont;
        Place(deviceCard, fullAlertCheck, UiKit.S(16), UiKit.S(143));
        Place(deviceCard, osdCheck, UiKit.S(228), UiKit.S(143));

        var startup = MakeCheck("登录 Windows 后自动启动", false);
        startup.Font = smallFont;
        try { startup.Checked = AppPreferences.StartsWithWindows(); }
        catch (UnauthorizedAccessException) { startup.Enabled = false; }
        catch (System.Security.SecurityException) { startup.Enabled = false; }
        catch (IOException) { startup.Enabled = false; }
        Place(deviceCard, startup, UiKit.S(16), UiKit.S(172));

        var trayHint = new Label { Text = "全部隐藏后可单击托盘打开", Font = smallFont, AutoSize = true };
        mutedLabels.Add(trayHint);
        Place(deviceCard, trayHint, UiKit.S(228), UiKit.S(174));

        // --- TAB 2: APPEARANCE ---
        var themeCard = MakeCard(appearancePage, "内置主题与配色", 0, 0, UiKit.S(472), UiKit.S(198));
        followSystemCheck = MakeCheck("跟随系统深浅色自动切换", draft.FollowSystemTheme);
        followSystemCheck.Font = smallFont;
        Place(themeCard, followSystemCheck, UiKit.S(14), UiKit.S(34));

        styleCombo = NewChoice("预设风格", UiKit.S(204));
        styleCombo.Items.AddRange(ThemePalette.Names);
        styleCombo.SelectedIndex = draft.EffectiveTheme();
        Place(themeCard, styleCombo, UiKit.S(254), UiKit.S(30));

        gallery = new ThemeGallery
        {
            Location = new Point(UiKit.S(14), UiKit.S(64)),
            Size = new Size(UiKit.S(444), UiKit.S(88)),
            SelectedTheme = draft.EffectiveTheme()
        };
        themeCard.Controls.Add(gallery);

        Place(themeCard, MakePrimaryLabel("强调色"), UiKit.S(14), UiKit.S(163));
        swatchBar = new AccentSwatchBar
        {
            Location = new Point(UiKit.S(68), UiKit.S(160)),
            Size = new Size(UiKit.S(188), UiKit.S(26))
        };
        themeCard.Controls.Add(swatchBar);

        accentButton = MakeActionButton("自选色…", UiKit.S(88), true);
        Place(themeCard, accentButton, UiKit.S(266), UiKit.S(158));

        var resetAccent = MakeActionButton("默认色", UiKit.S(96), false);
        Place(themeCard, resetAccent, UiKit.S(362), UiKit.S(158));

        var mediaCard = MakeCard(appearancePage, "托盘图标样式、头像徽章与实时预览", 0, UiKit.S(208), UiKit.S(472), UiKit.S(154));
        var trayLbl = new Label { Text = "托盘样式", Font = smallFont, AutoSize = true };
        primaryLabels.Add(trayLbl);
        Place(mediaCard, trayLbl, UiKit.S(14), UiKit.S(36));

        var trayStyleCombo = NewChoice("托盘图标样式", UiKit.S(158));
        trayStyleCombo.Items.AddRange(new object[] { "电池图形图标", "纯数字百分比" });
        trayStyleCombo.SelectedIndex = draft.TrayNumberIcon ? 1 : 0;
        Place(mediaCard, trayStyleCombo, UiKit.S(82), UiKit.S(32));

        avatarPreview = new PictureBox
        {
            Location = new Point(UiKit.S(14), UiKit.S(68)),
            Size = new Size(UiKit.S(40), UiKit.S(40)),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        mediaCard.Controls.Add(avatarPreview);

        var pickPic = MakeActionButton("选择图片…", UiKit.S(96), false);
        var clearPic = MakeActionButton("移除", UiKit.S(68), false);
        Place(mediaCard, pickPic, UiKit.S(62), UiKit.S(66));
        Place(mediaCard, clearPic, UiKit.S(164), UiKit.S(66));

        var onFloating = MakeCheck("悬浮窗显示", draft.PictureOnFloating);
        var onTaskbar = MakeCheck("任务栏显示", draft.PictureOnTaskbar);
        onFloating.Font = smallFont;
        onTaskbar.Font = smallFont;
        Place(mediaCard, onFloating, UiKit.S(62), UiKit.S(100));
        Place(mediaCard, onTaskbar, UiKit.S(158), UiKit.S(100));

        var opLabel = new Label { Text = "不透明度%", Font = smallFont, AutoSize = true };
        mutedLabels.Add(opLabel);
        Place(mediaCard, opLabel, UiKit.S(14), UiKit.S(125));
        opacityInput = new NumericUpDown
        {
            Minimum = 70,
            Maximum = 100,
            Increment = 5,
            Value = DisplayPreferences.NormalizeOpacity(draft.OpacityPercent),
            Width = UiKit.S(60),
            Font = smallFont,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "悬浮窗不透明度百分比"
        };
        Place(mediaCard, opacityInput, UiKit.S(88), UiKit.S(122));

        livePreview = new MiniWidgetPreview
        {
            Location = new Point(UiKit.S(254), UiKit.S(34)),
            Size = new Size(UiKit.S(204), UiKit.S(108))
        };
        mediaCard.Controls.Add(livePreview);

        // Wire Appearance events
        gallery.ThemeSelected += idx =>
        {
            if (syncingThemeControls) return;
            syncingThemeControls = true;
            draft.RememberThemePolarity(idx);
            if (draft.FollowSystemTheme && ThemePalette.IsDarkTheme(idx) != DisplayPreferences.IsWindowsAppDarkMode())
                followSystemCheck.Checked = draft.FollowSystemTheme = false;
            styleCombo.SelectedIndex = draft.EffectiveTheme();
            gallery.SelectedTheme = draft.EffectiveTheme();
            syncingThemeControls = false;
            PreviewTheme();
        };
        styleCombo.SelectedIndexChanged += delegate
        {
            if (syncingThemeControls) return;
            syncingThemeControls = true;
            int idx = styleCombo.SelectedIndex;
            draft.RememberThemePolarity(idx);
            if (draft.FollowSystemTheme && ThemePalette.IsDarkTheme(idx) != DisplayPreferences.IsWindowsAppDarkMode())
                followSystemCheck.Checked = draft.FollowSystemTheme = false;
            gallery.SelectedTheme = draft.EffectiveTheme();
            syncingThemeControls = false;
            PreviewTheme();
        };
        followSystemCheck.CheckedChanged += delegate
        {
            if (syncingThemeControls) return;
            draft.FollowSystemTheme = followSystemCheck.Checked;
            syncingThemeControls = true;
            int eff = draft.EffectiveTheme();
            styleCombo.SelectedIndex = eff;
            gallery.SelectedTheme = eff;
            syncingThemeControls = false;
            PreviewTheme();
        };
        trayStyleCombo.SelectedIndexChanged += delegate
        {
            draft.TrayNumberIcon = trayStyleCombo.SelectedIndex == 1;
            UpdateLivePreview();
        };
        swatchBar.ColorPicked += color =>
        {
            draft.Accent = color.ToArgb();
            PreviewTheme();
        };
        accentButton.Click += delegate
        {
            using (var picker = new ColorDialog { Color = CurrentDraftPalette().Accent, FullOpen = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                draft.Accent = picker.Color.ToArgb();
                PreviewTheme();
            }
        };
        resetAccent.Click += delegate
        {
            draft.Accent = null;
            PreviewTheme();
        };
        pickPic.Click += delegate { ChoosePicture(); };
        clearPic.Click += delegate { draft.Picture = null; PreviewPicture(); };
        onFloating.CheckedChanged += delegate { draft.PictureOnFloating = onFloating.Checked; UpdateLivePreview(); };
        opacityInput.ValueChanged += delegate { draft.OpacityPercent = Decimal.ToInt32(opacityInput.Value); };

        // Footer Save / Cancel
        cancelButton = MakeActionButton("取消", UiKit.S(88), false);
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Click += delegate { Close(); };
        Place(this, cancelButton, UiKit.S(306), UiKit.S(486));
        CancelButton = cancelButton;

        saveButton = MakeActionButton("保存", UiKit.S(96), true);
        saveButton.Click += delegate
        {
            int refresh = Decimal.ToInt32(intervalInput.Value);
            var device = source.SelectedItem as BluetoothMouse;
            draft.Floating = floating.Checked;
            draft.Taskbar = taskbar.Checked;
            draft.ClickThrough = clickThrough.Checked;
            draft.Compact = variant.SelectedIndex == 1;
            draft.TaskbarContent = barContent.SelectedIndex;
            draft.FollowSystemTheme = followSystemCheck.Checked;
            draft.TrayNumberIcon = trayStyleCombo.SelectedIndex == 1;
            int selThreshIdx = lowAlertCombo.SelectedIndex;
            draft.LowBatteryThreshold = selThreshIdx >= 0 && selThreshIdx < AlertThresholds.Length ? AlertThresholds[selThreshIdx] : 20;
            draft.FullBatteryAlert = fullAlertCheck.Checked;
            draft.ShowOsd = osdCheck.Checked;
            draft.OpacityPercent = Decimal.ToInt32(opacityInput.Value);
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
        Place(this, saveButton, UiKit.S(402), UiKit.S(486));
        AcceptButton = saveButton;

        PreviewPicture();
        PreviewTheme();
        SwitchTab(0);
    }

    private ThemePalette CurrentDraftPalette()
    {
        return ThemePalette.Create(draft.EffectiveTheme(), draft.Accent);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiKit.ApplyWindowChrome(this, CurrentDraftPalette(), false, false);
    }

    private ModernButton MakeNavButton(string title, int tabIndex)
    {
        var btn = new ModernButton
        {
            Text = title,
            Size = new Size(UiKit.S(84), UiKit.S(29)),
            Font = sectionFont,
            AccessibleName = title
        };
        btn.Click += delegate { SwitchTab(tabIndex); };
        return btn;
    }

    private void SwitchTab(int index)
    {
        activeTabIndex = index;
        generalPage.Visible = index == 0;
        appearancePage.Visible = index == 1;
        statisticsPage.Visible = index == 2;

        bool expanded = index == 2;
        if (expanded) statistics.RefreshData(statisticsDevice);

        int w = UiKit.S(expanded ? 780 : 512);
        int h = UiKit.S(532);
        if (ClientSize.Width != w || ClientSize.Height != h)
        {
            ClientSize = new Size(w, h);
            contentHost.Size = new Size(w - UiKit.S(40), h - UiKit.S(166));
            cancelButton.Location = new Point(w - UiKit.S(206), h - UiKit.S(46));
            saveButton.Location = new Point(w - UiKit.S(110), h - UiKit.S(46));
            if (Visible)
            {
                Rectangle desktop = Screen.FromRectangle(Bounds).WorkingArea;
                Location = new Point(
                    desktop.Left + Math.Max(0, (desktop.Width - Width) / 2),
                    desktop.Top + Math.Max(0, (desktop.Height - Height) / 2));
            }
        }
        UpdateNavButtonStyles(CurrentDraftPalette());
    }

    internal void UpdateStatistics(string deviceName)
    {
        statisticsDevice = deviceName;
        if (activeTabIndex == 2 && !IsDisposed)
            statistics.RefreshData(deviceName);
    }

    private SectionCard MakeCard(Control parent, string title, int x, int y, int width, int height)
    {
        var card = new SectionCard(title, sectionFont)
        {
            Location = new Point(x, y),
            Size = new Size(width, height)
        };
        cards.Add(card);
        parent.Controls.Add(card);
        return card;
    }

    private Label MakePrimaryLabel(string text)
    {
        var label = new Label { Text = text, AutoSize = true };
        primaryLabels.Add(label);
        return label;
    }

    private CheckBox MakeCheck(string text, bool isChecked)
    {
        var box = new CheckBox { Text = text, Checked = isChecked, AutoSize = true };
        checks.Add(box);
        return box;
    }

    private ModernComboBox NewChoice(string name, int width)
    {
        var box = new ModernComboBox { Width = width, AccessibleName = name };
        combos.Add(box);
        return box;
    }

    private ModernButton MakeActionButton(string text, int width, bool primary)
    {
        var button = new ModernButton
        {
            Text = text,
            Size = new Size(width, UiKit.S(30)),
            Font = bodyFont,
            Primary = primary
        };
        if (!primary) secondaryButtons.Add(button);
        return button;
    }

    private static void Place(Control parent, Control control, int x, int y)
    {
        control.Location = new Point(x, y);
        parent.Controls.Add(control);
    }

    private void UpdateNavButtonStyles(ThemePalette palette)
    {
        tabGeneralBtn.ApplyPalette(palette, activeTabIndex == 0, UiKit.S(8));
        tabAppearanceBtn.ApplyPalette(palette, activeTabIndex == 1, UiKit.S(8));
        tabStatsBtn.ApplyPalette(palette, activeTabIndex == 2, UiKit.S(8));
    }

    private void PreviewTheme()
    {
        var palette = CurrentDraftPalette();
        BackColor = palette.Surface;
        ForeColor = palette.Ink;
        contentHost.BackColor = palette.Surface;
        generalPage.BackColor = appearancePage.BackColor = statisticsPage.BackColor = palette.Surface;

        foreach (var card in cards) card.ApplyTheme(palette);
        foreach (var lbl in primaryLabels) lbl.ForeColor = palette.Ink;
        foreach (var lbl in mutedLabels) lbl.ForeColor = palette.Muted;
        foreach (var chk in checks) chk.ForeColor = palette.Ink;
        foreach (var cmb in combos)
            cmb.ApplyPalette(palette);
        intervalInput.BackColor = opacityInput.BackColor = palette.Surface;
        intervalInput.ForeColor = opacityInput.ForeColor = palette.Ink;

        foreach (var btn in secondaryButtons)
            btn.ApplyPalette(palette, false, UiKit.S(8));
        accentButton.ApplyPalette(palette, true, UiKit.S(8));
        saveButton.ApplyPalette(palette, true, UiKit.S(9));
        UpdateNavButtonStyles(palette);

        avatarPreview.BackColor = palette.Surface;
        gallery.ApplyHostPalette(palette);
        swatchBar.ApplyHostPalette(palette, draft.Accent);
        statistics.ApplyTheme(palette);
        UpdateLivePreview();

        if (IsHandleCreated)
            UiKit.ApplyWindowChrome(this, palette, false, false);
        Invalidate();
    }

    private void UpdateLivePreview()
    {
        livePreview.UpdateState(CurrentDraftPalette(), draft.PictureOnFloating ? previewAvatarBitmap : null, draft.TrayNumberIcon);
    }

    private void ChoosePicture()
    {
        using (var picker = new OpenFileDialog
        {
            Title = "选择状态图片",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true
        })
        {
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            try { draft.Picture = DisplayPreferences.ImportPicture(picker.FileName); }
            catch (ArgumentException) { ShowPictureError(); return; }
            catch (OutOfMemoryException) { ShowPictureError(); return; }
            catch (IOException) { ShowPictureError(); return; }
            catch (UnauthorizedAccessException) { ShowPictureError(); return; }
            catch (ExternalException) { ShowPictureError(); return; }
            PreviewPicture();
        }
    }

    private void PreviewPicture()
    {
        var old = previewAvatarBitmap;
        previewAvatarBitmap = DisplayPreferences.OpenPicture(draft.Picture);
        avatarPreview.Image = previewAvatarBitmap;
        UiKit.SetRegionRounded(avatarPreview, UiKit.S(8));
        UpdateLivePreview();
        if (old != null) old.Dispose();
    }

    private void ShowPictureError()
    {
        MessageBox.Show(this, "无法读取这张图片。请选择可打开的 PNG、JPEG 或 BMP 文件。", Text,
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ShowSaveError()
    {
        MessageBox.Show(this, "Windows 无法保存设置，请检查当前账户的注册表写入权限。", Text,
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (previewAvatarBitmap != null)
            {
                avatarPreview.Image = null;
                previewAvatarBitmap.Dispose();
                previewAvatarBitmap = null;
            }
            if (ownedIcon != null) ownedIcon.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            bodyFont.Dispose();
            smallFont.Dispose();
            sectionFont.Dispose();
            titleFont.Dispose();
        }
    }
}

internal sealed class SectionCard : Panel
{
    private readonly string headingText;
    private readonly Font headingFont;
    private ThemePalette theme = ThemePalette.Create(0, null);

    internal SectionCard(string heading, Font font)
    {
        headingText = heading;
        headingFont = font;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    internal void ApplyTheme(ThemePalette palette)
    {
        theme = palette;
        BackColor = palette.NeutralTile;
        ForeColor = palette.Ink;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color parentBg = Parent != null ? Parent.BackColor : theme.Surface;
        g.Clear(parentBg);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = UiKit.S(theme.TileRadius);
        using (var fill = new SolidBrush(theme.NeutralTile))
        using (var border = new Pen(theme.Border, 1f))
        {
            UiKit.FillRounded(g, fill, rect, radius);
            UiKit.DrawRounded(g, border, rect, radius);
        }

        if (!string.IsNullOrEmpty(headingText))
        {
            TextRenderer.DrawText(g, headingText, headingFont,
                new Rectangle(UiKit.S(14), UiKit.S(8), Width - UiKit.S(28), UiKit.S(22)),
                theme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

internal sealed class ThemeGallery : Control
{
    private readonly Font labelFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold);
    private ThemePalette hostPalette = ThemePalette.Create(0, null);
    private int hoveredIndex = -1;
    internal int SelectedTheme;
    internal event Action<int> ThemeSelected;

    internal ThemeGallery()
    {
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    internal void ApplyHostPalette(ThemePalette palette)
    {
        hostPalette = palette;
        BackColor = palette.NeutralTile;
        Invalidate();
    }

    private Rectangle CardRect(int index)
    {
        int cols = 4;
        int gapX = UiKit.S(8);
        int gapY = UiKit.S(8);
        int cw = (Width - gapX * (cols - 1)) / cols;
        int ch = (Height - gapY) / 2;
        int col = index % cols;
        int row = index / cols;
        return new Rectangle(col * (cw + gapX), row * (ch + gapY), cw, ch);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = -1;
        for (int i = 0; i < ThemePalette.Names.Length; i++)
        {
            if (CardRect(i).Contains(e.Location)) { hit = i; break; }
        }
        if (hit != hoveredIndex) { hoveredIndex = hit; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoveredIndex != -1) { hoveredIndex = -1; Invalidate(); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        for (int i = 0; i < ThemePalette.Names.Length; i++)
        {
            if (CardRect(i).Contains(e.Location))
            {
                SelectedTheme = i;
                Invalidate();
                if (ThemeSelected != null) ThemeSelected(i);
                break;
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(hostPalette.NeutralTile);

        for (int i = 0; i < ThemePalette.Names.Length; i++)
        {
            var r = CardRect(i);
            var p = ThemePalette.Create(i, null);
            var rectF = new RectangleF(r.X + 1f, r.Y + 1f, r.Width - 2f, r.Height - 2f);
            bool selected = i == SelectedTheme;
            bool hover = i == hoveredIndex;

            using (var bg = new SolidBrush(p.Surface))
            using (var tile = new SolidBrush(p.AccentTile))
            using (var dot = new SolidBrush(p.Accent))
            using (var border = new Pen(selected ? hostPalette.Accent : (hover ? p.Ink : p.Border), selected ? 2f : 1f))
            {
                UiKit.FillRounded(g, bg, rectF, UiKit.S(7));
                var strip = new RectangleF(r.X + UiKit.S(6), r.Bottom - UiKit.S(11), r.Width - UiKit.S(12), UiKit.S(6));
                UiKit.FillRounded(g, tile, strip, UiKit.S(3));
                int dotSz = UiKit.S(10);
                g.FillEllipse(dot, r.X + UiKit.S(8), r.Y + UiKit.S(8), dotSz, dotSz);
                UiKit.DrawRounded(g, border, rectF, UiKit.S(7));

                TextRenderer.DrawText(g, ThemePalette.ShortNames[i], labelFont,
                    new Rectangle(r.X + UiKit.S(22), r.Y + UiKit.S(4), r.Width - UiKit.S(26), UiKit.S(18)),
                    p.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) labelFont.Dispose();
    }
}

internal sealed class AccentSwatchBar : Control
{
    private ThemePalette hostPalette = ThemePalette.Create(0, null);
    private int? selectedArgb;
    internal event Action<Color> ColorPicked;

    internal AccentSwatchBar()
    {
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    internal void ApplyHostPalette(ThemePalette palette, int? customAccent)
    {
        hostPalette = palette;
        selectedArgb = customAccent;
        BackColor = palette.NeutralTile;
        Invalidate();
    }

    private Rectangle DotRect(int index)
    {
        int sz = UiKit.S(18);
        int step = UiKit.S(23);
        int y = (Height - sz) / 2;
        return new Rectangle(index * step + 2, y, sz, sz);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        for (int i = 0; i < ThemePalette.PresetAccents.Length; i++)
        {
            if (DotRect(i).Contains(e.Location))
            {
                if (ColorPicked != null) ColorPicked(ThemePalette.PresetAccents[i]);
                break;
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(hostPalette.NeutralTile);

        for (int i = 0; i < ThemePalette.PresetAccents.Length; i++)
        {
            Color c = ThemePalette.PresetAccents[i];
            var r = DotRect(i);
            using (var fill = new SolidBrush(c))
                g.FillEllipse(fill, r);
            bool active = selectedArgb.HasValue && (selectedArgb.Value | unchecked((int)0xFF000000)) == c.ToArgb();
            if (active)
            {
                using (var ring = new Pen(hostPalette.Ink, 2f))
                    g.DrawEllipse(ring, r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4);
            }
        }
    }
}

internal sealed class MiniWidgetPreview : Control
{
    private readonly Font titleFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold);
    private readonly Font valFont = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
    private readonly Font subFont = new Font("Microsoft YaHei UI", 7.2f);
    private ThemePalette palette = ThemePalette.Create(0, null);
    private Image avatar;
    private bool numberTray;

    internal MiniWidgetPreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    internal void UpdateState(ThemePalette nextPalette, Image nextAvatar, bool trayNumberMode)
    {
        palette = nextPalette;
        avatar = nextAvatar;
        numberTray = trayNumberMode;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color parentBg = Parent != null ? Parent.BackColor : palette.NeutralTile;
        g.Clear(parentBg);

        var outer = new RectangleF(1f, 1f, Width - 2.5f, Height - 2.5f);
        float r = Math.Min(UiKit.S(palette.Radius) * 0.7f, UiKit.S(11));
        using (var bg = new SolidBrush(palette.Surface))
        using (var border = new Pen(palette.Border, 1f))
        {
            UiKit.FillRounded(g, bg, outer, r);
            UiKit.DrawRounded(g, border, outer, r);
        }

        int tx = UiKit.S(10);
        if (avatar != null)
        {
            int sz = UiKit.S(16);
            var imgRect = new Rectangle(tx, UiKit.S(7), sz, sz);
            using (var clip = UiKit.RoundedPath(imgRect, UiKit.S(4)))
            {
                var state = g.Save();
                g.SetClip(clip);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(avatar, imgRect);
                g.Restore(state);
            }
            tx += sz + UiKit.S(5);
        }
        TextRenderer.DrawText(g, "实时预览", titleFont,
            new Rectangle(tx, UiKit.S(5), Width - tx - UiKit.S(34), UiKit.S(20)),
            palette.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // Mini preview of selected tray icon right in top-right corner
        using (var ico = TrayIconRenderer.Create(86, false, palette, numberTray, 20))
        using (var bmp = ico.ToBitmap())
        {
            int icoSz = UiKit.S(18);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(bmp, new Rectangle(Width - icoSz - UiKit.S(9), UiKit.S(6), icoSz, icoSz));
        }

        int gap = UiKit.S(5);
        int pad = UiKit.S(8);
        int tw = (Width - pad * 2 - gap * 2) / 3;
        int ty = UiKit.S(30);
        int th = Height - ty - UiKit.S(24);
        string[] labels = { "86%", "1600", "2KHz" };
        string[] subs = { "电量", "DPI", "回报率" };

        for (int i = 0; i < 3; i++)
        {
            var tileRect = new RectangleF(pad + i * (tw + gap), ty, tw, th);
            using (var fill = new SolidBrush(i == 0 ? palette.AccentTile : palette.NeutralTile))
            using (var pen = new Pen(i == 0 ? palette.AccentBorder : palette.Border, 1f))
            {
                UiKit.FillRounded(g, fill, tileRect, Math.Min(UiKit.S(palette.TileRadius) * 0.65f, UiKit.S(6)));
                UiKit.DrawRounded(g, pen, tileRect, Math.Min(UiKit.S(palette.TileRadius) * 0.65f, UiKit.S(6)));
            }
            var box = Rectangle.Round(tileRect);
            TextRenderer.DrawText(g, subs[i], subFont, new Rectangle(box.X + UiKit.S(5), box.Y + UiKit.S(3), box.Width - UiKit.S(7), UiKit.S(14)),
                palette.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, labels[i], valFont, new Rectangle(box.X + UiKit.S(5), box.Y + UiKit.S(17), box.Width - UiKit.S(7), box.Height - UiKit.S(18)),
                palette.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        TextRenderer.DrawText(g, "● 2.4G · 预计约 45 小时", subFont,
            new Rectangle(pad, Height - UiKit.S(20), Width - pad * 2, UiKit.S(16)),
            palette.Success, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            titleFont.Dispose();
            valFont.Dispose();
            subFont.Dispose();
        }
    }
}
