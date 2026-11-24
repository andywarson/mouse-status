using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

internal sealed class MouseStatus
{
    internal int? Battery;
    internal int? Rate;
    internal bool? Charging;
    internal bool ReceiverFound;
    internal bool IsBluetooth;
    internal bool? BluetoothConnected;
    internal string DeviceName;
    internal string DeviceKey;
}

internal static class Receiver
{
    private const uint ReadWrite = 0xC0000000;
    private const uint OverlappedFlag = 0x40000000;

    internal static MouseStatus ReadStatus(Action<string> trace)
    {
        var status = new MouseStatus();
        foreach (string path in Paths())
        {
            int receiver = new[] {0x1018, 0x1014, 0x1016}.FirstOrDefault(pid =>
                path.IndexOf("vid_3837&pid_" + pid.ToString("x4") + "&mi_", StringComparison.OrdinalIgnoreCase) >= 0);
            if (receiver == 0) continue;
            status.ReceiverFound = true;
            using (SafeFileHandle handle = CreateFile(path, ReadWrite, 3, IntPtr.Zero, 3, OverlappedFlag, IntPtr.Zero))
            {
                if (handle.IsInvalid) { Trace(trace, "Open: " + Marshal.GetLastWin32Error()); continue; }
                if (!IsControlInterface(handle, receiver, trace)) continue;
                var product = new StringBuilder(127);
                if (HidD_GetProductString(handle, product, 254) && !string.IsNullOrWhiteSpace(product.ToString()))
                    status.DeviceName = product.ToString().Trim();
                try
                {
                    var battery = Query(handle, MouseProtocol.DeviceInfo, 14, trace);
                    status.Battery = MouseProtocol.Battery(battery, receiver);
                    if (!status.Battery.HasValue) continue;
                    status.DeviceKey = "usb:" + path.ToUpperInvariant() + ":" + battery[10].ToString("X2");
                    status.Charging = MouseProtocol.Charging(battery);
                    status.Rate = MouseProtocol.Rate(Query(handle, MouseProtocol.CurrentProfile, 9, trace));
                    if (receiver == 0x1016 && status.Rate > 1000) status.Rate = null;
                    return status;
                }
                catch (IOException e) { Trace(trace, e.Message); }
                catch (Win32Exception e) { Trace(trace, e.Message); }
            }
        }
        return status;
    }

    private static string[] Paths()
    {
        Guid guid;
        HidD_GetHidGuid(out guid);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint size;
            if (CM_Get_Device_Interface_List_Size(out size, ref guid, null, 0) != 0)
                return new string[0];
            var list = new char[size];
            uint result = CM_Get_Device_Interface_List(ref guid, null, list, size, 0);
            if (result == 0) return new string(list).Split(new[] {'\0'}, StringSplitOptions.RemoveEmptyEntries);
            if (result != 0x1A) break;
        }
        return new string[0];
    }

    private static bool IsControlInterface(SafeFileHandle handle, int receiver, Action<string> trace)
    {
        IntPtr data;
        if (!HidD_GetPreparsedData(handle, out data)) return false;
        try
        {
            var caps = new ushort[32];
            if (HidP_GetCaps(data, caps) != 0x110000) return false;
            Trace(trace, string.Format("Usage {0:X4}:{1:X4}; input {2}; output {3}", caps[1], caps[0], caps[2], caps[3]));
            if (caps[2] != 64 || caps[3] != 64 ||
                !((caps[1] == 0xFF01 && caps[0] == 1) ||
                  (receiver == 0x1018 && caps[1] == 0xFF0B && caps[0] == 0xC4))) return false;
            return HidP_InitializeReportForID(0, 0x4D, data, new byte[64], 64) == 0x110000 &&
                   HidP_InitializeReportForID(1, 0x4D, data, new byte[64], 64) == 0x110000;
        }
        finally { HidD_FreePreparsedData(data); }
    }

    private static byte[] Query(SafeFileHandle handle, ushort command, int minimum, Action<string> trace)
    {
        HidD_FlushQueue(handle);
        byte[] request = MouseProtocol.Request(command);
        if (Transfer(handle, request, true, 1000) != request.Length)
            throw new IOException("Short HID write.");
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 2000)
        {
            var reply = new byte[64];
            int remaining = Math.Max(1, 2000 - (int)clock.ElapsedMilliseconds);
            int size = Transfer(handle, reply, false, remaining);
            Trace(trace, string.Format("{0:X4} ({1}): {2}", command, size, BitConverter.ToString(reply)));
            if (size == 64 && MouseProtocol.Matches(reply, command, minimum)) return reply;
        }
        return null;
    }

    private static int Transfer(SafeFileHandle handle, byte[] data, bool write, int timeout)
    {
        using (var completion = new EventWaitHandle(false, EventResetMode.ManualReset))
        {
            var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
            IntPtr operation = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
            try
            {
                var overlapped = new NativeOverlapped();
                overlapped.EventHandle = completion.SafeWaitHandle.DangerousGetHandle();
                Marshal.StructureToPtr(overlapped, operation, false);
                int transferred;
                bool finished = write
                    ? WriteFile(handle, pinned.AddrOfPinnedObject(), data.Length, out transferred, operation)
                    : ReadFile(handle, pinned.AddrOfPinnedObject(), data.Length, out transferred, operation);
                if (finished) return transferred;
                int error = Marshal.GetLastWin32Error();
                if (error != 997) throw new Win32Exception(error);
                if (!completion.WaitOne(timeout))
                {
                    CancelIoEx(handle, operation);
                    GetOverlappedResult(handle, operation, out transferred, true);
                    return 0;
                }
                if (!GetOverlappedResult(handle, operation, out transferred, false)) throw new Win32Exception();
                return transferred;
            }
            finally { Marshal.FreeHGlobal(operation); pinned.Free(); }
        }
    }

    private static void Trace(Action<string> trace, string message) { if (trace != null) trace(message); }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetProductString(SafeFileHandle handle, StringBuilder product, int size);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FlushQueue(SafeFileHandle handle);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, [Out] ushort[] caps);
    [DllImport("hid.dll")] private static extern int HidP_InitializeReportForID(int type, byte id, IntPtr data, byte[] report, uint length);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_Interface_List_SizeW")]
    private static extern uint CM_Get_Device_Interface_List_Size(out uint length, ref Guid guid, string device, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_Interface_ListW")]
    private static extern uint CM_Get_Device_Interface_List(ref Guid guid, string device, [Out] char[] list, uint length, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle handle, IntPtr data, int size, out int bytes, IntPtr operation);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle handle, IntPtr data, int size, out int bytes, IntPtr operation);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle handle, IntPtr operation, out int bytes, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr operation);
}
