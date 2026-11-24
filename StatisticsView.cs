using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

internal sealed class StatisticsView : UserControl
{
    private readonly HistoryStore history;
    private readonly ComboBox range;
    private readonly Label title;
    private readonly Label footnote;
    private readonly HistoryChart batteryChart;
    private readonly HistoryChart rateChart;
    private readonly Font heading = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);

    internal StatisticsView(HistoryStore history)
    {
        this.history = history;
        Dock = DockStyle.Fill;

        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 3, ColumnCount = 1 };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        Controls.Add(stack);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        title = new Label { Text = "当前设备", Dock = DockStyle.Fill, Font = heading, AutoEllipsis = true };
        header.Controls.Add(title, 0, 0);
        range = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "统计时间范围" };
        range.Items.AddRange(new object[] { "最近 1 小时", "最近 24 小时", "最近 7 天" });
        range.SelectedIndex = 0;
        header.Controls.Add(range, 1, 0);
        stack.Controls.Add(header, 0, 0);

        var charts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        batteryChart = new HistoryChart(true) { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 5, 1) };
        rateChart = new HistoryChart(false) { Dock = DockStyle.Fill, Margin = new Padding(5, 1, 0, 1) };
        charts.Controls.Add(batteryChart, 0, 0);
        charts.Controls.Add(rateChart, 1, 0);
        stack.Controls.Add(charts, 0, 1);

        footnote = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        stack.Controls.Add(footnote, 0, 2);
        range.SelectedIndexChanged += delegate { RefreshData(title.Text); };
    }

    internal void ApplyTheme(ThemePalette theme)
    {
        BackColor = theme.Surface;
        ForeColor = theme.Ink;
        footnote.ForeColor = theme.Muted;
        batteryChart.ApplyTheme(theme);
        rateChart.ApplyTheme(theme);
    }

    internal void RefreshData(string device)
    {
        if (IsDisposed) return;
        title.Text = string.IsNullOrWhiteSpace(device) ? "当前设备" : device;
        DateTime end = DateTime.UtcNow;
        DateTime start = end.AddHours(-(range.SelectedIndex == 2 ? 168 : range.SelectedIndex == 1 ? 24 : 1));
        var points = history.Snapshot(start);
        var metrics = HistoryMetrics.Calculate(points, start, end);
        batteryChart.SetData(points, start, end, metrics);
        rateChart.SetData(points, start, end, metrics);
        footnote.Text = history.CanSave
            ? "占比按相邻有效读数之间的时长估算，断连和关机期间不计入。"
            : "历史暂时只保存在本次运行中。占比按相邻有效读数之间的时长估算。";
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) heading.Dispose();
    }
}

internal sealed class HistoryChart : Control
{
    private static readonly Color[] SeriesColors = {
        Color.FromArgb(63, 107, 215), Color.FromArgb(18, 135, 106), Color.FromArgb(203, 91, 54),
        Color.FromArgb(137, 83, 181), Color.FromArgb(171, 112, 24), Color.FromArgb(36, 132, 162)
    };
    private readonly bool battery;
    private readonly Font heading = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
    private readonly Font caption = new Font("Microsoft YaHei UI", 8);
    private readonly Font center = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
    private IList<HistorySample> points = new HistorySample[0];
    private DateTime start, end;
    private HistoryMetrics metrics = new HistoryMetrics();
    private ThemePalette theme = ThemePalette.Create(0, null);

    internal HistoryChart(bool showBattery)
    {
        battery = showBattery;
        DoubleBuffered = true;
        ResizeRedraw = true;
        AccessibleName = battery ? "电量变化折线图" : "回报率档位使用比例环形图";
    }

    internal void ApplyTheme(ThemePalette palette)
    {
        theme = palette;
        BackColor = theme.NeutralTile;
        StatusWidget.SetShape(this, theme.TileRadius);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        StatusWidget.SetShape(this, theme.TileRadius);
    }

