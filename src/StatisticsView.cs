using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

internal sealed class StatisticsView : UserControl
{
    private readonly HistoryStore history;
    private readonly ModernComboBox range;
    private readonly ModernButton modeRateBtn;
    private readonly ModernButton modeDpiBtn;
    private readonly Label title;
    private readonly Label footnote;
    private readonly KpiStrip kpiStrip;
    private readonly HistoryChart batteryChart;
    private readonly HistoryChart donutChart;
    private readonly Font headingFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
    private readonly Font smallFont = new Font("Microsoft YaHei UI", 8.5f);
    private ThemePalette currentPalette = ThemePalette.Create(0, null);
    private bool showDpiDonut;

    internal StatisticsView(HistoryStore history)
    {
        this.history = history;
        Dock = DockStyle.Fill;
        DoubleBuffered = true;

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(UiKit.S(8), UiKit.S(6), UiKit.S(8), UiKit.S(4)),
            RowCount = 4,
            ColumnCount = 1
        };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, UiKit.S(32)));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, UiKit.S(50)));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, UiKit.S(26)));
        Controls.Add(stack);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(76)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(84)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiKit.S(124)));

        title = new Label
        {
            Text = "当前设备",
            Dock = DockStyle.Fill,
            Font = headingFont,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        header.Controls.Add(title, 0, 0);

        modeRateBtn = new ModernButton
        {
            Text = "回报率",
            Font = smallFont,
            Dock = DockStyle.Fill,
            Margin = new Padding(UiKit.S(2), UiKit.S(2), UiKit.S(2), UiKit.S(2)),
            AccessibleName = "切换回报率占比图"
        };
        modeDpiBtn = new ModernButton
        {
            Text = "灵敏度",
            Font = smallFont,
            Dock = DockStyle.Fill,
            Margin = new Padding(UiKit.S(2), UiKit.S(2), UiKit.S(6), UiKit.S(2)),
            AccessibleName = "切换灵敏度占比图"
        };
        modeRateBtn.Click += delegate { SetDonutMode(false); };
        modeDpiBtn.Click += delegate { SetDonutMode(true); };
        header.Controls.Add(modeRateBtn, 1, 0);
        header.Controls.Add(modeDpiBtn, 2, 0);

        range = new ModernComboBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, UiKit.S(2), 0, 0),
            AccessibleName = "统计时间范围"
        };
        range.Items.AddRange(new object[] { "最近 1 小时", "最近 24 小时", "最近 7 天" });
        range.SelectedIndex = 0;
        header.Controls.Add(range, 3, 0);
        stack.Controls.Add(header, 0, 0);

        kpiStrip = new KpiStrip { Dock = DockStyle.Fill, Margin = new Padding(0, UiKit.S(3), 0, UiKit.S(4)) };
        stack.Controls.Add(kpiStrip, 0, 1);

        var charts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        batteryChart = new HistoryChart(true) { Dock = DockStyle.Fill, Margin = new Padding(0, UiKit.S(2), UiKit.S(5), UiKit.S(2)) };
        donutChart = new HistoryChart(false) { Dock = DockStyle.Fill, Margin = new Padding(UiKit.S(5), UiKit.S(2), 0, UiKit.S(2)) };
        charts.Controls.Add(batteryChart, 0, 0);
        charts.Controls.Add(donutChart, 1, 0);
        stack.Controls.Add(charts, 0, 2);

        footnote = new Label { Dock = DockStyle.Fill, Font = smallFont, TextAlign = ContentAlignment.MiddleLeft };
        stack.Controls.Add(footnote, 0, 3);

        range.SelectedIndexChanged += delegate { RefreshData(title.Text); };
    }

    private void SetDonutMode(bool showDpi)
    {
        showDpiDonut = showDpi;
        donutChart.ShowDpiMode = showDpi;
        UpdateToggleStyles();
        RefreshData(title.Text);
    }

    private void UpdateToggleStyles()
    {
        modeRateBtn.ApplyPalette(currentPalette, !showDpiDonut, UiKit.S(7));
        modeDpiBtn.ApplyPalette(currentPalette, showDpiDonut, UiKit.S(7));
    }

    internal void ApplyTheme(ThemePalette theme)
    {
        currentPalette = theme;
        BackColor = theme.Surface;
        ForeColor = theme.Ink;
        title.ForeColor = theme.Ink;
        footnote.ForeColor = theme.Muted;
        range.ApplyPalette(theme);
        kpiStrip.ApplyTheme(theme);
        batteryChart.ApplyTheme(theme);
        donutChart.ApplyTheme(theme);
        UpdateToggleStyles();
    }

    internal void RefreshData(string device)
    {
        if (IsDisposed) return;
        title.Text = string.IsNullOrWhiteSpace(device) ? "当前设备" : device;
        DateTime end = DateTime.UtcNow;
        DateTime start = end.AddHours(-(range.SelectedIndex == 2 ? 168 : range.SelectedIndex == 1 ? 24 : 1));
        var points = history.Snapshot(start);
        var metrics = HistoryMetrics.Calculate(points, start, end);
        double? rangeEst = metrics.EstimatedRemainingHours(null);
        double? fallbackEst = rangeEst ?? history.EstimateRemainingHours(metrics.LatestBattery, metrics.LatestCharging, end);
        kpiStrip.SetMetrics(metrics, fallbackEst);
        batteryChart.SetData(points, start, end, metrics);
        donutChart.SetData(points, start, end, metrics);
        footnote.Text = history.CanSave
            ? "鼠标悬停折线图可查看单点读数；占比按相邻有效采样间隔估算，断连与休眠期间自动留空。"
            : "历史暂时仅保存在本次运行内存中；占比按相邻有效采样间隔估算。";
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            headingFont.Dispose();
            smallFont.Dispose();
        }
    }
}

