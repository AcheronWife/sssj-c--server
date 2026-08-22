using System.Text;

namespace Gcg2OfflineServer.Protocol;

/// <summary>
/// 极简 Protobuf 编解码器。
/// 仅支持 varint (wire 0) 和 bytes/string (wire 2)，足够覆盖 GCG2 协议。
/// </summary>
public static class ProtobufWriter
{
    public static byte[] EncodeVarint(long value)
    {
        var bytes = new List<byte>(10);
        var remaining = (ulong)value;
        do
        {
            byte b = (byte)(remaining & 0x7f);
            remaining >>= 7;
            if (remaining != 0) b |= 0x80;
            bytes.Add(b);
        } while (remaining != 0);
        return bytes.ToArray();
    }

    public static byte[] FieldVarint(int fieldNumber, long value)
        => Concat(EncodeVarint(fieldNumber << 3), EncodeVarint(value));

    public static byte[] FieldBytes(int fieldNumber, byte[] data)
        => Concat(EncodeVarint((fieldNumber << 3) | 2), EncodeVarint(data.Length), data);

    public static byte[] FieldBytes(int fieldNumber, string str)
        => FieldBytes(fieldNumber, Encoding.UTF8.GetBytes(str));

    public static byte[] Concat(params byte[][] arrays)
    {
        var total = arrays.Sum(a => a.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var a in arrays)
        {
            Buffer.BlockCopy(a, 0, result, offset, a.Length);
            offset += a.Length;
        }
        return result;
    }
}

public static class ProtobufReader
{
    public class Field
    {
        public int FieldNumber;
        public int WireType;
        public long VarintValue;
        public byte[] BytesValue = Array.Empty<byte>();
    }

    public static List<Field> Decode(byte[] buffer)
    {
        var fields = new List<Field>();
        var offset = 0;
        while (offset < buffer.Length)
        {
            var (tag, tagLen) = ReadVarint(buffer, offset);
            offset += tagLen;
            var fieldNumber = (int)(tag >> 3);
            var wireType = (int)(tag & 7);
            if (fieldNumber == 0)
                throw new InvalidDataException("Invalid protobuf field 0");

            if (wireType == 0)
            {
                var (val, len) = ReadVarint(buffer, offset);
                offset += len;
                fields.Add(new Field { FieldNumber = fieldNumber, WireType = 0, VarintValue = val });
            }
            else if (wireType == 2)
            {
                var (length, lenLen) = ReadVarint(buffer, offset);
                offset += lenLen;
                var end = offset + (int)length;
                if (end > buffer.Length)
                    throw new InvalidDataException("Truncated bytes field");
                var data = new byte[(int)length];
                if (data.Length > 0)
                    Buffer.BlockCopy(buffer, offset, data, 0, data.Length);
                offset = end;
                fields.Add(new Field { FieldNumber = fieldNumber, WireType = 2, BytesValue = data });
            }
            else
            {
                throw new NotSupportedException($"Unsupported protobuf wire type {wireType}");
            }
        }
        return fields;
    }

    public static string FirstString(List<Field> fields, int fieldNumber, string fallback = "")
    {
        var f = fields.FirstOrDefault(x => x.FieldNumber == fieldNumber && x.WireType == 2);
        return f != null ? Encoding.UTF8.GetString(f.BytesValue) : fallback;
    }

    public static long FirstNumber(List<Field> fields, int fieldNumber, long fallback = 0)
    {
        var f = fields.FirstOrDefault(x => x.FieldNumber == fieldNumber && x.WireType == 0);
        return f != null ? f.VarintValue : fallback;
    }

    private static (long value, int length) ReadVarint(byte[] buffer, int offset)
    {
        long value = 0;
        int shift = 0;
        int pos = offset;
        while (pos < buffer.Length && shift < 70)
        {
            byte b = buffer[pos++];
            value |= (long)(b & 0x7f) << shift;
            if ((b & 0x80) == 0)
                return (value, pos - offset);
            shift += 7;
        }
        throw new InvalidDataException("Invalid protobuf varint");
    }
}
