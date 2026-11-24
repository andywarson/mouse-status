using System;
using System.Linq;
class ProtocolTests
{
    static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    static void Main()
    {
        var query = MouseProtocol.Request(0x0900);
        Check(query.Length == 64 && query.Take(9).SequenceEqual(new byte[] {77,1,1,0,0,9,0,0,8}), "Battery query");
        var battery = new byte[64];
        Array.Copy(new byte[] {77,1,1,14,0,9,0,0,0x37,0x38,0x33,0x40,4}, battery, 13);
        battery[20] = 0;
        battery[22] = MouseProtocol.Checksum(battery, 22);
        Check(MouseProtocol.Battery(battery) == 0, "Zero is valid");
        Check(MouseProtocol.Charging(battery) == false, "Report says not charging");
        battery[19] = 1;
        battery[22] = MouseProtocol.Checksum(battery, 22);
        Check(MouseProtocol.Charging(battery) == true, "Report says charging");
        battery[19] = 2;
        battery[22] = MouseProtocol.Checksum(battery, 22);
        Check(MouseProtocol.Charging(battery) == null, "Unknown charging state stays unknown");
        Check(MouseProtocol.Battery(battery, 0x1014) == 0, "Accept compatible 8K receiver");
        Check(MouseProtocol.Battery(battery, 0x1016) == null, "Reject 8K model on 1K receiver");
        battery[10] = 0x29;
        battery[22] = MouseProtocol.Checksum(battery, 22);
        Check(MouseProtocol.Battery(battery, 0x1016) == 0, "Accept compatible 1K receiver");
        battery[20] = 101;
        battery[22] = MouseProtocol.Checksum(battery, 22);
        Check(MouseProtocol.Battery(battery) == null, "Reject invalid battery");
        var profile = new byte[64];
        Array.Copy(new byte[] {77,1,1,9,2,0,0,0,0,0x61,0x51}, profile, 11);
        profile[17] = MouseProtocol.Checksum(profile, 17);
        Check(MouseProtocol.Rate(profile) == 4000, "Use wireless setting");
        profile[17] ^= 1;
        Check(MouseProtocol.Rate(profile) == null, "Reject corrupt frame");
        Check(AppPreferences.RefreshSeconds(null) == 30, "Default interval");
        Check(AppPreferences.RefreshSeconds(5) == 5, "Minimum interval");
        Check(AppPreferences.RefreshSeconds(300) == 300, "Maximum interval");
        Check(AppPreferences.RefreshSeconds(0) == 30, "Invalid interval cannot start a busy loop");
        var legacyTray = DisplayPreferences.FromSwitches(null, null, 1);
        Check(!legacyTray.Floating && legacyTray.Taskbar, "Migrate legacy taskbar mode");
        var both = DisplayPreferences.FromSwitches(1, 1, 1);
        Check(both.Floating && both.Taskbar, "Both displays can be enabled together");
        var neither = DisplayPreferences.FromSwitches(0, 0, 0);
        Check(!neither.Floating && !neither.Taskbar, "Both displays can be disabled");
        var custom = ThemePalette.Create(1, System.Drawing.Color.Yellow.ToArgb());
        Check(custom.Accent.ToArgb() == System.Drawing.Color.Yellow.ToArgb() &&
              custom.OnAccent.ToArgb() == System.Drawing.Color.Black.ToArgb(), "Light accent uses dark text");
        string imagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Guid.NewGuid() + ".png");
        try
        {
            using (var image = new System.Drawing.Bitmap(72, 48))
                image.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
            using (var thumbnail = DisplayPreferences.OpenPicture(DisplayPreferences.ImportPicture(imagePath)))
                Check(thumbnail.Width == 64 && thumbnail.Height == 64, "Pictures are copied as square thumbnails");
        }
        finally { System.IO.File.Delete(imagePath); }
        var begin = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
        var samples = new[] {
            new HistorySample(begin, "mouse", "run", 80, 1000, false, 30),
            new HistorySample(begin.AddSeconds(30), "mouse", "run", 79, 2000, false, 30),
            new HistorySample(begin.AddSeconds(60), "mouse", "run", 79, 2000, false, 30),
            new HistorySample(begin.AddMinutes(30), "mouse", "run", 75, 1000, false, 30),
            new HistorySample(begin.AddMinutes(30).AddSeconds(30), "mouse", "other-run", 74, 1000, false, 30)
        };
        var totals = HistoryMetrics.Calculate(samples, begin.AddSeconds(15), begin.AddHours(1));
        Check(totals.Rates[1000] == 15 && totals.Rates[2000] == 30, "Only count observed intervals inside range and session");
        Check(totals.ObservedDrop == 0, "Omit discharge from intervals starting before the range");
        var disconnected = new HistorySample(begin.AddSeconds(30), "mouse", "run", null, null, null, 30);
        Check(HistoryMetrics.Calculate(new[] { samples[0], disconnected, samples[2] }, begin, begin.AddHours(1))
            .TotalSeconds == 0, "A missing sample breaks both adjoining rate intervals");
        var charging = new HistorySample(begin.AddSeconds(60), "mouse", "run", 80, 2000, true, 30);
        var period = HistoryMetrics.Calculate(new[] { samples[0], samples[1], charging }, begin, begin.AddHours(1));
        Check(period.ObservedDrop == 1 && period.TotalSeconds == 60, "Charging is excluded only from battery drop");
        var folder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history-check-" + Guid.NewGuid());
        try
        {
            var store = new HistoryStore(folder);
            var reading = new MouseStatus { Battery = 80, Rate = 2000, Charging = false };
            store.Record("usb", "device-id", reading, 30, begin);
            store.Record("usb", "device-id", reading, 30, begin.AddSeconds(30));
            var reopened = new HistoryStore(folder);
            reopened.Record("usb", null, new MouseStatus(), 30, begin.AddSeconds(60));
            Check(reopened.Snapshot(begin).Count == 2, "History remains available after restart with receiver disconnected");
            reopened.Record("ble", "different-device", reading, 30, begin.AddSeconds(75));
            Check(reopened.Snapshot(begin).Count == 1, "Another device has separate history");
        }
        finally
        {
            if (System.IO.Directory.Exists(folder))
            {
                foreach (string file in System.IO.Directory.GetFiles(folder)) System.IO.File.Delete(file);
                System.IO.Directory.Delete(folder);
            }
        }
        Console.WriteLine("Protocol checks passed.");
    }
}