    internal void SetData(IList<HistorySample> history, DateTime from, DateTime until, HistoryMetrics summary)
    {
        points = history;
        start = from;
        end = until;
        metrics = summary;
        AccessibleDescription = battery ? DropLabel() : DurationLabel(summary.TotalSeconds) + "有效时长";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 150 || Height < 145) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        DrawText(e.Graphics, battery ? "电量变化" : "回报率档位占比", heading,
            new Rectangle(13, 9, Width - 26, 23), theme.Ink);
        if (battery) PaintBattery(e.Graphics);
        else PaintRates(e.Graphics);
    }

    private void PaintBattery(Graphics canvas)
    {
        DrawText(canvas, DropLabel(), caption, new Rectangle(13, 35, Width - 26, 18), theme.Muted);
        var plot = Rectangle.FromLTRB(56, 64, Width - 16, Height - 45);
        using (var grid = new Pen(Color.FromArgb(45, theme.Muted)))
        {
            for (int percent = 0; percent <= 100; percent += 25)
            {
                int y = plot.Bottom - percent * plot.Height / 100;
                canvas.DrawLine(grid, plot.Left, y, plot.Right, y);
                DrawText(canvas, percent + "%", caption, new Rectangle(5, y - 8, 43, 16), theme.Muted,
                    TextFormatFlags.Right);
            }
        }
        if (end <= start) return;
        var firstReading = points.FirstOrDefault(point => point.Time >= start && point.Time <= end && point.Battery.HasValue);
        DateTime visibleStart = firstReading == null ? start : firstReading.Time;
        if ((end - visibleStart).TotalSeconds < 10) visibleStart = end.AddSeconds(-10);
        bool brief = (end - visibleStart).TotalMinutes < 15;
        DrawText(canvas, TimeLabel(visibleStart, brief), caption, new Rectangle(plot.Left, plot.Bottom + 6, 102, 18), theme.Muted);
        DrawText(canvas, TimeLabel(end, brief), caption, new Rectangle(plot.Right - 102, plot.Bottom + 6, 102, 18), theme.Muted,
            TextFormatFlags.Right);

        int count = 0;
        Point? priorPoint = null;
        HistorySample prior = null;
        var ink = theme.Accent;
        if (theme.OnAccent.ToArgb() == Color.Black.ToArgb() && theme.Ink.GetBrightness() < 0.5f) ink = theme.Ink;
        var chargeColor = theme.Ink.GetBrightness() > 0.7f ? Color.MediumAquamarine : Color.FromArgb(0, 125, 97);
        using (var normal = new Pen(ink, 2.4f))
        using (var charge = new Pen(chargeColor, 2.4f))
        using (var normalDot = new SolidBrush(ink))
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
                count++;
                int x = plot.Left + (int)Math.Round((point.Time - visibleStart).TotalSeconds / (end - visibleStart).TotalSeconds * plot.Width);
                int y = plot.Bottom - point.Battery.Value * plot.Height / 100;
                var here = new Point(x, y);
                if (prior != null && point.Follows(prior))
                {
                    if (priorPoint.Value != here)
                        canvas.DrawLine(point.Charging == true || prior.Charging == true ? charge : normal, priorPoint.Value, here);
                }
                else canvas.FillEllipse(point.Charging == true ? chargeDot : normalDot, x - 3, y - 3, 6, 6);
                prior = point;
                priorPoint = here;
            }
            if (count == 0) DrawText(canvas, "暂无电量记录", center, plot, theme.Muted, TextFormatFlags.HorizontalCenter);
        }
        DrawText(canvas, "绿色为充电区间", caption, new Rectangle(13, Height - 20, Width - 26, 16), chargeColor);
    }

    private void PaintRates(Graphics canvas)
    {
        DrawText(canvas, metrics.TotalSeconds > 0 ? "有效观测 " + DurationLabel(metrics.TotalSeconds) : "等待有效档位读数",
            caption, new Rectangle(13, 35, Width - 26, 18), theme.Muted);
        int diameter = Math.Min(112, Width / 2 - 18);
        Rectangle circle = new Rectangle(17, 62 + Math.Max(0, (Height - 80 - diameter) / 2), diameter, diameter);
        using (var baseRing = new Pen(Color.FromArgb(47, theme.Muted), 13))
            canvas.DrawEllipse(baseRing, circle);
        if (metrics.TotalSeconds <= 0)
        {
            DrawText(canvas, "暂无数据", center, circle, theme.Muted, TextFormatFlags.HorizontalCenter);
            DrawText(canvas, "需要连续两次\n有效档位读数", caption,
                new Rectangle(circle.Right + 14, circle.Top, Width - circle.Right - 20, circle.Height),
                theme.Muted, TextFormatFlags.WordBreak);
            return;
        }

        float angle = -90;
        int index = 0;
        foreach (var rate in metrics.Rates)
        {
            double share = rate.Value / metrics.TotalSeconds;
            float arc = (float)(share * 360);
            Color color = SeriesColors[index % SeriesColors.Length];
            using (var segment = new Pen(color, 13))
            using (var dot = new SolidBrush(color))
            {
                canvas.DrawArc(segment, circle, angle, arc);
                int y = 66 + index * 23;
                canvas.FillEllipse(dot, circle.Right + 13, y + 6, 8, 8);
                DrawText(canvas, rate.Key + " Hz", caption,
                    new Rectangle(circle.Right + 25, y, Width - circle.Right - 29, 20), theme.Ink);
                DrawText(canvas, share.ToString("P0"), caption,
                    new Rectangle(Width - 65, y, 52, 20), theme.Ink, TextFormatFlags.Right);
            }
            angle += arc;
            index++;
        }
        DrawText(canvas, metrics.Rates.Count + " 个档位", center, circle, theme.Ink, TextFormatFlags.HorizontalCenter);
    }

    private string DropLabel()
    {
        return metrics.HasDischargePairs ? "未充电期间累计下降 " + metrics.ObservedDrop + " 个百分点" : "历史读数以电量百分比显示";
    }

    private string TimeLabel(DateTime time, bool brief)
    {
        return time.ToLocalTime().ToString(brief ? "HH:mm:ss" : (end - start).TotalDays >= 2 ? "M/d HH:mm" : "HH:mm");
    }

    private static string DurationLabel(double seconds)
    {
        if (seconds >= 3600) return (seconds / 3600).ToString("0.0") + " 小时";
        if (seconds >= 60) return (seconds / 60).ToString("0") + " 分钟";
        return seconds.ToString("0") + " 秒";
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
