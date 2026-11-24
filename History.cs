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
    internal readonly int? Battery, Rate;
    internal readonly bool? Charging;
    internal readonly int Interval;

    internal HistorySample(DateTime time, string device, string segment, int? battery, int? rate, bool? charging, int interval)
    {
        Time = time;
        Device = device;
        Segment = segment;
        Battery = battery;
        Rate = rate;
        Charging = charging;
        Interval = interval;
    }

    internal bool HasReading { get { return Battery.HasValue || Rate.HasValue; } }

    internal bool Follows(HistorySample before)
    {
        var seconds = (Time - before.Time).TotalSeconds;
        return Device == before.Device && Segment == before.Segment &&
            seconds > 0 && seconds <= Math.Max(2 * before.Interval + 5, 15);
    }

    internal string Serialize()
    {
        var culture = CultureInfo.InvariantCulture;
        return string.Join("\t", Time.Ticks.ToString(culture), Device, Segment,
            Battery.HasValue ? Battery.Value.ToString(culture) : "",
            Rate.HasValue ? Rate.Value.ToString(culture) : "",
            Charging.HasValue ? (Charging.Value ? "1" : "0") : "", Interval.ToString(culture));
    }

    internal static HistorySample Parse(string line)
    {
        var fields = line.Split('\t');
        long time;
        int interval, level, rate;
        if (fields.Length != 7 || !long.TryParse(fields[0], out time) ||
            time < DateTime.MinValue.Ticks || time > DateTime.MaxValue.Ticks ||
            fields[1].Length != 64 || fields[2].Length != 32 ||
            !int.TryParse(fields[6], out interval) || AppPreferences.RefreshSeconds(interval) != interval)
            return null;
        int? battery = int.TryParse(fields[3], out level) && level >= 0 && level <= 100 ? (int?)level : null;
        int? hz = int.TryParse(fields[4], out rate) && rate >= 125 && rate <= 8000 ? (int?)rate : null;
        bool? charging = fields[5] == "1" ? (bool?)true : fields[5] == "0" ? (bool?)false : null;
        return new HistorySample(new DateTime(time, DateTimeKind.Utc), fields[1], fields[2], battery, hz, charging, interval);
    }
}

internal sealed class HistoryStore
{
    private readonly Queue<HistorySample> samples = new Queue<HistorySample>();
    private readonly string folder;
    private string source, activeKey, segment = Guid.NewGuid().ToString("N");
    private HistorySample last;
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
            BeginSegment();
        }
        if (!string.IsNullOrEmpty(identity))
        {
            string key = Hash(identity);
            if (key != activeKey)
            {
                activeKey = key;
                BeginSegment();
                if (source == "usb") RememberUsbKey(key);
            }
        }
        if (activeKey == null) return;
        if (!loaded) Load(now);

        var sample = new HistorySample(now, activeKey, segment, status.Battery, status.Rate, status.Charging, interval);
        if (!sample.HasReading && (last == null || !last.HasReading)) return;
        samples.Enqueue(sample);
        last = sample;
        while (samples.Count != 0 && samples.Peek().Time < now.AddDays(-7)) samples.Dequeue();

        try
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(DayFile(now), sample.Serialize() + Environment.NewLine, Encoding.UTF8);
            CanSave = true;
        }
        catch (IOException) { CanSave = false; }
        catch (UnauthorizedAccessException) { CanSave = false; }
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
        // Keep the preceding interval if it crosses the start of the selected range.
        return samples.Where(sample => sample.Device == activeKey && sample.Time >= from.AddMinutes(-11))
                      .OrderBy(sample => sample.Time).ToArray();
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
    internal int ObservedDrop;
    internal bool HasDischargePairs;
    internal double TotalSeconds;

    internal static HistoryMetrics Calculate(IList<HistorySample> points, DateTime start, DateTime end)
    {
        var metrics = new HistoryMetrics();
        for (int i = 1; i < points.Count; i++)
        {
            var before = points[i - 1];
            var after = points[i];
            if (!after.Follows(before)) continue;
            var a = before.Time < start ? start : before.Time;
            var b = after.Time > end ? end : after.Time;
            if (b <= a) continue;
            if (before.Rate.HasValue && after.Rate.HasValue)
            {
                int rate = before.Rate.Value;
                double seconds = (b - a).TotalSeconds;
                double total;
                metrics.Rates.TryGetValue(rate, out total);
                metrics.Rates[rate] = total + seconds;
                metrics.TotalSeconds += seconds;
            }
            if (before.Time >= start && after.Time <= end && before.Battery.HasValue && after.Battery.HasValue &&
                before.Charging == false && after.Charging == false)
            {
                metrics.HasDischargePairs = true;
                metrics.ObservedDrop += Math.Max(0, before.Battery.Value - after.Battery.Value);
            }
        }
        return metrics;
    }
}
