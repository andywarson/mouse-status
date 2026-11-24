using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal enum GlyphKind
{
    None,
    Settings,
    Close,
    Refresh
}

internal static class UiKit
{
    private static float cachedScale;

    internal static void EnableHighDpi()
    {
        try { SetProcessDPIAware(); }
        catch (EntryPointNotFoundException) { }
    }

    internal static float DpiScale
    {
        get
        {
            if (cachedScale <= 0f)
            {
                // Screen DC, not a Bitmap: a bitmap's resolution is not guaranteed to follow the display.
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float s = g.DpiX / 96f;
                    cachedScale = s < 1f ? 1f : (s > 3f ? 3f : s);
                }
            }
            return cachedScale;
        }
    }

    internal static int S(int px)
    {
        return (int)Math.Round(px * DpiScale);
    }

    private static int cachedBuild = -1;

    // Environment.OSVersion on .NET Framework is manifest-dependent and reports 6.2.9200
    // on Windows 10/11 for this exe, so ask ntdll for the real build number.
    internal static int WindowsBuild
    {
        get
        {
            if (cachedBuild < 0)
            {
                try
                {
                    var info = new OsVersionInfo { Size = Marshal.SizeOf(typeof(OsVersionInfo)) };
                    cachedBuild = RtlGetVersion(ref info) == 0 && info.Major >= 10 ? info.Build : 0;
                }
                catch (DllNotFoundException) { cachedBuild = 0; }
                catch (EntryPointNotFoundException) { cachedBuild = 0; }
            }
            return cachedBuild;
        }
    }

    internal static bool IsWindows11 { get { return WindowsBuild >= 22000; } }

    private static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

    internal static void ApplyWindowChrome(Form form, ThemePalette palette, bool borderlessRounded, bool smallCorners)
    {
        if (form == null || !form.IsHandleCreated) return;
        IntPtr hwnd = form.Handle;
        int dark = palette.IsDark ? 1 : 0;
        try
        {
            // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 from Windows 10 20H1; builds before that used 19.
            if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
            if (IsWindows11)
            {
                if (borderlessRounded)
                {
                    form.Region = null;
                    int corner = smallCorners ? 3 : 2; // DWMWCP_ROUNDSMALL = 3, DWMWCP_ROUND = 2
                    DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
                    int border = ColorRef(palette.Border);
                    DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
                }
                else
                {
                    // Blend the caption into the themed client area (DWMWA_CAPTION_COLOR / TEXT_COLOR).
                    int caption = ColorRef(palette.Surface);
                    int text = ColorRef(palette.Ink);
                    DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
                    DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));
                }
            }
            else if (borderlessRounded)
            {
                SetRegionRounded(form, S(smallCorners ? Math.Min(palette.Radius, 10) : palette.Radius));
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    internal static void SetRegionRounded(Control control, int radius)
    {
        if (control.Width < radius * 2 || control.Height < radius * 2)
            radius = Math.Min(control.Width, control.Height) / 2;
        if (radius <= 0) return;
        using (var path = RoundedPath(new RectangleF(0, 0, control.Width, control.Height), radius))
        {
            Region previous = control.Region;
            control.Region = new Region(path);
            if (previous != null) previous.Dispose();
        }
    }

    internal static GraphicsPath RoundedPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0) return path;
        float maxR = Math.Min(rect.Width, rect.Height) / 2f;
        float r = Math.Max(0f, Math.Min(radius, maxR));
        if (r <= 0.5f)
        {
            path.AddRectangle(rect);
            return path;
        }
        float d = r * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void FillRounded(Graphics g, Brush brush, RectangleF rect, float radius)
    {
        using (var path = RoundedPath(rect, radius))
            g.FillPath(brush, path);
    }

    internal static void DrawRounded(Graphics g, Pen pen, RectangleF rect, float radius)
    {
        using (var path = RoundedPath(rect, radius))
            g.DrawPath(pen, path);
    }

    internal static void DrawGlyph(Graphics g, GlyphKind kind, Rectangle bounds, Color color)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = bounds.Left + bounds.Width / 2f;
        float cy = bounds.Top + bounds.Height / 2f;
        float unit = Math.Min(bounds.Width, bounds.Height) * 0.28f;
        float stroke = Math.Max(1.5f, 1.55f * DpiScale);

        using (var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        using (var fill = new SolidBrush(color))
        {
            if (kind == GlyphKind.Close)
            {
                float d = unit * 0.85f;
                g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
                g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d);
            }
            else if (kind == GlyphKind.Settings)
            {
                float rOuter = unit * 1.06f;
                float rInner = unit * 0.52f;
                float rHub = unit * 0.78f;
                for (int i = 0; i < 6; i++)
                {
                    double a = i * Math.PI / 3.0;
                    float x1 = cx + (float)(Math.Cos(a) * rHub * 0.85f);
                    float y1 = cy + (float)(Math.Sin(a) * rHub * 0.85f);
                    float x2 = cx + (float)(Math.Cos(a) * rOuter);
                    float y2 = cy + (float)(Math.Sin(a) * rOuter);
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
                g.DrawEllipse(pen, cx - rHub, cy - rHub, rHub * 2f, rHub * 2f);
                using (var clearBrush = new SolidBrush(Color.FromArgb(40, color)))
                    g.FillEllipse(clearBrush, cx - rInner * 0.6f, cy - rInner * 0.6f, rInner * 1.2f, rInner * 1.2f);
            }
            else if (kind == GlyphKind.Refresh)
            {
                float r = unit * 0.95f;
                g.DrawArc(pen, cx - r, cy - r, r * 2f, r * 2f, -35f, 285f);
                double tipAngle = (-35.0) * Math.PI / 180.0;
                float tx = cx + (float)(Math.Cos(tipAngle) * r);
                float ty = cy + (float)(Math.Sin(tipAngle) * r);
                float ah = unit * 0.52f;
                g.DrawLine(pen, tx, ty, tx - ah, ty - ah * 0.2f);
                g.DrawLine(pen, tx, ty, tx + ah * 0.15f, ty - ah);
            }
        }
        g.Restore(state);
    }

    internal static void DrawMouseBadge(Graphics g, Rectangle bounds, ThemePalette palette)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1f, bounds.Height - 1f);
        using (var bg = new SolidBrush(palette.AccentTile))
        using (var border = new Pen(palette.AccentBorder, 1f))
        {
            FillRounded(g, bg, rect, Math.Min(palette.TileRadius, S(8)));
            DrawRounded(g, border, rect, Math.Min(palette.TileRadius, S(8)));
        }
        float mw = bounds.Width * 0.44f;
        float mh = bounds.Height * 0.64f;
        float mx = bounds.X + (bounds.Width - mw) / 2f;
        float my = bounds.Y + (bounds.Height - mh) / 2f;
        using (var pen = new Pen(palette.Accent, Math.Max(1.4f, 1.4f * DpiScale)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var path = new GraphicsPath())
        {
            path.AddArc(mx, my, mw, mw, 180, 180);
            path.AddArc(mx, my + mh - mw, mw, mw, 0, 180);
            path.CloseFigure();
            g.DrawPath(pen, path);
            float cx = bounds.X + bounds.Width / 2f;
            g.DrawLine(pen, cx, my + mh * 0.22f, cx, my + mh * 0.42f);
        }
        g.Restore(state);
    }

    internal static void DrawBolt(Graphics g, RectangleF bounds, Color color)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;
        var pts = new[]
        {
            new PointF(x + w * 0.58f, y),
            new PointF(x + w * 0.16f, y + h * 0.54f),
            new PointF(x + w * 0.48f, y + h * 0.54f),
            new PointF(x + w * 0.38f, y + h),
            new PointF(x + w * 0.86f, y + h * 0.44f),
            new PointF(x + w * 0.54f, y + h * 0.44f)
        };
        using (var brush = new SolidBrush(color))
            g.FillPolygon(brush, pts);
        g.Restore(state);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersionInfo
    {
        internal int Size, Major, Minor, Build, Platform;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Csd;
    }

    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
    private static extern int RtlGetVersion(ref OsVersionInfo info);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}

