using System;
using System.Collections.Generic;
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
        string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        var devices = await Complete(DeviceInformation.FindAllAsync(selector, new[] { ConnectionProperty }), token);
        var result = new List<BluetoothMouse>();
        foreach (var entry in devices)
        {
            object state;
            entry.Properties.TryGetValue(ConnectionProperty, out state);
            string name = string.IsNullOrWhiteSpace(entry.Name) ? "蓝牙设备" : entry.Name;
            result.Add(new BluetoothMouse(entry.Id, name, state is bool && (bool)state));
        }
        result.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));
        return result;
    }

    internal static async Task<MouseStatus> Read(string id)
    {
        var status = new MouseStatus { IsBluetooth = true };
        using (var timeout = new CancellationTokenSource(4500))
        {
            try
            {
                var deviceInfo = await Complete(DeviceInformation.CreateFromIdAsync(id, new[] { ConnectionProperty }), timeout.Token);
                if (deviceInfo == null || !deviceInfo.Pairing.IsPaired) return status;
                status.DeviceName = string.IsNullOrWhiteSpace(deviceInfo.Name) ? null : deviceInfo.Name.Trim();
                object connected;
                deviceInfo.Properties.TryGetValue(ConnectionProperty, out connected);
                status.BluetoothConnected = connected as bool?;
                if (status.BluetoothConnected == false) return status;

                using (var device = await Complete(BluetoothLEDevice.FromIdAsync(id), timeout.Token))
                {
                    if (device == null) return status;
                    status.BluetoothConnected = device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                    if (status.BluetoothConnected != true) return status;
                    var services = await Complete(device.GetGattServicesForUuidAsync(BatteryService, BluetoothCacheMode.Cached), timeout.Token);
                    foreach (var service in services.Services)
                    {
                        using (service)
                        {
                            if (services.Status != GattCommunicationStatus.Success || status.Battery.HasValue) continue;
                            var characteristics = await Complete(service.GetCharacteristicsForUuidAsync(BatteryLevel, BluetoothCacheMode.Cached), timeout.Token);
                            if (characteristics.Status != GattCommunicationStatus.Success) continue;
                            foreach (var characteristic in characteristics.Characteristics)
                            {
                                var reading = await Complete(characteristic.ReadValueAsync(BluetoothCacheMode.Uncached), timeout.Token);
                                if (reading.Status != GattCommunicationStatus.Success || reading.Value.Length == 0) continue;
                                using (var reader = DataReader.FromBuffer(reading.Value))
                                {
                                    byte level = reader.ReadByte();
                                    if (level <= 100) { status.Battery = level; break; }
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }
        return status;
    }

    private static async Task<T> Complete<T>(IAsyncOperation<T> operation, CancellationToken token)
    {
        try
        {
            while (operation.Status == AsyncStatus.Started)
                await Task.Delay(50, token);
            return operation.GetResults();
        }
        catch (OperationCanceledException)
        {
            operation.Cancel();
            throw;
        }
        finally
        {
            if (operation.Status != AsyncStatus.Started) operation.Close();
        }
    }
}
