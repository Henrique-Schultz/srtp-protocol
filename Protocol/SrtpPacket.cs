using System.Buffers.Binary;

namespace Srtp.Protocol;

public sealed class SrtpPacket
{
    public const int HeaderLength = 9;
    public const int MaxPayloadLength = 255;

    public bool Syn { get; set; }
    public bool Fin { get; set; }
    public ushort Seq { get; set; }
    public bool AckFlag { get; set; }
    public bool Nack { get; set; }
    public ushort Ack { get; set; }
    public byte Length { get; set; }
    public uint Crc32 { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public byte[] ToBytes()
    {
        Validate();

        var bytes = new byte[HeaderLength + Payload.Length];
        WriteHeader(bytes, crc32: 0);
        Payload.CopyTo(bytes.AsSpan(HeaderLength));

        var crc = Crc32Helper(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(5, 4), crc);
        Crc32 = crc;

        return bytes;
    }

    public static bool TryParse(byte[] datagram, out SrtpPacket? packet)
    {
        packet = null;
        if (datagram.Length < HeaderLength)
        {
            return false;
        }

        var first = BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(0, 4));
        var length = datagram[4];
        var syn = ((first >> 31) & 1u) == 1u;
        var fin = ((first >> 30) & 1u) == 1u;
        var ackFlag = ((first >> 15) & 1u) == 1u;
        var isControlWithoutPayload = datagram.Length == HeaderLength && (syn || fin || ackFlag);

        if (datagram.Length != HeaderLength + length && !isControlWithoutPayload)
        {
            return false;
        }

        var payloadLength = isControlWithoutPayload ? 0 : length;
        packet = new SrtpPacket
        {
            Syn = syn,
            Fin = fin,
            Seq = (ushort)((first >> 16) & SequenceNumber.MaxValue),
            AckFlag = ackFlag,
            Nack = ((first >> 14) & 1u) == 1u,
            Ack = (ushort)(first & SequenceNumber.MaxValue),
            Length = length,
            Crc32 = BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(5, 4)),
            Payload = datagram.AsSpan(HeaderLength, payloadLength).ToArray()
        };

        return true;
    }

    public bool IsValidChecksum()
    {
        Validate();
        var bytes = new byte[HeaderLength + Payload.Length];
        WriteHeader(bytes, crc32: 0);
        Payload.CopyTo(bytes.AsSpan(HeaderLength));
        return Crc32Helper(bytes) == Crc32;
    }

    private void Validate()
    {
        if (Seq > SequenceNumber.MaxValue)
        {
            throw new InvalidOperationException("SEQ deve caber em 14 bits.");
        }

        if (Ack > SequenceNumber.MaxValue)
        {
            throw new InvalidOperationException("ACK deve caber em 14 bits.");
        }

        if (Payload.Length > MaxPayloadLength)
        {
            throw new InvalidOperationException("Payload nao pode passar de 255 bytes.");
        }

        if (Length != Payload.Length && !IsControlWithoutPayload())
        {
            throw new InvalidOperationException("Length deve ser igual ao tamanho do payload.");
        }
    }

    private void WriteHeader(Span<byte> destination, uint crc32)
    {
        var first =
            (Syn ? 1u : 0u) << 31 |
            (Fin ? 1u : 0u) << 30 |
            ((uint)Seq & SequenceNumber.MaxValue) << 16 |
            (AckFlag ? 1u : 0u) << 15 |
            (Nack ? 1u : 0u) << 14 |
            ((uint)Ack & SequenceNumber.MaxValue);

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], first);
        destination[4] = Length;
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(5, 4), crc32);
    }

    private static uint Crc32Helper(ReadOnlySpan<byte> data)
    {
        return Protocol.Crc32.Compute(data);
    }

    private bool IsControlWithoutPayload()
    {
        return Payload.Length == 0 && (Syn || Fin || AckFlag);
    }
}