internal sealed class ModernButton : Button
{
    private bool hovered;
    private bool pressed;
    internal bool Primary;
    internal GlyphKind Glyph = GlyphKind.None;
    internal int CornerRadius = 10;
    internal Color SurfaceFill = Color.FromArgb(235, 229, 249);
    internal Color HoverFill = Color.FromArgb(222, 215, 242);
    internal Color BorderStroke = Color.FromArgb(215, 205, 238);
    internal Color InkColor = Color.FromArgb(29, 25, 46);

    internal ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
    }

    protected override bool ShowFocusCues { get { return false; } }

    public override void NotifyDefault(bool value)
    {
        base.NotifyDefault(false);
    }

    internal void ApplyPalette(ThemePalette palette, bool primary, int radius)
    {
        Primary = primary;
        CornerRadius = radius;
        if (primary)
        {
            SurfaceFill = palette.Accent;
            HoverFill = ThemePalette.Blend(palette.Accent, palette.IsDark ? Color.White : Color.Black, 12);
            BorderStroke = palette.Accent;
            InkColor = palette.OnAccent;
        }
        else
        {
            SurfaceFill = palette.NeutralTile;
            HoverFill = palette.Hover;
            BorderStroke = palette.Border;
            InkColor = palette.Ink;
        }
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left) { pressed = true; Invalidate(); }
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        if (pressed) { pressed = false; Invalidate(); }
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color parentBg = Parent != null ? Parent.BackColor : SystemColors.Control;
        g.Clear(parentBg);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = Math.Min(CornerRadius, Math.Min(rect.Width, rect.Height) / 2f);
        Color fill = !Enabled ? ThemePalette.Blend(parentBg, SurfaceFill, 45)
                   : pressed ? ThemePalette.Blend(HoverFill, Color.Black, 10)
                   : hovered ? HoverFill
                   : SurfaceFill;

        using (var brush = new SolidBrush(fill))
            UiKit.FillRounded(g, brush, rect, radius);

        if (!Primary)
        {
            using (var pen = new Pen(BorderStroke, 1f))
                UiKit.DrawRounded(g, pen, rect, radius);
        }

        Color fg = Enabled ? InkColor : ThemePalette.Blend(parentBg, InkColor, 45);
        if (Glyph != GlyphKind.None)
        {
            UiKit.DrawGlyph(g, Glyph, ClientRectangle, fg);
        }
        else if (!string.IsNullOrEmpty(Text))
        {
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }
}

