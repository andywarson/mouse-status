using System;
using System.IO;
using System.Linq;
using System.Text;

internal static class ProtocolTests
{
    private static void Check(bool ok, string reason)
    {
        if (!ok) throw new Exception("Test failed: " + reason);
    }

    private static int Main()
    {
        // 1. DeviceInfo (0x0900) request & parsing
        var query = MouseProtocol.Request(0x0900);
        Check(query.Length == 64 && query.Take(9).SequenceEqual(new byte[] { 77, 1, 1, 0, 0, 9, 0, 0, 8 }), "Battery query frame");

        var offline = new byte[64];
        Array.Copy(new byte[] { 0x4D, 1, 1, 1, 0, 9, 0, 0, 0xFF }, offline, 9);
        Check(MouseProtocol.IsOfflineReply(offline, 0x0900), "Detect sleeping mouse 0xFF reply immediately");
        Check(!MouseProtocol.IsOfflineReply(offline, 0x0002), "Command mismatch does not trigger offline shortcut");

        var battery = new byte[64];
        Array.Copy(new byte[] { 77, 1, 1, 14, 0, 9, 0, 0, 0x37, 0x38, 0x33, 0x40, 4 }, battery, 13);
        battery[20] = 0;
        battery[22] = MouseProtocol.Checksum(battery, 22);
        Check(MouseProtocol.Battery(battery) == 0, "Zero percent battery is valid");
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
        Check(MouseProtocol.Battery(battery) == null, "Reject invalid >100% battery");

        // 2. CurrentProfile (0x0002) & DpiTable (0x0003)
        var profile = new byte[64];
        Array.Copy(new byte[] { 77, 1, 1, 9, 2, 0, 0, 0, 1, 0x61, 0x51 }, profile, 11);
        profile[17] = MouseProtocol.Checksum(profile, 17);
        Check(MouseProtocol.Rate(profile) == 4000, "Extract wireless polling rate");
        Check(MouseProtocol.ProfileIndex(profile) == 1, "Extract current profile index");
        profile[17] ^= 1;
        Check(MouseProtocol.Rate(profile) == null, "Reject corrupt profile checksum");

        var dpiReq = MouseProtocol.DpiRequest(1, 0);
        Check(dpiReq[0] == 0x4D && dpiReq[3] == 2 && dpiReq[4] == 3 && dpiReq[5] == 0 && dpiReq[8] == 1 && dpiReq[9] == 0, "DPI table request header");
        Check(dpiReq[10] == MouseProtocol.Checksum(dpiReq, 10), "DPI table request checksum");

        var dpiFrame = new byte[64];
        // Header: 0x4D, 1, 1, len=17, cmd=0x0003, 0, 0
        // Payload: profile=1, dir=0, count=4, active=1 (2nd stage), dual=1
        Array.Copy(new byte[] { 0x4D, 1, 1, 17, 3, 0, 0, 0, 1, 0, 4, 1, 1 }, dpiFrame, 13);
        int[] stages = { 800, 1600, 3200, 6400, 12800, 25600 };
        for (int i = 0; i < 6; i++)
        {
            dpiFrame[13 + i * 2] = (byte)(stages[i] & 0xFF);
            dpiFrame[14 + i * 2] = (byte)(stages[i] >> 8);
        }
        dpiFrame[25] = MouseProtocol.Checksum(dpiFrame, 25);
        var parsedDpi = MouseProtocol.Dpi(dpiFrame, 1, 0);
        Check(parsedDpi != null && parsedDpi.Value == 1600 && parsedDpi.Stage == 2 && parsedDpi.StageCount == 4 && parsedDpi.DualAxis,
            "Parse active DPI value, 1-based stage, total count, and dual-axis flag");
        Check(MouseProtocol.Dpi(dpiFrame, 0, 0) == null, "Reject mismatched profile index in DPI reply");

        var statusDual = new MouseStatus { Dpi = 1600, DpiY = 1200 };
        Check(statusDual.DpiDisplay == "1600×1200", "Format asymmetric X/Y DPI");
        statusDual.DpiY = 1600;
        Check(statusDual.DpiDisplay == "1600", "Format symmetric DPI");

        // 3. Preferences & 8 Built-in Themes
        Check(AppPreferences.RefreshSeconds(null) == 30, "Default interval");
        Check(AppPreferences.RefreshSeconds(5) == 5 && AppPreferences.RefreshSeconds(300) == 300, "Interval bounds");
        Check(AppPreferences.RefreshSeconds(0) == 30, "Invalid interval rejected");
        Check(DisplayPreferences.NormalizeOpacity(85) == 85 && DisplayPreferences.NormalizeOpacity(20) == 100, "Opacity bounds");

        var legacyTray = DisplayPreferences.FromSwitches(null, null, 1);
        Check(!legacyTray.Floating && legacyTray.Taskbar, "Migrate legacy taskbar mode");
        var both = DisplayPreferences.FromSwitches(1, 1, 1);
        Check(both.Floating && both.Taskbar, "Both displays can coexist");
        var neither = DisplayPreferences.FromSwitches(0, 0, 0);
        Check(!neither.Floating && !neither.Taskbar, "Both displays can be disabled");

        Check(ThemePalette.Names.Length == 8, "Eight built-in themes available");
        for (int t = 0; t < ThemePalette.Names.Length; t++)
        {
            var pal = ThemePalette.Create(t, null);
            Check(pal.Index == t, "Theme index preserved");
        }
        var customLightAccent = ThemePalette.Create(1, System.Drawing.Color.Yellow.ToArgb());
        Check(customLightAccent.OnAccent.R < 40, "Light accent selects dark foreground text");
        var customDarkAccent = ThemePalette.Create(2, System.Drawing.Color.MidnightBlue.ToArgb());
        Check(customDarkAccent.OnAccent.R > 240, "Dark accent selects white foreground text");

        string imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Guid.NewGuid() + ".png");
        try
        {
            using (var image = new System.Drawing.Bitmap(72, 48))
                image.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
            using (var thumbnail = DisplayPreferences.OpenPicture(DisplayPreferences.ImportPicture(imagePath)))
                Check(thumbnail.Width == 64 && thumbnail.Height == 64, "Pictures cropped to 64x64 square PNG");
        }
        finally { File.Delete(imagePath); }

