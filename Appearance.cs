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
    internal int Theme;
    internal int? Accent;
    internal byte[] Picture;
    internal bool PictureOnFloating = true;
    internal bool PictureOnTaskbar;

    internal DisplayPreferences Copy() { return (DisplayPreferences)MemberwiseClone(); }

    internal static DisplayPreferences FromSwitches(object floating, object taskbar, object legacyMode)
    {
        bool legacyTray = Equals(legacyMode, 1);
        return new DisplayPreferences {
            Floating = floating is int ? Equals(floating, 1) : !legacyTray,
            Taskbar = taskbar is int ? Equals(taskbar, 1) : legacyTray
        };
    }

    internal static DisplayPreferences Read(RegistryKey key)
    {
        if (key == null) return FromSwitches(null, null, null);
        var display = FromSwitches(key.GetValue("ShowFloating"), key.GetValue("ShowTaskbar"), key.GetValue("TrayOnly"));
        display.Compact = Equals(key.GetValue("CompactFloating"), 1);
        display.Theme = ThemePalette.Normalize(key.GetValue("Theme") as int? ?? 0);
        display.Accent = key.GetValue("Accent") as int?;
        display.Picture = key.GetValue("Picture") as byte[];
        display.PictureOnFloating = !Equals(key.GetValue("PictureOnFloating"), 0);
        display.PictureOnTaskbar = Equals(key.GetValue("PictureOnTaskbar"), 1);
        return display;
    }

    internal void SaveSwitches(RegistryKey key)
    {
        key.SetValue("ShowFloating", Floating ? 1 : 0);
        key.SetValue("ShowTaskbar", Taskbar ? 1 : 0);
    }

    internal void Save(RegistryKey key)
    {
        SaveSwitches(key);
        key.SetValue("CompactFloating", Compact ? 1 : 0);
        key.SetValue("Theme", Theme);
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
    internal static readonly string[] Names = { "Material 3 · 色调卡片", "Fluent 2 · 浅色", "石墨 · 深色极简" };
    internal readonly Color Surface, Ink, Muted, Accent, OnAccent, AccentTile, NeutralTile;
    internal readonly int Radius, TileRadius;

    private ThemePalette(Color surface, Color ink, Color muted, Color accent, Color neutral, int radius, int tileRadius, int tint)
    {
        Surface = surface;
        Ink = ink;
        Muted = muted;
        Accent = accent;
        OnAccent = Luminance(accent) > 0.179 ? Color.Black : Color.White;
        AccentTile = Blend(surface, accent, tint);
        NeutralTile = neutral;
        Radius = radius;
        TileRadius = tileRadius;
    }

    internal static int Normalize(int index) { return index >= 0 && index < Names.Length ? index : 0; }

    internal static ThemePalette Create(int index, int? accent)
    {
        Color? color = accent.HasValue ? (Color?)Color.FromArgb(accent.Value | unchecked((int)0xFF000000)) : null;
        switch (Normalize(index))
        {
            case 1:
                return new ThemePalette(Color.FromArgb(249, 251, 253), Color.FromArgb(22, 30, 41), Color.FromArgb(66, 79, 94),
                    color ?? Color.FromArgb(0, 93, 184), Color.FromArgb(236, 241, 247), 11, 8, 13);
            case 2:
                return new ThemePalette(Color.FromArgb(27, 29, 34), Color.FromArgb(244, 246, 249), Color.FromArgb(180, 188, 199),
                    color ?? Color.FromArgb(146, 207, 190), Color.FromArgb(43, 46, 53), 9, 5, 18);
            default:
                return new ThemePalette(Color.FromArgb(248, 246, 255), Color.FromArgb(27, 26, 43), Color.FromArgb(70, 67, 93),
                    color ?? Color.FromArgb(89, 72, 164), Color.FromArgb(233, 226, 246), 25, 17, 18);
        }
    }

    private static Color Blend(Color background, Color accent, int percent)
    {
        int rest = 100 - percent;
        return Color.FromArgb((background.R * rest + accent.R * percent) / 100,
            (background.G * rest + accent.G * percent) / 100, (background.B * rest + accent.B * percent) / 100);
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