internal sealed class ModernComboBox : ComboBox
{
    private const int WmPaint = 0x000F;
    private ThemePalette palette = ThemePalette.Create(0, null);

    internal ModernComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        ItemHeight = UiKit.S(22);
    }

    internal void ApplyPalette(ThemePalette next)
    {
        palette = next;
        BackColor = next.Surface;
        ForeColor = next.Ink;
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count)
        {
            using (var empty = new SolidBrush(palette.Surface))
                e.Graphics.FillRectangle(empty, e.Bounds);
            return;
        }
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected &&
                        (e.State & DrawItemState.ComboBoxEdit) == 0;
        Color bg = selected ? palette.Accent : palette.Surface;
        Color fg = selected ? palette.OnAccent : palette.Ink;
        using (var brush = new SolidBrush(bg))
            e.Graphics.FillRectangle(brush, e.Bounds);

        string text = Items[e.Index] != null ? Items[e.Index].ToString() : "";
        var textBounds = new Rectangle(e.Bounds.X + UiKit.S(6), e.Bounds.Y, Math.Max(10, e.Bounds.Width - UiKit.S(10)), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, Font, textBounds, fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if ((m.Msg == WmPaint || m.Msg == 0x0318) && Width > 4 && Height > 4)
        {
            using (var g = m.Msg == 0x0318 && m.WParam != IntPtr.Zero ? Graphics.FromHdc(m.WParam) : Graphics.FromHwnd(Handle))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var border = new Pen(palette.Border, 1.5f))
                using (var mask = new Pen(palette.Surface, 2f))
                {
                    g.DrawRectangle(mask, 1, 1, Width - 3, Height - 3);
                    g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
                }
                int arrowAreaLeft = Width - UiKit.S(22);
                using (var bg = new SolidBrush(palette.Surface))
                    g.FillRectangle(bg, arrowAreaLeft, 1, UiKit.S(21), Height - 2);
                float cx = arrowAreaLeft + UiKit.S(10);
                float cy = Height / 2f;
                float s = UiKit.S(4);
                using (var arrowPen = new Pen(palette.Muted, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(arrowPen, cx - s, cy - s * 0.45f, cx, cy + s * 0.55f);
                    g.DrawLine(arrowPen, cx, cy + s * 0.55f, cx + s, cy - s * 0.45f);
                }
            }
        }
    }
}
