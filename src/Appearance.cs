using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Win32;

internal sealed class DisplayPreferences
{
    internal bool Floating;
    internal bool Taskbar;
    internal bool Compact;
    internal bool ClickThrough;
    internal int TaskbarContent; // 0 = DPI + Hz, 1 = DPI only, 2 = Hz only
    internal int Theme;
    internal bool FollowSystemTheme;
    internal int LightTheme = 1; // Default light: Fluent 2
    internal int DarkTheme = 5;  // Default dark: Deep Ocean Aurora
    internal int? Accent;
    internal int OpacityPercent = 100;
    internal bool TrayNumberIcon;
    internal int LowBatteryThreshold = 20; // 0 = disabled, else 10..50
    internal bool FullBatteryAlert = true;
    internal bool ShowOsd = true;
    internal byte[] Picture;
    internal bool PictureOnFloating = true;
    internal bool PictureOnTaskbar;

    internal DisplayPreferences Copy() { return (DisplayPreferences)MemberwiseClone(); }

    internal static DisplayPreferences FromSwitches(object floating, object taskbar, object legacyMode)
    {
        bool legacyTray = Equals(legacyMode, 1);
        return new DisplayPreferences
        {
            Floating = floating is int ? Equals(floating, 1) : !legacyTray,
            Taskbar = taskbar is int ? Equals(taskbar, 1) : legacyTray
        };
    }

    internal static int NormalizeOpacity(object value)
    {
        if (!(value is int)) return 100;
        int pct = (int)value;
        return pct >= 70 && pct <= 100 ? pct : 100;
    }

    internal static int NormalizeTaskbarContent(object value)
    {
        if (!(value is int)) return 0;
        int mode = (int)value;
        return mode >= 0 && mode <= 2 ? mode : 0;
    }

    internal static int NormalizeLowBatteryThreshold(object value)
    {
        if (!(value is int)) return 20;
        int pct = (int)value;
        if (pct == 0) return 0;
        return pct >= 10 && pct <= 50 ? pct : 20;
    }

    internal void RememberThemePolarity(int themeIndex)
    {
        int idx = ThemePalette.Normalize(themeIndex);
        Theme = idx;
        if (ThemePalette.IsDarkTheme(idx)) DarkTheme = idx;
        else LightTheme = idx;
    }

    internal int EffectiveTheme(bool? systemDarkOverride = null)
    {
        int explicitIdx = ThemePalette.Normalize(Theme);
        if (!FollowSystemTheme) return explicitIdx;

        bool sysDark = systemDarkOverride ?? IsWindowsAppDarkMode();
        if (ThemePalette.IsDarkTheme(explicitIdx) == sysDark) return explicitIdx;

        int fallback = ThemePalette.Normalize(sysDark ? DarkTheme : LightTheme);
        if (ThemePalette.IsDarkTheme(fallback) == sysDark) return fallback;
        return sysDark ? 5 : 1;
    }

