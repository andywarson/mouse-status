using System;

internal sealed class DpiStageInfo
{
    internal readonly int Value;
    internal readonly int Stage;
    internal readonly int StageCount;
    internal readonly bool DualAxis;

    internal DpiStageInfo(int value, int stage, int stageCount, bool dualAxis)
    {
        Value = value;
        Stage = stage;
        StageCount = stageCount;
        DualAxis = dualAxis;
    }
}

internal static class MouseProtocol
{
    internal const ushort DeviceInfo = 0x0900;
    internal const ushort CurrentProfile = 0x0002;
    internal const ushort DpiTable = 0x0003;
    private static readonly int?[] Rates = { 125, null, 500, 1000, 2000, 4000, 8000 };

    internal static byte[] Request(ushort command)
    {
        if (command != DeviceInfo && command != CurrentProfile)
            throw new ArgumentOutOfRangeException("command");
        return BuildFrame(command);
    }

    internal static byte[] DpiRequest(int profileIndex, int direction)
    {
        if (profileIndex < 0 || profileIndex > 2)
            throw new ArgumentOutOfRangeException("profileIndex");
        if (direction < 0 || direction > 1)
            throw new ArgumentOutOfRangeException("direction");
        return BuildFrame(DpiTable, (byte)profileIndex, (byte)direction);
    }

    private static byte[] BuildFrame(ushort command, params byte[] payload)
    {
        var data = new byte[64];
        data[0] = 0x4D;
        data[1] = 1;
        data[2] = 1;
        data[3] = (byte)payload.Length;
        data[4] = (byte)command;
        data[5] = (byte)(command >> 8);
        if (payload.Length > 0)
            Array.Copy(payload, 0, data, 8, payload.Length);
        int checksumIndex = 8 + payload.Length;
        data[checksumIndex] = Checksum(data, checksumIndex);
        return data;
    }

    internal static byte Checksum(byte[] data, int end)
    {
        byte result = 0;
        for (int i = 2; i < end; i++) result ^= data[i];
        return result;
    }

    internal static bool IsOfflineReply(byte[] data, ushort command)
    {
        return data != null && data.Length == 64 &&
               data[0] == 0x4D && data[1] == 1 &&
               data[4] == (byte)command && data[5] == (byte)(command >> 8) &&
               data[8] == 0xFF;
    }

    internal static bool Matches(byte[] data, ushort command, int minimum)
    {
        if (data == null || data.Length != 64 || data[0] != 0x4D || data[1] != 1 ||
            data[4] != (byte)command || data[5] != (byte)(command >> 8) ||
            data[3] < minimum || data[3] > 55 || data[8] == 0xFF)
            return false;
        return (data[2] & 1) == 0 || data[8 + data[3]] == Checksum(data, 8 + data[3]);
    }

    internal static int? Battery(byte[] data)
    {
        return Battery(data, 0x1018);
    }

    internal static int? Battery(byte[] data, int receiver)
    {
        if (!Matches(data, DeviceInfo, 14) || data[8] != 0x37 || data[9] != 0x38 ||
            data[11] != 0x40 || !MatchesModel(data[10], receiver) || data[20] > 100)
            return null;
        return data[20];
    }

    private static bool MatchesModel(byte pid, int receiver)
    {
        if (receiver == 0x1016) return pid == 0x29 || pid == 0x2A;
        return (receiver == 0x1014 || receiver == 0x1018) &&
            Array.IndexOf(new byte[] { 0x26, 0x27, 0x28, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38 }, pid) >= 0;
    }

    internal static int? ProfileIndex(byte[] data)
    {
        if (!Matches(data, CurrentProfile, 9) || data[8] > 2) return null;
        return data[8];
    }

    internal static int? Rate(byte[] data)
    {
        if (!Matches(data, CurrentProfile, 9)) return null;
        int code = data[10] >> 4;
        return code < Rates.Length ? Rates[code] : null;
    }

    internal static DpiStageInfo Dpi(byte[] data, int expectedProfile, int expectedDirection)
    {
        if (!Matches(data, DpiTable, 17)) return null;
        if (data[8] != expectedProfile || data[9] != expectedDirection) return null;
        int count = data[10];
        int active = data[11];
        byte dual = data[12];
        if (count < 1 || count > 6 || active < 0 || active >= count || dual > 1) return null;
        for (int i = 0; i < count; i++)
        {
            int stageValue = data[13 + i * 2] | (data[14 + i * 2] << 8);
            if (stageValue <= 0 || stageValue > 50000) return null;
        }
        int currentDpi = data[13 + active * 2] | (data[14 + active * 2] << 8);
        return new DpiStageInfo(currentDpi, active + 1, count, dual == 1);
    }

    internal static bool? Charging(byte[] data)
    {
        if (!Matches(data, DeviceInfo, 14)) return null;
        if (data[19] == 0) return false;
        if (data[19] == 1) return true;
        return null;
    }
}
