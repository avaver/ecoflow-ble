using System.Text.Json;

namespace ecoflow_ble;

public class Worker : BackgroundService
{
    private const string ServiceUUID = "00000001-0000-1000-8000-00805f9b34fb";
    private const string CharacteristicUUID = "00000003-0000-1000-8000-00805f9b34fb";
    private readonly ILogger<Worker> _logger;
    private readonly Bluetooth _bt;
    private readonly MQTTClient _mqtt;
    private readonly IHostApplicationLifetime _app;
    private BtDevice? _device;
    private static DateTime _lastUpdatePd = DateTime.UtcNow;
    private static DateTime _lastUpdateBms0 = DateTime.UtcNow;
    private static DateTime _lastUpdateBms1 = DateTime.UtcNow;
    private static DateTime _lastUpdateInverter = DateTime.UtcNow;


    public Worker(Bluetooth bt, MQTTClient mqtt, IHostApplicationLifetime app, ILogger<Worker> logger)
    {
        _bt = bt;
        _mqtt = mqtt;
        _app = app;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await _bt.PowerOff();
        await _bt.PowerOn();
        _bt.DeviceAdded += OnDeviceAdded;
        await _mqtt.Connect();
        await _bt.StartScan("R33");
        
        await Task.Delay(-1, cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_device != null)
        {
            if (await _device.GetConnected())
            {
                _logger.LogInformation("Disconnecting from {Address}...", await _device.GetAddress());
                await _device.Disconnect();
                while(await _device.GetConnected()) await Task.Delay(1000);
            }
            _device.Dispose();
        }
        await _bt.PowerOff();
        await _bt.PowerOn();
        _bt.Dispose();
        _logger.LogInformation("Shutting down...");
        await base.StopAsync(cancellationToken);
    }

    private async Task OnDeviceAdded(BtDevice device)
    {
        await _bt.StopScan();
        
        var address = await device.GetAddress();
        var alias = await device.GetAlias();
        _logger.LogInformation("Device added {Address} {Alias}", address, alias);
        
        if(_device != null)
        {
            device.Dispose();
            return;
        }

        _logger.LogInformation("Will use {Address} {Alias}", address, alias);
        _device = device;
        _device.Connected += OnDeviceConnected;
        _device.ServicesResolved += OnServicesResolved;
        _device.Disconnected += OnDeviceDisonnected;

        await ConnectWithRetry(_device);
    }

    private async Task ConnectWithRetry(BtDevice device)
    {
        while (!(await device.GetConnected()))
        {
            _logger.LogWarning("Trying to connect...");
            try
            {
                await device.Connect();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection failed, will retry in 5s", ex.Message);
            }
            await Task.Delay(5000);
        }
    }

    private async Task OnDeviceConnected(BtDevice device)
    {
        _logger.LogInformation("Device connected: {Address}", await device.GetAddress());
    }

    private async Task OnServicesResolved(BtDevice device)
    {
        _logger.LogInformation("Services resolved: \n{Services}", string.Join("\n", await device.GetUUIDs()));
        var service = await device.GetService(ServiceUUID);
        if (service == null)
        {
            _logger.LogCritical("Required service not found");
            _app.StopApplication();
            return;
        }
        
        var ch = await service.GetCharacteristic(CharacteristicUUID);
        if (ch == null) 
        {
            _logger.LogCritical("Required characteristic not found");
            _app.StopApplication();
            return;
        }
        
        ch.ValueChanged += OnValueChanged;
    }

    private async Task OnDeviceDisonnected(BtDevice device)
    {
        _logger.LogInformation("Device disconnected");
        if (_app.ApplicationStopping.IsCancellationRequested || _app.ApplicationStopped.IsCancellationRequested) return;
        await ConnectWithRetry(device);
    }

    private async Task OnValueChanged(GattCharacteristic ch, GattCharacteristicValueEventArgs args)
    {
        var data = args.Value;
        var packets = GetPackets(data);
        foreach(var p in packets)
        {
            switch (p.GetPacketType())
            {
                case PacketType.PD:
                    await ProcessPd(p);
                    break;
                case PacketType.BMS:
                    await ProcessBms(p);
                    break;
                case PacketType.Inverter:
                    await ProcessInverter(p);
                    break;
            }
        }
    }

