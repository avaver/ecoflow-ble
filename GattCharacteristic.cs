using bluez.DBus;
using Tmds.DBus;

namespace ecoflow_ble;

public class GattCharacteristicValueEventArgs : EventArgs
{
    public GattCharacteristicValueEventArgs(byte[] value)
    {
      Value = value;
    }

    public byte[] Value { get; }
}

delegate Task GattCharacteristicEventHandlerAsync(GattCharacteristic sender, GattCharacteristicValueEventArgs args);

class GattCharacteristic: IDisposable
{
    private IGattCharacteristic1 _characteristic;

    private IDisposable? _watcher;

    internal event GattCharacteristicEventHandlerAsync? ValueChanged;

    private GattCharacteristic(IGattCharacteristic1 characteristic)
    {
        _characteristic = characteristic;
    }

    public static async Task<GattCharacteristic> Create(IGattCharacteristic1 characteristic)
    {
        var ch = new GattCharacteristic(characteristic);
        ch._watcher = await ch._characteristic.WatchPropertiesAsync(ch.OnPropertyChanges);
        await ch._characteristic.StartNotifyAsync();
        return ch;
    }

    private void OnPropertyChanges(PropertyChanges changes)
    {
      foreach (var pair in changes.Changed)
      {
        switch (pair.Key)
        {
          case "Value":
            if (ValueChanged != null) ValueChanged(this, new GattCharacteristicValueEventArgs((byte[])pair.Value));
            break;
        }
      }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}