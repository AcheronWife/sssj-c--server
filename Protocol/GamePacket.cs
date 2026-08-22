namespace Gcg2OfflineServer.Protocol;

/// <summary>
/// 16 字节游戏包头 + payload。
/// 布局（全部小端）：
///   [0..1]  command      ushort
///   [2..3]  returnCode   ushort
///   [4..7]  size         uint32  = 16 + payload.Length
///   [8..11] serial       uint32
///   [12]    compressed   byte
///   [13]    magic        byte    = 0x88
///   [14..15] reserved    byte[2] = 0
///   [16..]  payload
/// </summary>
public static class GamePacket
{
    public const int HeaderSize = 16;
    public const byte Magic = 0x88;

    public static byte[] Make(ushort command, uint serial, byte[]? payload = null, ushort returnCode = 0)
    {
        payload ??= Array.Empty<byte>();
        var packet = new byte[HeaderSize + payload.Length];
        BitConverter.TryWriteBytes(packet.AsSpan(0, 2), command);
        BitConverter.TryWriteBytes(packet.AsSpan(2, 2), returnCode);
        BitConverter.TryWriteBytes(packet.AsSpan(4, 4), (uint)(HeaderSize + payload.Length));
        BitConverter.TryWriteBytes(packet.AsSpan(8, 4), serial);
        packet[12] = 0;
        packet[13] = Magic;
        // 14, 15 保留为 0
        if (payload.Length > 0)
            Buffer.BlockCopy(payload, 0, packet, HeaderSize, payload.Length);
        return packet;
    }

    public static ParsedPacket Read(byte[] packet)
    {
        if (packet.Length < HeaderSize)
            throw new InvalidDataException($"Packet too short: {packet.Length} < {HeaderSize}");

        var size = BitConverter.ToUInt32(packet, 4);
        if (size != (uint)packet.Length)
            throw new InvalidDataException($"Packet size mismatch: header={size}, actual={packet.Length}");

        if (packet[13] != Magic)
            throw new InvalidDataException($"Unexpected packet magic: 0x{packet[13]:X2}");

        var command = BitConverter.ToUInt16(packet, 0);
        var returnCode = BitConverter.ToUInt16(packet, 2);
        var serial = BitConverter.ToUInt32(packet, 8);
        var payload = new byte[packet.Length - HeaderSize];
        if (payload.Length > 0)
            Buffer.BlockCopy(packet, HeaderSize, payload, 0, payload.Length);

        return new ParsedPacket(command, returnCode, serial, payload);
    }
}

public record ParsedPacket(ushort Command, ushort ReturnCode, uint Serial, byte[] Payload);