    private List<byte[]> GetPackets(byte[] bytes)
    {
        var result = new List<byte[]>();
        while (bytes[0] == 0xaa && bytes[1] == 0x02)
        {
            var size = BitConverter.ToInt16(bytes, 2);
            var packet = new byte[18 + size];
            Array.Copy(bytes, packet, packet.Length);
            result.Add(packet);
            if (bytes.Length <= packet.Length) break;
            var bytesNew = new byte[bytes.Length - packet.Length];
            Array.Copy(bytes, packet.Length, bytesNew, 0, bytesNew.Length);
            bytes = bytesNew;
        }
        return result;
    }

    private async Task ProcessPd(byte[] packet)
    {
        var data = packet.DecodePayload();
        if ((DateTime.UtcNow - _lastUpdatePd) > TimeSpan.FromSeconds(5))
        {
            _lastUpdatePd = DateTime.UtcNow;
            var battery = data[12];
            var inPower = BitConverter.ToInt16(data, 15);
            var outPower = BitConverter.ToInt16(data, 13);
            var remainingMins = BitConverter.ToInt32(data, 17);
            try
            {
                var payload = JsonSerializer.Serialize(new {battery, inPower, outPower, remainingMins});
                _logger.LogInformation(payload);
                await _mqtt.Send("ecoflow_pd", payload, _app.ApplicationStopping);
            }
            catch (Exception ex) 
            {
                _logger.LogWarning(ex, "Sending via MQTT failed");
            }
        }
    }

    private async Task ProcessBms(byte[] packet)
    {
        var cellId = packet[16];
        var data = packet.DecodePayload();

        if ((DateTime.UtcNow - (cellId == 0 ? _lastUpdateBms0 : _lastUpdateBms1)) > TimeSpan.FromSeconds(5))
        {
            if (cellId == 0) _lastUpdateBms0 = DateTime.UtcNow;
            else _lastUpdateBms1 = DateTime.UtcNow;

            var level = data[9];
            var temp = data[18];
            var cycles = data[32];
            var voltage = BitConverter.ToInt32(data, 10) / 1000.0;
            var inPower = BitConverter.ToInt32(data, 55);
            var outPower = BitConverter.ToInt32(data, 59);
            var remainingMins = BitConverter.ToInt32(data, 63);
            try
            {
                var payload = JsonSerializer.Serialize(new {cellId, level, temp, cycles, voltage, inPower, outPower, remainingMins});
                _logger.LogInformation(payload);
                await _mqtt.Send($"ecoflow_bms_{cellId}", payload, _app.ApplicationStopping);
            }
            catch (Exception ex) 
            {
                _logger.LogWarning(ex, "Sending via MQTT failed");
            }
        }
    }

    private async Task ProcessInverter(byte[] packet)
    {
        var data = packet.DecodePayload();
        if ((DateTime.UtcNow - _lastUpdateInverter) > TimeSpan.FromSeconds(5))
        {
            _lastUpdateInverter = DateTime.UtcNow;
            var acInType = data[6];
            var acInPower = BitConverter.ToInt16(data, 7);
            var acOutPower = BitConverter.ToInt16(data, 9);
            var acInVoltage = BitConverter.ToInt32(data, 21) / 1000.0;
            var acOutVoltage = BitConverter.ToInt32(data, 12) / 1000.0;
            var acInCurrent = BitConverter.ToInt32(data, 25) / 1000.0;
            var acOutCurrent = BitConverter.ToInt32(data, 16) / 1000.0;
            var acOutTemp = BitConverter.ToInt16(data, 30);
            try
            {
                var payload = JsonSerializer.Serialize(new 
                    {
                        acInType, 
                        acInPower, 
                        acOutPower, 
                        acInVoltage, 
                        acOutVoltage, 
                        acInCurrent, 
                        acOutCurrent, 
                        acOutTemp
                    });
                _logger.LogInformation(payload);
                await _mqtt.Send("ecoflow_inverter", payload, _app.ApplicationStopping);
            }
            catch (Exception ex) 
            {
                _logger.LogWarning(ex, "Sending via MQTT failed");
            }
        }
    }
}
