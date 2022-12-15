using bluez.DBus;
using Tmds.DBus;

namespace ecoflow_ble;

delegate Task StateChangedHandlerAsync(BtDevice sender);

class BtDevice: IDisposable
{
    private readonly ILogger<BtDevice> _logger;
    private readonly IDevice1 _device;
    private IDisposable? _propertiesWatcher;

    internal event StateChangedHandlerAsync? Connected;
    internal event StateChangedHandlerAsync? Disconnected;
    internal event StateChangedHandlerAsync? ServicesResolved;

    public BtDevice(string path)
    {
        _device = Connection.System.CreateProxy<IDevice1>(Bluetooth.DBusService, path);
        _logger = LoggerFactory.Create(l => l.AddConsole().AddSystemdConsole()).CreateLogger<BtDevice>();
    }

    public async Task Connect()
    {
        _propertiesWatcher = await _device.WatchPropertiesAsync(OnPropertyChanged);
        await _device.ConnectAsync();
    }

    public async Task Disconnect()
    {
        await _device.DisconnectAsync();
    }

    private void OnPropertyChanged(PropertyChanges changes)
    {
        foreach(var p in changes.Changed)
        {
            _logger.LogDebug("Device property \"{Property}\" changed to \"{Value}\"", p.Key, p.Value);
            switch (p.Key)
            {
                case "Connected":
                    if (true.Equals(p.Value) && Connected != null) Connected(this);
                    else if (false.Equals(p.Value) && Disconnected != null) Disconnected(this); 
                    break;
                case "ServicesResolved":
                    if (true.Equals(p.Value) && ServicesResolved != null) ServicesResolved(this);
                    break;
            }
        }
    }

    public async Task<GattService?> GetService(string uuid)
    {
        var proxies = await Bluetooth.GetProxies<IGattService1>(_device, Bluetooth.GattInterface);
        foreach(var p in proxies)
            if ((await p.GetUUIDAsync()).ToLowerInvariant() == uuid.ToLowerInvariant())
                return new GattService(p);

        return null;
    }

    public void Dispose()
    {
        _propertiesWatcher?.Dispose();
    }

    public async Task<string> GetAddress() 
    {
        return await _device.GetAddressAsync();
    }

    public async Task<string> GetName() 
    {
        return await _device.GetNameAsync();
    }

    public async Task<string> GetAlias() 
    {
        return await _device.GetAliasAsync();
    }

    public async Task<bool> GetConnected() 
    {
        return await _device.GetConnectedAsync();
    }

    public async Task<bool> GetPaired() 
    {
        return await _device.GetPairedAsync();
    }

    public async Task<short> GetRSSI() 
    {
        return await _device.GetRSSIAsync();
    }

    public async Task<bool> GetServiceResolved() 
    {
        return await _device.GetServicesResolvedAsync();
    }

    public async Task<short> GetTxPower() 
    {
        return await _device.GetTxPowerAsync();
    }

    public async Task<string[]> GetUUIDs() 
    {
        return await _device.GetUUIDsAsync();
    }

    public async Task<Device1Properties> GetAllProperties()
    {
        return await _device.GetAllAsync();
    }

    public async Task<IDictionary<ushort, object>> GetManufacturerData() 
    {
        return await _device.GetManufacturerDataAsync();
    }
}