using bluez.DBus;
using Tmds.DBus;

namespace ecoflow_ble;

delegate Task DeviceAddedHandlerAsync(BtDevice device);

public class Bluetooth: IDisposable
{
    public const string DBusService = "org.bluez";
    public const string AdapterPath = "/org/bluez/hci0";
    public const string DeviceInterface = "org.bluez.Device1";
    public const string GattInterface = "org.bluez.GattService1";
    public const string GattCharInterface = "org.bluez.GattCharacteristic1";

    private readonly ILogger<Bluetooth> _logger;
    private static readonly IObjectManager _manager = Connection.System.CreateProxy<IObjectManager>(DBusService, "/");
    private readonly IAdapter1 _adapter;
    private IDisposable? _deviceWatcher;

    internal event DeviceAddedHandlerAsync? DeviceAdded;

    public Bluetooth(ILogger<Bluetooth> logger)
    {
        _logger = logger;
        _adapter = Connection.System.CreateProxy<IAdapter1>(DBusService, AdapterPath);
    }

    public static async Task<IReadOnlyList<T>> GetProxies<T>(IDBusObject parent, string interfaceName)
    {   
        return (await _manager.GetManagedObjectsAsync())
            .Where(o => o.Value.Keys.Contains(interfaceName))
            .Select(o => o.Key)
            .Where(k => k.ToString().StartsWith($"{parent.ObjectPath}/"))
            .Select(k => Connection.System.CreateProxy<T>(Bluetooth.DBusService, k))
            .ToList();
    }

    public async Task StartScan(string namePrefix, bool bleOnly = true)
    {
        _deviceWatcher = await _manager.WatchInterfacesAddedAsync(OnDeviceAdded);
        var filters = new Dictionary<string, object>();
        if (bleOnly) filters.Add("Transport", "le");
        if (!string.IsNullOrEmpty(namePrefix)) filters.Add("Pattern", namePrefix);
        await _adapter.SetDiscoveryFilterAsync(filters);
        _logger.LogInformation("Discovering bluetooth devices (filter: \"{Filter}\")...", namePrefix);
        await _adapter.StartDiscoveryAsync();
    }

    public async Task StopScan()
    {
        _logger.LogInformation("Discovery stop requested");
        if (await _adapter.GetDiscoveringAsync()) 
        {
            await _adapter.StopDiscoveryAsync();
            _logger.LogInformation("Discovery stopped");
        }
        _deviceWatcher?.Dispose();
    }

    private void OnDeviceAdded((ObjectPath objectPath, IDictionary<string, IDictionary<string, object>> interfaces) args)
    {
        if (args.objectPath.ToString().StartsWith($"{AdapterPath}/") && args.interfaces.ContainsKey(DeviceInterface))
        {
            _logger.LogInformation("Found bluetooth device: {path}", args.objectPath.ToString());
            if (DeviceAdded != null)
            {
                DeviceAdded(new BtDevice(args.objectPath.ToString()));
            }
        }
    }

    public async Task PowerOn()
    {
        _logger.LogInformation("Bluetooth adapter ON");
        await _adapter.SetPoweredAsync(true);
    }

    public async Task PowerOff()
    {
        _logger.LogInformation("Bluetooth adapter OFF");
        await _adapter.SetPoweredAsync(false);
    }

    public void Dispose()
    {
        _deviceWatcher?.Dispose();
    }
}