    internal static bool IsWindowsAppDarkMode()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                if (key == null) return false;
                object val = key.GetValue("AppsUseLightTheme");
                return val is int && (int)val == 0;
            }
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Security.SecurityException) { return false; }
        catch (IOException) { return false; }
    }

    internal static DisplayPreferences Read(RegistryKey key)
    {
        if (key == null) return FromSwitches(null, null, null);
        var display = FromSwitches(key.GetValue("ShowFloating"), key.GetValue("ShowTaskbar"), key.GetValue("TrayOnly"));
        display.Compact = Equals(key.GetValue("CompactFloating"), 1);
        display.ClickThrough = Equals(key.GetValue("ClickThrough"), 1);
        display.TaskbarContent = NormalizeTaskbarContent(key.GetValue("TaskbarContent"));
        display.Theme = ThemePalette.Normalize(key.GetValue("Theme") as int? ?? 0);
        display.FollowSystemTheme = Equals(key.GetValue("FollowSystemTheme"), 1);
        display.LightTheme = ThemePalette.Normalize(key.GetValue("LightTheme") as int? ?? (ThemePalette.IsDarkTheme(display.Theme) ? 1 : display.Theme));
        display.DarkTheme = ThemePalette.Normalize(key.GetValue("DarkTheme") as int? ?? (ThemePalette.IsDarkTheme(display.Theme) ? display.Theme : 5));
        display.Accent = key.GetValue("Accent") as int?;
        display.OpacityPercent = NormalizeOpacity(key.GetValue("OpacityPercent"));
        display.TrayNumberIcon = Equals(key.GetValue("TrayNumberIcon"), 1);
        display.LowBatteryThreshold = NormalizeLowBatteryThreshold(key.GetValue("LowBatteryThreshold"));
        display.FullBatteryAlert = !Equals(key.GetValue("FullBatteryAlert"), 0);
        display.ShowOsd = !Equals(key.GetValue("ShowOsd"), 0);
        display.Picture = key.GetValue("Picture") as byte[];
        display.PictureOnFloating = !Equals(key.GetValue("PictureOnFloating"), 0);
        display.PictureOnTaskbar = Equals(key.GetValue("PictureOnTaskbar"), 1);
        return display;
    }

    internal void SaveSwitches(RegistryKey key)
    {
        key.SetValue("ShowFloating", Floating ? 1 : 0);
        key.SetValue("ShowTaskbar", Taskbar ? 1 : 0);
        key.SetValue("ClickThrough", ClickThrough ? 1 : 0);
    }

    internal void Save(RegistryKey key)
    {
        SaveSwitches(key);
        key.SetValue("CompactFloating", Compact ? 1 : 0);
        key.SetValue("TaskbarContent", NormalizeTaskbarContent(TaskbarContent));
        key.SetValue("Theme", ThemePalette.Normalize(Theme));
        key.SetValue("FollowSystemTheme", FollowSystemTheme ? 1 : 0);
        key.SetValue("LightTheme", ThemePalette.Normalize(LightTheme));
        key.SetValue("DarkTheme", ThemePalette.Normalize(DarkTheme));
        key.SetValue("OpacityPercent", NormalizeOpacity(OpacityPercent));
        key.SetValue("TrayNumberIcon", TrayNumberIcon ? 1 : 0);
        key.SetValue("LowBatteryThreshold", NormalizeLowBatteryThreshold(LowBatteryThreshold));
        key.SetValue("FullBatteryAlert", FullBatteryAlert ? 1 : 0);
        key.SetValue("ShowOsd", ShowOsd ? 1 : 0);
        if (Accent.HasValue) key.SetValue("Accent", Accent.Value);
        else key.DeleteValue("Accent", false);
        if (Picture != null) key.SetValue("Picture", Picture, RegistryValueKind.Binary);
        else key.DeleteValue("Picture", false);
        key.SetValue("PictureOnFloating", PictureOnFloating ? 1 : 0);
        key.SetValue("PictureOnTaskbar", PictureOnTaskbar ? 1 : 0);
    }

    internal static byte[] ImportPicture(string path)
    {
        using (var source = Image.FromFile(path))
        using (var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
        using (var canvas = Graphics.FromImage(bitmap))
        using (var data = new MemoryStream())
        {
            int edge = Math.Min(source.Width, source.Height);
            var crop = new Rectangle((source.Width - edge) / 2, (source.Height - edge) / 2, edge, edge);
            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            canvas.DrawImage(source, new Rectangle(0, 0, 64, 64), crop, GraphicsUnit.Pixel);
            bitmap.Save(data, ImageFormat.Png);
            return data.ToArray();
        }
    }

    internal static Bitmap OpenPicture(byte[] data)
    {
        if (data == null || data.Length > 65536) return null;
        try
        {
            using (var buffer = new MemoryStream(data))
            using (var image = Image.FromStream(buffer))
                return new Bitmap(image);
        }
        catch (ArgumentException) { return null; }
        catch (OutOfMemoryException) { return null; }
        catch (System.Runtime.InteropServices.ExternalException) { return null; }
    }
}

internal sealed class ThemePalette
{
    internal static readonly string[] Names = {
        "薰衣草 · Material 3",
        "云母浅色 · Fluent 2",
        "石墨极简 · 深色",
        "晨雾青瓷 · 浅色",
        "暖砂拿铁 · 浅色",
        "深海极光 · 深色",
        "赛博霓虹 · 电竞深色",
        "黑金碳纤 · 旗舰深色"
    };

    internal static readonly string[] ShortNames = {
        "薰衣草", "云母白", "石墨黑", "青瓷绿",
        "暖砂色", "深海蓝", "赛博紫", "黑金碳"
    };

    internal static readonly Color[] PresetAccents = {
        Color.FromArgb(99, 102, 241),   // Indigo
        Color.FromArgb(0, 103, 192),    // Fluent Blue
        Color.FromArgb(13, 148, 136),   // Teal
        Color.FromArgb(16, 185, 129),   // Emerald
        Color.FromArgb(245, 158, 11),   // Amber Gold
        Color.FromArgb(239, 68, 68),    // Coral Red
        Color.FromArgb(168, 85, 247),   // Cyber Violet
        Color.FromArgb(236, 72, 153)    // Rose Pink
    };

    internal readonly int Index;
    internal readonly bool IsDark;
    internal readonly Color Surface;
    internal readonly Color NeutralTile;
    internal readonly Color AccentTile;
    internal readonly Color Border;
    internal readonly Color AccentBorder;
    internal readonly Color Hover;
    internal readonly Color Ink;
    internal readonly Color Muted;
    internal readonly Color Accent;
    internal readonly Color OnAccent;
    internal readonly Color Success;
    internal readonly Color Warning;
    internal readonly Color Danger;
    internal readonly int Radius;
    internal readonly int TileRadius;

