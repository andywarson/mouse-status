using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;

internal sealed class BluetoothMouse
{
    internal readonly string Id;
    internal readonly string Name;
    internal readonly bool Connected;

    internal BluetoothMouse(string id, string name, bool connected)
    {
        Id = id;
        Name = name;
        Connected = connected;
    }

    public override string ToString() { return Name; }
}

internal static class BluetoothStatus
{
    private const string ConnectionProperty = "System.Devices.Aep.IsConnected";
    private static readonly Guid BatteryService = new Guid("0000180F-0000-1000-8000-00805F9B34FB");
    private static readonly Guid BatteryLevel = new Guid("00002A19-0000-1000-8000-00805F9B34FB");

    internal static async Task<List<BluetoothMouse>> PairedDevices(CancellationToken token)
    {
        var result = new List<BluetoothMouse>();
        try
        {
            string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await Complete(DeviceInformation.FindAllAsync(selector, new[] { ConnectionProperty }), token).ConfigureAwait(false);
            if (devices == null) return result;
            foreach (var entry in devices)
            {
                object state;
                entry.Properties.TryGetValue(ConnectionProperty, out state);
                string name = string.IsNullOrWhiteSpace(entry.Name) ? "蓝牙设备" : entry.Name.Trim();
                result.Add(new BluetoothMouse(entry.Id, name, state is bool && (bool)state));
            }
            result.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (COMException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (Exception ex) { if (ex.GetType() != typeof(Exception)) throw; }
        return result;
    }

    internal static async Task<MouseStatus> Read(string id)
    {
        var status = new MouseStatus { IsBluetooth = true };
        if (string.IsNullOrWhiteSpace(id)) return status;
        using (var timeout = new CancellationTokenSource(4500))
        {
            try
            {
                var deviceInfo = await Complete(DeviceInformation.CreateFromIdAsync(id, new[] { ConnectionProperty }), timeout.Token).ConfigureAwait(false);
                if (deviceInfo == null || deviceInfo.Pairing == null || !deviceInfo.Pairing.IsPaired) return status;
                status.DeviceName = string.IsNullOrWhiteSpace(deviceInfo.Name) ? null : deviceInfo.Name.Trim();
                object connected;
                deviceInfo.Properties.TryGetValue(ConnectionProperty, out connected);
                status.BluetoothConnected = connected as bool?;
                if (status.BluetoothConnected == false) return status;

                using (var device = await Complete(BluetoothLEDevice.FromIdAsync(id), timeout.Token).ConfigureAwait(false))
                {
                    if (device == null) return status;
                    status.BluetoothConnected = device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                    if (status.BluetoothConnected != true) return status;

                    var services = await Complete(device.GetGattServicesForUuidAsync(BatteryService, BluetoothCacheMode.Cached), timeout.Token).ConfigureAwait(false);
                    if (services == null || services.Services == null) return status;
                    try
                    {
                        if (services.Status != GattCommunicationStatus.Success) return status;
                        foreach (var service in services.Services)
                        {
                            if (status.Battery.HasValue) break;
                            var characteristics = await Complete(service.GetCharacteristicsForUuidAsync(BatteryLevel, BluetoothCacheMode.Cached), timeout.Token).ConfigureAwait(false);
                            if (characteristics == null || characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics == null)
                                continue;
                            foreach (var characteristic in characteristics.Characteristics)
                            {
                                var reading = await Complete(characteristic.ReadValueAsync(BluetoothCacheMode.Uncached), timeout.Token).ConfigureAwait(false);
                                if (reading == null || reading.Status != GattCommunicationStatus.Success || reading.Value == null || reading.Value.Length == 0)
                                    continue;
                                using (var reader = DataReader.FromBuffer(reading.Value))
                                {
                                    byte level = reader.ReadByte();
                                    if (level <= 100)
                                    {
                                        status.Battery = level;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        foreach (var service in services.Services)
                        {
                            try { if (service != null) service.Dispose(); } catch { }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (COMException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Exception ex) { if (ex.GetType() != typeof(Exception)) throw; }
        }
        return status;
    }

    private static async Task<T> Complete<T>(IAsyncOperation<T> operation, CancellationToken token)
    {
        if (operation == null) return default(T);
        var tcs = new TaskCompletionSource<T>();
        using (token.Register(() =>
        {
            try { operation.Cancel(); } catch { }
            tcs.TrySetCanceled();
        }))
        {
            // WinRT guarantees put_Completed fires even if the operation finished before assignment.
            operation.Completed = (info, asyncStatus) =>
            {
                try
                {
                    if (asyncStatus == AsyncStatus.Completed)
                        tcs.TrySetResult(info.GetResults());
                    else if (asyncStatus == AsyncStatus.Canceled)
                        tcs.TrySetCanceled();
                    else
                        tcs.TrySetException(info.ErrorCode ?? new COMException("WinRT operation failed."));
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
                finally
                {
                    try { info.Close(); } catch { }
                }
            };
            return await tcs.Task.ConfigureAwait(false);
        }
    }
}