internal sealed class KpiStrip : Control
{
    private readonly Font labelFont = new Font("Microsoft YaHei UI", 8f);
    private readonly Font valueFont = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
    private ThemePalette theme = ThemePalette.Create(0, null);
    private readonly string[] labels = { "最新电量", "未充电掉电", "预计续航", "常用灵敏度", "常用回报率" };
    private readonly string[] values = { "—", "—", "—", "—", "—" };

    internal KpiStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    internal void ApplyTheme(ThemePalette palette)
    {
        theme = palette;
        BackColor = palette.Surface;
        Invalidate();
    }

    internal void SetMetrics(HistoryMetrics m, double? estimatedHours)
    {
        values[0] = m.LatestBattery.HasValue
            ? (m.MinBattery.HasValue && m.MaxBattery.HasValue && m.MinBattery.Value != m.MaxBattery.Value
                ? string.Format(CultureInfo.InvariantCulture, "{0}% ({1}–{2}%)", m.LatestBattery.Value, m.MinBattery.Value, m.MaxBattery.Value)
                : m.LatestBattery.Value.ToString(CultureInfo.InvariantCulture) + "%")
            : "—";
        values[1] = m.HasDischargePairs ? ("-" + m.ObservedDrop.ToString(CultureInfo.InvariantCulture) + "%") : "0%";
        values[2] = m.LatestBattery.HasValue
            ? HistoryMetrics.FormatRemainingHours(estimatedHours, m.LatestCharging, true)
            : "—";
        values[3] = m.DominantDpi.HasValue ? (m.DominantDpi.Value.ToString(CultureInfo.InvariantCulture) + " DPI") : "—";
        values[4] = m.DominantRate.HasValue ? (m.DominantRate.Value.ToString(CultureInfo.InvariantCulture) + " Hz") : "—";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(theme.Surface);
        int count = labels.Length;
        int gap = UiKit.S(7);
        int cardW = (Width - gap * (count - 1)) / count;
        int h = Height - 1;
        if (cardW < 36 || h < 20) return;

        using (var fill = new SolidBrush(theme.NeutralTile))
        using (var accentFill = new SolidBrush(theme.AccentTile))
        using (var border = new Pen(theme.Border, 1f))
        {
            for (int i = 0; i < count; i++)
            {
                int left = i * (cardW + gap);
                int w = i == count - 1 ? (Width - left - 1) : cardW;
                var rect = new RectangleF(left + 0.5f, 0.5f, w, h - 1f);
                float r = Math.Min(UiKit.S(theme.TileRadius), UiKit.S(8));
                UiKit.FillRounded(g, i == 0 ? accentFill : fill, rect, r);
                UiKit.DrawRounded(g, border, rect, r);

                var topBox = new Rectangle(left + UiKit.S(9), UiKit.S(3), w - UiKit.S(12), UiKit.S(16));
                var botBox = new Rectangle(left + UiKit.S(9), UiKit.S(19), w - UiKit.S(12), h - UiKit.S(21));
                TextRenderer.DrawText(g, labels[i], labelFont, topBox, theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, values[i], valueFont, botBox, theme.Ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { labelFont.Dispose(); valueFont.Dispose(); }
    }
}

internal sealed class HistoryChart : Control
{
    private static readonly Color[] SeriesColors = {
        Color.FromArgb(79, 110, 247),
        Color.FromArgb(16, 185, 129),
        Color.FromArgb(245, 158, 11),
        Color.FromArgb(168, 85, 247),
        Color.FromArgb(236, 72, 153),
        Color.FromArgb(14, 165, 233)
    };

    private sealed class PlottedSample
    {
        internal Point Screen;
        internal HistorySample Sample;
    }

    private readonly bool battery;
    internal bool ShowDpiMode;
    private readonly Font heading = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
    private readonly Font caption = new Font("Microsoft YaHei UI", 8f);
    private readonly Font center = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
    private IList<HistorySample> points = new HistorySample[0];
    private readonly List<PlottedSample> plotted = new List<PlottedSample>();
    private PlottedSample hoveredSample;
    private DateTime start;
    private DateTime end;
    private HistoryMetrics metrics = new HistoryMetrics();
    private ThemePalette theme = ThemePalette.Create(0, null);

    internal HistoryChart(bool showBattery)
    {
        battery = showBattery;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        UpdateAccessibleMetadata();
    }

    private void UpdateAccessibleMetadata()
    {
        AccessibleName = battery
            ? "电量变化折线图"
            : (ShowDpiMode ? "灵敏度档位使用比例环形图" : "回报率档位使用比例环形图");
        Text = AccessibleName;
    }

    internal void ApplyTheme(ThemePalette palette)
    {
        theme = palette;
        BackColor = palette.Surface;
        Invalidate();
    }

    internal void SetData(IList<HistorySample> history, DateTime from, DateTime until, HistoryMetrics summary)
    {
        points = history;
        start = from;
        end = until;
        metrics = summary;
        hoveredSample = null;
        UpdateAccessibleMetadata();
        double activeSeconds = ShowDpiMode ? summary.TotalDpiSeconds : summary.TotalSeconds;
        AccessibleDescription = battery ? DropLabel() : DurationLabel(activeSeconds) + "有效时长";
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!battery || plotted.Count == 0) return;
        PlottedSample nearest = null;
        int bestDist = UiKit.S(24);
        for (int i = 0; i < plotted.Count; i++)
        {
            int dx = Math.Abs(plotted[i].Screen.X - e.X);
            if (dx <= bestDist)
            {
                bestDist = dx;
                nearest = plotted[i];
            }
        }
        if (!ReferenceEquals(nearest, hoveredSample))
        {
            hoveredSample = nearest;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoveredSample != null)
        {
            hoveredSample = null;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(theme.Surface);
        if (Width < 120 || Height < 120) return;

        var cardRect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float cardRadius = UiKit.S(theme.TileRadius);
        using (var fill = new SolidBrush(theme.NeutralTile))
        using (var border = new Pen(theme.Border, 1f))
        {
            UiKit.FillRounded(g, fill, cardRect, cardRadius);
            UiKit.DrawRounded(g, border, cardRect, cardRadius);
        }

        string headerText = battery ? "电量变化趋势" : (ShowDpiMode ? "灵敏度 (DPI) 占比" : "回报率档位占比");
        DrawText(g, headerText, heading, new Rectangle(UiKit.S(13), UiKit.S(8), Width - UiKit.S(26), UiKit.S(22)), theme.Ink);
        if (battery) PaintBattery(g);
        else PaintDonut(g);
    }

    private void PaintBattery(Graphics canvas)
    {
        plotted.Clear();
        DrawText(canvas, DropLabel(), caption, new Rectangle(UiKit.S(13), UiKit.S(30), Width - UiKit.S(26), UiKit.S(18)), theme.Muted);
        var plot = Rectangle.FromLTRB(UiKit.S(48), UiKit.S(54), Width - UiKit.S(16), Height - UiKit.S(38));
        if (plot.Width < 30 || plot.Height < 30) return;

        using (var grid = new Pen(Color.FromArgb(45, theme.Muted)))
        {
            for (int percent = 0; percent <= 100; percent += 25)
            {
                int y = plot.Bottom - percent * plot.Height / 100;
                canvas.DrawLine(grid, plot.Left, y, plot.Right, y);
                DrawText(canvas, percent + "%", caption, new Rectangle(UiKit.S(4), y - UiKit.S(8), UiKit.S(39), UiKit.S(16)), theme.Muted,
                    TextFormatFlags.Right);
            }
        }
        if (end <= start) return;

        var firstReading = points.FirstOrDefault(point => point.Time >= start && point.Time <= end && point.Battery.HasValue);
        DateTime visibleStart = firstReading == null ? start : firstReading.Time;
        if ((end - visibleStart).TotalSeconds < 10) visibleStart = end.AddSeconds(-10);
        bool brief = (end - visibleStart).TotalMinutes < 15;
        DrawText(canvas, TimeLabel(visibleStart, brief), caption,
            new Rectangle(plot.Left, plot.Bottom + UiKit.S(4), UiKit.S(95), UiKit.S(16)), theme.Muted);
        DrawText(canvas, TimeLabel(end, brief), caption,
            new Rectangle(plot.Right - UiKit.S(95), plot.Bottom + UiKit.S(4), UiKit.S(95), UiKit.S(16)), theme.Muted, TextFormatFlags.Right);

        Color lineInk = theme.Accent;
        if (!theme.IsDark && theme.OnAccent.R < 50) lineInk = ThemePalette.Blend(theme.Ink, theme.Accent, 70);
        Color chargeColor = theme.Success;

        Point? priorPoint = null;
        HistorySample prior = null;
        var segmentPts = new List<Point>();

        // Pass 1: Area fills underneath
        foreach (var point in points)
        {
            if (point.Time < start || point.Time > end || !point.Battery.HasValue)
            {
                FlushAreaFill(canvas, segmentPts, plot.Bottom, lineInk);
                prior = null;
                continue;
            }
            int x = plot.Left + (int)Math.Round((point.Time - visibleStart).TotalSeconds / (end - visibleStart).TotalSeconds * plot.Width);
            x = Math.Max(plot.Left, Math.Min(plot.Right, x));
            int y = plot.Bottom - point.Battery.Value * plot.Height / 100;
            var here = new Point(x, y);
            if (prior == null || !point.Follows(prior))
                FlushAreaFill(canvas, segmentPts, plot.Bottom, lineInk);
            segmentPts.Add(here);
            prior = point;
        }
        FlushAreaFill(canvas, segmentPts, plot.Bottom, lineInk);

        // Pass 2: Crisp lines and dots on top
        prior = null;
        priorPoint = null;
        using (var normal = new Pen(lineInk, 2.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        using (var charge = new Pen(chargeColor, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var normalDot = new SolidBrush(lineInk))
        using (var chargeDot = new SolidBrush(chargeColor))
        {
            foreach (var point in points)
            {
                if (point.Time < start || point.Time > end || !point.Battery.HasValue)
                {
                    prior = null;
                    priorPoint = null;
                    continue;
                }
                int x = plot.Left + (int)Math.Round((point.Time - visibleStart).TotalSeconds / (end - visibleStart).TotalSeconds * plot.Width);
                x = Math.Max(plot.Left, Math.Min(plot.Right, x));
                int y = plot.Bottom - point.Battery.Value * plot.Height / 100;
                var here = new Point(x, y);
                plotted.Add(new PlottedSample { Screen = here, Sample = point });

                if (prior != null && point.Follows(prior))
                {
                    if (priorPoint.Value != here)
                        canvas.DrawLine(point.Charging == true || prior.Charging == true ? charge : normal, priorPoint.Value, here);
                }
                else
                {
                    int r = UiKit.S(3);
                    canvas.FillEllipse(point.Charging == true ? chargeDot : normalDot, x - r, y - r, r * 2, r * 2);
                }
                prior = point;
                priorPoint = here;
            }

            if (plotted.Count == 0)
            {
                DrawText(canvas, "暂无电量记录", center, plot, theme.Muted, TextFormatFlags.HorizontalCenter);
            }
            else if (hoveredSample != null)
            {
                PaintHoverCallout(canvas, plot, hoveredSample, lineInk, chargeColor);
            }
        }
        DrawText(canvas, "● 绿色线段表示充电区间", caption,
            new Rectangle(UiKit.S(13), Height - UiKit.S(20), Width - UiKit.S(26), UiKit.S(16)), chargeColor);
    }

    private static void FlushAreaFill(Graphics g, List<Point> pts, int bottomY, Color baseColor)
    {
        if (pts.Count >= 2 && pts[pts.Count - 1].X > pts[0].X)
        {
            var poly = new Point[pts.Count + 2];
            for (int i = 0; i < pts.Count; i++) poly[i] = pts[i];
            poly[pts.Count] = new Point(pts[pts.Count - 1].X, bottomY);
            poly[pts.Count + 1] = new Point(pts[0].X, bottomY);
            int topY = pts.Min(p => p.Y);
            var gradRect = new Rectangle(pts[0].X, Math.Max(0, topY - 2), Math.Max(1, pts[pts.Count - 1].X - pts[0].X), Math.Max(4, bottomY - topY + 4));
            using (var brush = new LinearGradientBrush(gradRect, Color.FromArgb(42, baseColor), Color.FromArgb(2, baseColor), LinearGradientMode.Vertical))
                g.FillPolygon(brush, poly);
        }
        pts.Clear();
    }

    private void PaintHoverCallout(Graphics g, Rectangle plot, PlottedSample item, Color lineInk, Color chargeColor)
    {
        Point pt = item.Screen;
        var s = item.Sample;
        using (var guide = new Pen(Color.FromArgb(90, theme.Muted), 1f) { DashStyle = DashStyle.Dot })
            g.DrawLine(guide, pt.X, plot.Top, pt.X, plot.Bottom);

        Color dotColor = s.Charging == true ? chargeColor : lineInk;
        int r = UiKit.S(4);
        using (var halo = new SolidBrush(Color.FromArgb(60, dotColor)))
        using (var solid = new SolidBrush(dotColor))
        {
            g.FillEllipse(halo, pt.X - r - 3, pt.Y - r - 3, (r + 3) * 2, (r + 3) * 2);
            g.FillEllipse(solid, pt.X - r, pt.Y - r, r * 2, r * 2);
        }

        string tip = s.Time.ToLocalTime().ToString("HH:mm:ss") + " · " + s.Battery.Value + "%";
        if (s.Dpi.HasValue) tip += " · " + s.Dpi.Value + " DPI";
        if (s.Rate.HasValue) tip += " · " + s.Rate.Value + "Hz";
        Size textSz = TextRenderer.MeasureText(tip, caption);
        int boxW = textSz.Width + UiKit.S(14);
        int boxH = UiKit.S(22);
        int bx = Math.Max(UiKit.S(8), Math.Min(Width - boxW - UiKit.S(8), pt.X - boxW / 2));
        int by = pt.Y - boxH - UiKit.S(8);
        if (by < UiKit.S(30)) by = pt.Y + UiKit.S(8);

        var boxRect = new RectangleF(bx, by, boxW, boxH);
        using (var bg = new SolidBrush(theme.Surface))
        using (var pen = new Pen(theme.AccentBorder, 1f))
        {
            UiKit.FillRounded(g, bg, boxRect, UiKit.S(6));
            UiKit.DrawRounded(g, pen, boxRect, UiKit.S(6));
        }
        DrawText(g, tip, caption, Rectangle.Round(boxRect), theme.Ink, TextFormatFlags.HorizontalCenter);
    }

    private void PaintDonut(Graphics canvas)
    {
        SortedDictionary<int, double> buckets = ShowDpiMode ? metrics.Dpis : metrics.Rates;
        double totalSeconds = ShowDpiMode ? metrics.TotalDpiSeconds : metrics.TotalSeconds;
        string unit = ShowDpiMode ? " DPI" : " Hz";

        DrawText(canvas, totalSeconds > 0 ? "有效观测 " + DurationLabel(totalSeconds) : "等待连续有效读数",
            caption, new Rectangle(UiKit.S(13), UiKit.S(30), Width - UiKit.S(26), UiKit.S(18)), theme.Muted);

        int ringThickness = UiKit.S(13);
        int maxDiameter = Math.Min(Height - UiKit.S(76), Width / 2 - UiKit.S(20));
        int diameter = Math.Max(UiKit.S(64), Math.Min(UiKit.S(112), maxDiameter));
        Rectangle circle = new Rectangle(
            UiKit.S(16),
            UiKit.S(58) + Math.Max(0, (Height - UiKit.S(72) - diameter) / 2),
            diameter,
            diameter);

        using (var baseRing = new Pen(Color.FromArgb(45, theme.Muted), ringThickness))
            canvas.DrawEllipse(baseRing, circle);

        if (totalSeconds <= 0 || buckets.Count == 0)
        {
            DrawText(canvas, "暂无数据", center, circle, theme.Muted, TextFormatFlags.HorizontalCenter);
            DrawText(canvas, "需要至少两次连续\n有效设备采样读数", caption,
                new Rectangle(circle.Right + UiKit.S(14), circle.Top, Width - circle.Right - UiKit.S(20), circle.Height),
                theme.Muted, TextFormatFlags.WordBreak);
            return;
        }

        float angle = -90f;
        int index = 0;
        var ordered = buckets.OrderByDescending(kv => kv.Value).Take(6).ToList();
        int pctColWidth = UiKit.S(46);
        int legendLeft = circle.Right + UiKit.S(13);
        int rowStep = UiKit.S(22);

        foreach (var entry in ordered)
        {
            double share = entry.Value / totalSeconds;
            float arc = (float)(share * 360.0);
            Color color = SeriesColors[index % SeriesColors.Length];
            using (var segment = new Pen(color, ringThickness))
            using (var dot = new SolidBrush(color))
            {
                if (arc > 0.4f) canvas.DrawArc(segment, circle, angle, arc);
                int y = UiKit.S(58) + index * rowStep;
                if (y + UiKit.S(18) <= Height - UiKit.S(6))
                {
                    int dotSz = UiKit.S(8);
                    canvas.FillEllipse(dot, legendLeft, y + UiKit.S(6), dotSz, dotSz);
                    int textLeft = legendLeft + dotSz + UiKit.S(5);
                    int textWidth = Math.Max(UiKit.S(30), Width - textLeft - pctColWidth - UiKit.S(10));
                    DrawText(canvas, entry.Key.ToString(CultureInfo.InvariantCulture) + unit, caption,
                        new Rectangle(textLeft, y, textWidth, UiKit.S(20)), theme.Ink);
                    DrawText(canvas, Math.Round(share * 100).ToString("0", CultureInfo.InvariantCulture) + "%", caption,
                        new Rectangle(Width - pctColWidth - UiKit.S(10), y, pctColWidth, UiKit.S(20)), theme.Muted, TextFormatFlags.Right);
                }
            }
            angle += arc;
            index++;
        }
        DrawText(canvas, buckets.Count.ToString(CultureInfo.InvariantCulture) + " 个档位", center, circle, theme.Ink, TextFormatFlags.HorizontalCenter);
    }

    private string DropLabel()
    {
        return metrics.HasDischargePairs
            ? "未充电期间累计下降 " + metrics.ObservedDrop.ToString(CultureInfo.InvariantCulture) + " 个百分点"
            : "历史读数按电量百分比绘制";
    }

    private string TimeLabel(DateTime time, bool brief)
    {
        return time.ToLocalTime().ToString(brief ? "HH:mm:ss" : (end - start).TotalDays >= 2 ? "M/d HH:mm" : "HH:mm");
    }

    private static string DurationLabel(double seconds)
    {
        if (seconds >= 3600) return (seconds / 3600).ToString("0.0", CultureInfo.InvariantCulture) + " 小时";
        if (seconds >= 60) return (seconds / 60).ToString("0", CultureInfo.InvariantCulture) + " 分钟";
        return seconds.ToString("0", CultureInfo.InvariantCulture) + " 秒";
    }

    private static void DrawText(Graphics canvas, string value, Font font, Rectangle bounds, Color color,
                                 TextFormatFlags alignment = TextFormatFlags.Left)
    {
        TextRenderer.DrawText(canvas, value, font, bounds, color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | alignment);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { heading.Dispose(); caption.Dispose(); center.Dispose(); }
    }
}
