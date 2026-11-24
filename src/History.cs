using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

internal sealed class HistorySample
{
    internal readonly DateTime Time;
    internal readonly string Device;
    internal readonly string Segment;
    internal readonly int? Battery;
    internal readonly int? Rate;
    internal readonly int? Dpi;
    internal readonly bool? Charging;
    internal readonly int Interval;

    internal HistorySample(DateTime time, string device, string segment, int? battery, int? rate, bool? charging, int interval, int? dpi = null)
    {
        Time = time;
        Device = device;
        Segment = segment;
        Battery = battery;
        Rate = rate;
        Dpi = dpi;
        Charging = charging;
        Interval = interval;
    }

    internal bool HasReading { get { return Battery.HasValue || Rate.HasValue || Dpi.HasValue; } }

    internal bool Follows(HistorySample before)
    {
        var seconds = (Time - before.Time).TotalSeconds;
        return Device == before.Device && Segment == before.Segment &&
            seconds > 0 && seconds <= Math.Max(2 * before.Interval + 5, 15);
    }

    internal string Serialize()
    {
        var culture = CultureInfo.InvariantCulture;
        return string.Join("\t",
            Time.Ticks.ToString(culture),
            Device,
            Segment,
            Battery.HasValue ? Battery.Value.ToString(culture) : "",
            Rate.HasValue ? Rate.Value.ToString(culture) : "",
            Charging.HasValue ? (Charging.Value ? "1" : "0") : "",
            Interval.ToString(culture),
            Dpi.HasValue ? Dpi.Value.ToString(culture) : "");
    }

    internal static HistorySample Parse(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        var fields = line.Split('\t');
        long time;
        int interval, level, rate, dpiVal;
        if ((fields.Length != 7 && fields.Length != 8) ||
            !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out time) ||
            time < DateTime.MinValue.Ticks || time > DateTime.MaxValue.Ticks ||
            fields[1].Length != 64 || fields[2].Length != 32 ||
            !int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out interval) ||
            AppPreferences.RefreshSeconds(interval) != interval)
            return null;

        int? battery = int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out level) && level >= 0 && level <= 100
            ? (int?)level : null;
        int? hz = int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out rate) && rate >= 125 && rate <= 8000
            ? (int?)rate : null;
        bool? charging = fields[5] == "1" ? (bool?)true : fields[5] == "0" ? (bool?)false : null;
        int? dpi = fields.Length >= 8 && int.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out dpiVal) && dpiVal > 0 && dpiVal <= 50000
            ? (int?)dpiVal : null;
        return new HistorySample(new DateTime(time, DateTimeKind.Utc), fields[1], fields[2], battery, hz, charging, interval, dpi);
    }
}

internal sealed class HistoryStore
{
    internal const int RetentionDays = 14;
    private readonly Queue<HistorySample> samples = new Queue<HistorySample>();
    private readonly string folder;
    private string source;
    private string activeKey;
    private string legacyKey;
    private string segment = Guid.NewGuid().ToString("N");
    private HistorySample last;
    private DateTime? lastPrunedDay;
    private bool loaded;
    internal bool CanSave = true;

    internal HistoryStore(string path)
    {
        folder = path;
    }

