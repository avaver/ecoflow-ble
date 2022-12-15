using bluez.DBus;

namespace ecoflow_ble;

class GattService
{
    private readonly IGattService1 _gattService;

    public GattService(IGattService1 gattService)
    {
        _gattService = gattService;
    }

    public async Task<GattCharacteristic?> GetCharacteristic(string uuid)
    {
        var proxies = await Bluetooth.GetProxies<IGattCharacteristic1>(_gattService, Bluetooth.GattCharInterface);
        foreach(var p in proxies)
            if ((await p.GetUUIDAsync()).ToLowerInvariant() == uuid.ToLowerInvariant())
                return await GattCharacteristic.Create(p);

        return null;
    }
}