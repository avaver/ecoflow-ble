public enum PacketType
{
    PD,
    BMS,
    EMS,
    Inverter,
    Unknown
}

public static class ByteExtensions
{
    public static byte[] GetHeader(this byte[] data)
    {
        var header = new byte[18];
        Array.Copy(data, 0, header, 0, header.Length);
        return header;
    }

    public static byte[] DecodePayload(this byte[] data)
    {
        var size = BitConverter.ToInt16(data, 2);
        var payload = new byte[size];
        Array.Copy(data, 18, payload, 0, size);
        payload = payload.Select(b => (byte)(b ^ data[6])).ToArray();
        return payload;
    }

    public static PacketType GetPacketType(this byte[] data)
    {
        if (data[12] == 2 && data[14] == 32 && data[15] == 2)
            return PacketType.PD;
        if (data[12] == 3 && data[14] == 32 && data[15] == 2)
            return PacketType.EMS;
        if (data[12] == 4 && data[14] == 32 && data[15] == 2)
            return PacketType.Inverter;
        if ((data[12] == 3 && data[14] == 32 && data[15] == 50) ||
            (data[12] == 6 && data[14] == 32 && data[15] == 2) ||
            (data[12] == 6 && data[14] == 32 && data[15] == 50))
            return PacketType.BMS;
        return PacketType.Unknown;
    }
}