    internal void Record(string deviceSource, string identity, MouseStatus status, int interval, DateTime now)
    {
        if (!string.Equals(source, deviceSource, StringComparison.Ordinal))
        {
            source = deviceSource;
            activeKey = source == "usb" ? PreviousUsbKey() : null;
            legacyKey = null;
            BeginSegment();
        }
        if (!string.IsNullOrEmpty(identity))
        {
            string key = Hash(identity);
            if (key != activeKey)
            {
                activeKey = key;
                legacyKey = null;
                BeginSegment();
                if (source == "usb") RememberUsbKey(key);
            }
        }
        if (activeKey == null) return;
        if (source == "usb" && !string.IsNullOrEmpty(status.LegacyDeviceKey))
            legacyKey = Hash(status.LegacyDeviceKey);
        if (!loaded) Load(now);

        var sample = new HistorySample(now, activeKey, segment, status.Battery, status.Rate, status.Charging, interval, status.Dpi);
        if (!sample.HasReading && (last == null || !last.HasReading)) return;
        samples.Enqueue(sample);
        last = sample;
        while (samples.Count != 0 && samples.Peek().Time < now.AddDays(-7)) samples.Dequeue();

        try
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(DayFile(now), sample.Serialize() + Environment.NewLine, Encoding.UTF8);
            CanSave = true;
            if (!lastPrunedDay.HasValue || lastPrunedDay.Value != now.Date)
            {
                lastPrunedDay = now.Date;
                PruneOldFiles(now);
            }
        }
        catch (IOException) { CanSave = false; }
        catch (UnauthorizedAccessException) { CanSave = false; }
    }

    internal void PruneOldFiles(DateTime now)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            DateTime keepFrom = now.Date.AddDays(-RetentionDays);
            foreach (string file in Directory.GetFiles(folder, "*.tsv"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                DateTime fileDate;
                if (DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fileDate) &&
                    fileDate < keepFrom)
                {
                    try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string PreviousUsbKey()
    {
        try
        {
            string path = Path.Combine(folder, "last-usb.txt");
            if (!File.Exists(path)) return null;
            string value = File.ReadAllText(path).Trim();
            return value.Length == 64 ? value : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void RememberUsbKey(string device)
    {
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "last-usb.txt"), device, Encoding.ASCII);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal IList<HistorySample> Snapshot(DateTime from)
    {
        if (activeKey == null) return new HistorySample[0];
        var earliest = from.AddMinutes(-11);
        return samples.Where(sample => (sample.Device == activeKey || sample.Device == legacyKey) && sample.Time >= earliest)
                      .OrderBy(sample => sample.Time).ToArray();
    }

    internal double? EstimateRemainingHours(int? currentBattery, bool? charging, DateTime now)
    {
        if (charging == true || activeKey == null) return null;
        var recent24h = Snapshot(now.AddHours(-24));
        var m24 = HistoryMetrics.Calculate(recent24h, now.AddHours(-24), now);
        double? est = m24.EstimatedRemainingHours(currentBattery);
        if (est.HasValue) return est;

        var recent7d = Snapshot(now.AddDays(-7));
        var m7d = HistoryMetrics.Calculate(recent7d, now.AddDays(-7), now);
        return m7d.EstimatedRemainingHours(currentBattery);
    }

    private void Load(DateTime now)
    {
        loaded = true;
        var cutoff = now.AddDays(-7);
        var previous = new List<HistorySample>();
        try
        {
            for (DateTime day = cutoff.Date; day <= now.Date; day = day.AddDays(1))
            {
                string path = DayFile(day);
                if (!File.Exists(path)) continue;
                foreach (string line in File.ReadLines(path))
                {
                    var sample = HistorySample.Parse(line);
                    if (sample != null && sample.Time >= cutoff && sample.Time <= now) previous.Add(sample);
                }
            }
            foreach (var sample in previous.OrderBy(entry => entry.Time)) samples.Enqueue(sample);
        }
        catch (IOException) { CanSave = false; }
        catch (UnauthorizedAccessException) { CanSave = false; }
    }

    private void BeginSegment()
    {
        segment = Guid.NewGuid().ToString("N");
        last = null;
    }

    private string DayFile(DateTime day)
    {
        return Path.Combine(folder, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".tsv");
    }

    private static string Hash(string value)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
    }
}

internal sealed class HistoryMetrics
{
    internal readonly SortedDictionary<int, double> Rates = new SortedDictionary<int, double>();
    internal readonly SortedDictionary<int, double> Dpis = new SortedDictionary<int, double>();
    internal int ObservedDrop;
    internal bool HasDischargePairs;
    internal double DischargeSeconds;
    internal double TotalSeconds;
    internal double TotalDpiSeconds;
    internal int? LatestBattery;
    internal int? MinBattery;
    internal int? MaxBattery;
    internal bool? LatestCharging;

    internal double? EstimatedRemainingHours(int? currentBattery = null)
    {
        int? level = currentBattery ?? LatestBattery;
        if (!level.HasValue) return null;
        if (level.Value <= 0) return 0.0;
        bool enoughEvidence = (ObservedDrop >= 2 && DischargeSeconds >= 600.0) ||
                              (ObservedDrop >= 1 && DischargeSeconds >= 1800.0);
        if (!enoughEvidence) return null;
        double dropPerHour = ObservedDrop / (DischargeSeconds / 3600.0);
        if (dropPerHour <= 0.01) return null;
        return Math.Min(999.0, level.Value / dropPerHour);
    }

    internal static string FormatRemainingHours(double? hours, bool? charging, bool shortForm)
    {
        if (charging == true) return "充电中";
        if (!hours.HasValue) return shortForm ? "积累中" : null;
        double h = hours.Value;
        if (h < 1.0) return Math.Max(1, (int)Math.Round(h * 60.0)).ToString(CultureInfo.InvariantCulture) + " 分钟";
        if (h >= 240.0) return "约 " + (h / 24.0).ToString("0.0", CultureInfo.InvariantCulture) + " 天";
        return "约 " + Math.Round(h).ToString("0", CultureInfo.InvariantCulture) + " 小时";
    }

    internal int? DominantRate
    {
        get
        {
            int? best = null;
            double bestSec = 0;
            foreach (var pair in Rates)
            {
                if (pair.Value > bestSec) { bestSec = pair.Value; best = pair.Key; }
            }
            return best;
        }
    }

    internal int? DominantDpi
    {
        get
        {
            int? best = null;
            double bestSec = 0;
            foreach (var pair in Dpis)
            {
                if (pair.Value > bestSec) { bestSec = pair.Value; best = pair.Key; }
            }
            return best;
        }
    }

    internal static HistoryMetrics Calculate(IList<HistorySample> points, DateTime start, DateTime end)
    {
        var metrics = new HistoryMetrics();
        for (int i = 0; i < points.Count; i++)
        {
            var sample = points[i];
            if (sample.Time >= start && sample.Time <= end && sample.Battery.HasValue)
            {
                int val = sample.Battery.Value;
                metrics.LatestBattery = val;
                metrics.LatestCharging = sample.Charging;
                if (!metrics.MinBattery.HasValue || val < metrics.MinBattery.Value) metrics.MinBattery = val;
                if (!metrics.MaxBattery.HasValue || val > metrics.MaxBattery.Value) metrics.MaxBattery = val;
            }
        }

        for (int i = 1; i < points.Count; i++)
        {
            var before = points[i - 1];
            var after = points[i];
            if (!after.Follows(before)) continue;
            var a = before.Time < start ? start : before.Time;
            var b = after.Time > end ? end : after.Time;
            if (b <= a) continue;
            double seconds = (b - a).TotalSeconds;

            if (before.Rate.HasValue && after.Rate.HasValue)
            {
                int rate = before.Rate.Value;
                double total;
                metrics.Rates.TryGetValue(rate, out total);
                metrics.Rates[rate] = total + seconds;
                metrics.TotalSeconds += seconds;
            }

            if (before.Dpi.HasValue && after.Dpi.HasValue)
            {
                int dpi = before.Dpi.Value;
                double total;
                metrics.Dpis.TryGetValue(dpi, out total);
                metrics.Dpis[dpi] = total + seconds;
                metrics.TotalDpiSeconds += seconds;
            }

            if (before.Time >= start && after.Time <= end && before.Battery.HasValue && after.Battery.HasValue &&
                before.Charging != true && after.Charging != true)
            {
                metrics.HasDischargePairs = true;
                metrics.DischargeSeconds += (after.Time - before.Time).TotalSeconds;
                metrics.ObservedDrop += Math.Max(0, before.Battery.Value - after.Battery.Value);
            }
        }
        return metrics;
    }
}
