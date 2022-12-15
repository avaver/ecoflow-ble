using bluez.DBus;
using Tmds.DBus;

namespace ecoflow_ble;

public class Worker : BackgroundService
{
    private const string ServiceUUID = "00000001-0000-1000-8000-00805f9b34fb";
    private const string CharacteristicUUID = "00000003-0000-1000-8000-00805f9b34fb";
    private readonly ILogger<Worker> _logger;
    private readonly Bluetooth _bt;
    private readonly DbClient _db;
    private readonly MQTTClient _mqtt;
    private readonly IHostApplicationLifetime _app;
    private BtDevice? _device;
    private static DateTime _lastUpdate = DateTime.UtcNow;


    public Worker(Bluetooth bt, DbClient db, MQTTClient mqtt, IHostApplicationLifetime app, ILogger<Worker> logger)
    {
        _bt = bt;
        _db = db;
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
        if (data.Length >= 51 && data[0] == 0xaa && data[1] == 0x02 && data[2] == 0x7b && (DateTime.UtcNow - _lastUpdate) > TimeSpan.FromSeconds(5))
        {
            _lastUpdate = DateTime.UtcNow;
            var battery = data[30];
            var temp = data[51];
            var inPower = BitConverter.ToInt16(data, 33);
            var outPower = BitConverter.ToInt16(data, 31);
            _logger.LogInformation("battery: {Battery}%, in: {In}W, out: {Out}W, temp: {Temp}°C", battery, inPower, outPower, temp);
            // try
            // {
            //     _db.Write(battery, inPower, outPower, temp);
            // }
            // catch (Exception ex)
            // {
            //     _logger.LogWarning(ex, "Writing to DB failed");
            // }
            try
            {
                await _mqtt.Send(battery, inPower, outPower, temp, _app.ApplicationStopping);
            }
            catch (Exception ex) 
            {
                _logger.LogWarning(ex, "Sending via MQTT failed");
            }
        }
    }
}