        // 4. History metrics (Rates + DPIs), backward-compatible 7-col TSV, & old file pruning
        var begin = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
        var samples = new[]
        {
            new HistorySample(begin, "mouse", "run", 80, 1000, false, 30, 800),
            new HistorySample(begin.AddSeconds(30), "mouse", "run", 79, 2000, false, 30, 1600),
            new HistorySample(begin.AddSeconds(60), "mouse", "run", 79, 2000, false, 30, 1600),
            new HistorySample(begin.AddMinutes(30), "mouse", "run", 75, 1000, false, 30, 800),
            new HistorySample(begin.AddMinutes(30).AddSeconds(30), "mouse", "other-run", 74, 1000, false, 30, 800)
        };
        var totals = HistoryMetrics.Calculate(samples, begin.AddSeconds(15), begin.AddHours(1));
        Check(totals.Rates[1000] == 15 && totals.Rates[2000] == 30, "Rate intervals clipped to range and segment");
        Check(totals.Dpis[800] == 15 && totals.Dpis[1600] == 30, "DPI intervals clipped to range and segment");
        Check(totals.DominantDpi == 1600 && totals.DominantRate == 2000, "Dominant DPI and rate identified");
        Check(totals.ObservedDrop == 0, "Omit discharge from interval starting before range");

        // Verify real OS build detection via RtlGetVersion (manifest-independent) & BLE battery drop (Charging == null)
        Check(UiKit.WindowsBuild >= 10240, "RtlGetVersion reports real Windows 10/11 build instead of 6.2.9200");
        var bleSamples = new[]
        {
            new HistorySample(begin, "ble", "s1", 90, null, null, 30, null),
            new HistorySample(begin.AddSeconds(30), "ble", "s1", 87, null, null, 30, null),
            new HistorySample(begin.AddSeconds(60), "ble", "s1", 91, null, null, 30, null)
        };
        var bleMetrics = HistoryMetrics.Calculate(bleSamples, begin, begin.AddHours(1));
        Check(bleMetrics.HasDischargePairs && bleMetrics.ObservedDrop == 3, "BLE samples with unknown charging state still count battery drop");
        var chargeSample = new HistorySample(begin.AddSeconds(60), "mouse", "run", 85, 2000, true, 30, 1600);
        var chargePeriod = HistoryMetrics.Calculate(new[] { samples[0], samples[1], chargeSample }, begin, begin.AddHours(1));
        Check(chargePeriod.ObservedDrop == 1, "Known charging intervals remain excluded from battery drop");

        // 5. Remaining runtime estimator
        var shortRun = HistoryMetrics.Calculate(new[]
        {
            new HistorySample(begin, "m", "s", 80, 1000, false, 60, 1600),
            new HistorySample(begin.AddMinutes(2), "m", "s", 78, 1000, false, 60, 1600)
        }, begin, begin.AddHours(1));
        Check(shortRun.EstimatedRemainingHours() == null, "Do not extrapolate remaining hours from <10 min sample");

        var twoHourRun = new System.Collections.Generic.List<HistorySample>();
        for (int m = 0; m <= 120; m += 5)
        {
            int bat = 84 - (m / 30); // drops 4% over 2 hours => 2% per hour; final battery = 80%
            twoHourRun.Add(new HistorySample(begin.AddMinutes(m), "m", "s", bat, 2000, false, 300, 1600));
        }
        var longMetrics = HistoryMetrics.Calculate(twoHourRun, begin, begin.AddHours(3));
        double? estHours = longMetrics.EstimatedRemainingHours();
        Check(estHours.HasValue && Math.Abs(estHours.Value - 40.0) < 0.1, "80% at 2%/h estimates ~40 hours remaining");