    private ThemePalette(
        int index, bool isDark,
        Color surface, Color neutral, Color border,
        Color ink, Color muted, Color accent,
        int radius, int tileRadius, int tint)
    {
        Index = index;
        IsDark = isDark;
        Surface = surface;
        NeutralTile = neutral;
        Border = border;
        Ink = ink;
        Muted = muted;
        Accent = accent;
        OnAccent = Luminance(accent) > 0.179 ? Color.FromArgb(20, 22, 28) : Color.White;
        AccentTile = Blend(surface, accent, tint);
        AccentBorder = Blend(border, accent, isDark ? 45 : 38);
        Hover = isDark ? Blend(neutral, Color.White, 10) : Blend(neutral, Color.Black, 6);
        Success = isDark ? Color.FromArgb(52, 211, 153) : Color.FromArgb(5, 150, 105);
        Warning = isDark ? Color.FromArgb(251, 191, 36) : Color.FromArgb(217, 119, 6);
        Danger = isDark ? Color.FromArgb(248, 113, 113) : Color.FromArgb(220, 38, 38);
        Radius = radius;
        TileRadius = tileRadius;
    }

    internal static int Normalize(int index) { return index >= 0 && index < Names.Length ? index : 0; }

    internal static bool IsDarkTheme(int index)
    {
        int idx = Normalize(index);
        return idx == 2 || idx == 5 || idx == 6 || idx == 7;
    }

    internal static ThemePalette Create(int index, int? customAccent)
    {
        int idx = Normalize(index);
        Color? custom = customAccent.HasValue
            ? (Color?)Color.FromArgb(customAccent.Value | unchecked((int)0xFF000000))
            : null;

        switch (idx)
        {
            case 1: // 云母浅色 · Fluent 2
                return new ThemePalette(1, false,
                    Color.FromArgb(246, 249, 253), Color.FromArgb(234, 240, 248), Color.FromArgb(214, 224, 237),
                    Color.FromArgb(20, 28, 38), Color.FromArgb(88, 102, 120),
                    custom ?? Color.FromArgb(0, 103, 192), 12, 9, 13);

            case 2: // 石墨极简 · 深色
                return new ThemePalette(2, true,
                    Color.FromArgb(24, 26, 31), Color.FromArgb(36, 39, 46), Color.FromArgb(54, 59, 69),
                    Color.FromArgb(243, 245, 248), Color.FromArgb(156, 165, 179),
                    custom ?? Color.FromArgb(110, 231, 183), 12, 8, 16);

            case 3: // 晨雾青瓷 · 浅色
                return new ThemePalette(3, false,
                    Color.FromArgb(243, 248, 246), Color.FromArgb(228, 239, 235), Color.FromArgb(203, 222, 215),
                    Color.FromArgb(20, 38, 34), Color.FromArgb(78, 105, 98),
                    custom ?? Color.FromArgb(13, 148, 136), 14, 10, 14);

            case 4: // 暖砂拿铁 · 浅色
                return new ThemePalette(4, false,
                    Color.FromArgb(251, 247, 240), Color.FromArgb(241, 233, 221), Color.FromArgb(224, 211, 193),
                    Color.FromArgb(43, 33, 24), Color.FromArgb(115, 95, 76),
                    custom ?? Color.FromArgb(202, 103, 2), 14, 10, 14);

            case 5: // 深海极光 · 深色
                return new ThemePalette(5, true,
                    Color.FromArgb(15, 23, 42), Color.FromArgb(30, 41, 59), Color.FromArgb(51, 65, 85),
                    Color.FromArgb(241, 245, 249), Color.FromArgb(148, 163, 184),
                    custom ?? Color.FromArgb(56, 189, 248), 14, 10, 18);

            case 6: // 赛博霓虹 · 电竞深色
                return new ThemePalette(6, true,
                    Color.FromArgb(18, 16, 27), Color.FromArgb(31, 27, 46), Color.FromArgb(63, 52, 92),
                    Color.FromArgb(246, 243, 255), Color.FromArgb(167, 155, 196),
                    custom ?? Color.FromArgb(168, 85, 247), 10, 7, 20);

            case 7: // 黑金碳纤 · 旗舰深色
                return new ThemePalette(7, true,
                    Color.FromArgb(22, 21, 20), Color.FromArgb(36, 34, 32), Color.FromArgb(64, 59, 53),
                    Color.FromArgb(247, 244, 238), Color.FromArgb(168, 160, 148),
                    custom ?? Color.FromArgb(245, 158, 11), 11, 8, 18);

            default: // 0: 薰衣草 · Material 3
                return new ThemePalette(0, false,
                    Color.FromArgb(248, 245, 255), Color.FromArgb(235, 229, 249), Color.FromArgb(215, 205, 238),
                    Color.FromArgb(29, 25, 46), Color.FromArgb(92, 85, 118),
                    custom ?? Color.FromArgb(95, 75, 182), 18, 12, 16);
        }
    }

    internal static Color Blend(Color background, Color accent, int percent)
    {
        int rest = 100 - percent;
        return Color.FromArgb(
            (background.R * rest + accent.R * percent) / 100,
            (background.G * rest + accent.G * percent) / 100,
            (background.B * rest + accent.B * percent) / 100);
    }

    private static double Luminance(Color color)
    {
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static double Linear(byte channel)
    {
        double value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