        // 6. Auto system theme pairing & numeric tray icons
        var autoPrefs = new DisplayPreferences { FollowSystemTheme = true };
        autoPrefs.RememberThemePolarity(3); // Celadon Light
        autoPrefs.RememberThemePolarity(6); // Cyber Neon Dark
        Check(autoPrefs.EffectiveTheme(false) == 3 && autoPrefs.EffectiveTheme(true) == 6,
            "FollowSystemTheme switches between remembered light and dark themes");

        var palCheck = ThemePalette.Create(5, null);
        foreach (int? lvl in new int?[] { null, 0, 12, 84, 100 })
        {
            using (var ico1 = TrayIconRenderer.Create(lvl, false, palCheck, true, 20))
            using (var ico2 = TrayIconRenderer.Create(lvl, true, palCheck, true, 20))
                Check(ico1 != null && ico2 != null, "Numeric tray icon renders across all battery states");
        }

        // 7. Smart battery alert state machine & OSD gear change detection
        var tracker = new BatteryAlertTracker();
        Check(tracker.EvaluateBattery("dev1", "A7", new MouseStatus { Battery = 20, Charging = false }, 20, true) != null,
            "First drop to threshold triggers low-battery notice");
        Check(tracker.EvaluateBattery("dev1", "A7", new MouseStatus { Battery = 18, Charging = false }, 20, true) == null,
            "Subsequent low readings do not spam duplicate alerts");
        Check(tracker.EvaluateBattery("dev1", "A7", new MouseStatus { Battery = 9, Charging = false }, 20, true) != null,
            "Dropping to <=10% escalates one critical warning");
        Check(tracker.EvaluateBattery("dev1", "A7", new MouseStatus { Battery = 8, Charging = false }, 20, true) == null,
            "Critical warning does not repeat");
        Check(tracker.EvaluateBattery("dev1", "A7", new MouseStatus { Battery = 100, Charging = true }, 20, true) != null,
            "Reaching 100% while charging triggers full-charge notice");
        Check(tracker.EvaluateBattery("dev1", "A7", new MouseStatus { Battery = 100, Charging = true }, 20, true) == null,
            "Full-charge notice fires once per charge cycle");

        string head, sub;
        Check(!tracker.EvaluateGearChange("dev1", new MouseStatus { Dpi = 800, DpiStage = 1, DpiStageCount = 6, Rate = 1000 }, out head, out sub),
            "Initial baseline reading does not pop OSD");
        Check(!tracker.EvaluateGearChange("dev1", new MouseStatus { Dpi = 800, DpiStage = 1, DpiStageCount = 6, Rate = 1000 }, out head, out sub),
            "Unchanged DPI/rate does not pop OSD");
        Check(tracker.EvaluateGearChange("dev1", new MouseStatus { Dpi = 1600, DpiStage = 2, DpiStageCount = 6, Rate = 1000 }, out head, out sub) &&
              head.Contains("1600") && sub.Contains("2/6"),
            "DPI button stage change triggers OSD summary");

        string dev64 = new string('A', 64);
        string seg32 = new string('b', 32);
        string legacyLine = string.Join("\t", begin.Ticks.ToString(), dev64, seg32, "88", "2000", "0", "30");
        var parsedLegacy = HistorySample.Parse(legacyLine);
        Check(parsedLegacy != null && parsedLegacy.Battery == 88 && parsedLegacy.Rate == 2000 && parsedLegacy.Dpi == null,
            "Parse legacy 7-column TSV line without DPI");

        var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history-check-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(folder);
            string expiredFile = Path.Combine(folder, "2026-10-01.tsv");
            File.WriteAllText(expiredFile, legacyLine + Environment.NewLine, Encoding.UTF8);

            var store = new HistoryStore(folder);
            var reading = new MouseStatus { Battery = 80, Rate = 2000, Dpi = 1600, Charging = false };
            store.Record("usb", "device-id", reading, 30, begin);
            store.Record("usb", "device-id", reading, 30, begin.AddSeconds(30));

            Check(!File.Exists(expiredFile), "Expired >14d TSV file automatically pruned");

            var reopened = new HistoryStore(folder);
            reopened.Record("usb", null, new MouseStatus(), 30, begin.AddSeconds(60));
            var snap = reopened.Snapshot(begin);
            Check(snap.Count == 2 && snap[0].Dpi == 1600, "History with DPI persists after restart while sleeping");

            reopened.Record("ble", "different-device", reading, 30, begin.AddSeconds(75));
            Check(reopened.Snapshot(begin).Count == 1, "Bluetooth device maintains isolated history");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
                Directory.Delete(folder);
            }
        }

        Console.WriteLine("All protocol, UI theme, and history checks passed.");
        return 0;
    }
}
