using System.Buffers.Binary;

namespace Srtp.Protocol;

public sealed class SrtpPacket
{
    // Cabecalho fixo do SRTP: 9 bytes no total.
    // Bytes 0-3: flags SYN/FIN/ACK/NACK + SEQ de 14 bits + ACK de 14 bits.
    // Byte 4: Length, usado como tamanho do payload nos dados e como janela proposta no SYN/SYN+ACK.
    // Bytes 5-8: CRC32, usado para detectar corrupcao no cabecalho ou no payload.
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
        byte[] bytes = new byte[HeaderLength + Payload.Length];

        // O CRC e calculado com o campo de CRC zerado; depois o valor real e gravado no cabecalho.
        // Isso evita o problema circular de o CRC precisar incluir o proprio valor do CRC.
        WriteHeader(bytes, crc32: 0);
        Payload.CopyTo(bytes.AsSpan(HeaderLength));

        uint crc = Protocol.Crc32.Compute(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(5, 4), crc);
        Crc32 = crc;

        return bytes;
    }

    public static SrtpPacket? Parse(byte[] datagram)
    {
        if (datagram.Length < HeaderLength)
        {
            return null;
        }

        uint first = BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(0, 4));
        byte length = datagram[4];
        bool syn = ((first >> 31) & 1u) == 1u;
        bool fin = ((first >> 30) & 1u) == 1u;
        bool ackFlag = ((first >> 15) & 1u) == 1u;
        bool isControlPacket = syn || fin || ackFlag;
        int payloadLength = isControlPacket ? 0 : length;

        // Pacotes de controle usam apenas o cabecalho. No SYN/SYN+ACK, o campo Length
        // nao representa payload: ele carrega a janela proposta para negociacao entre os lados.
        if (isControlPacket && datagram.Length != HeaderLength)
        {
            return null;
        }

        if (!isControlPacket && datagram.Length != HeaderLength + length)
        {
            return null;
        }

        return new SrtpPacket
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
    }

    public bool IsValidChecksum()
    {
        // Reconstroi o datagrama com CRC zerado para comparar com o CRC recebido.
        byte[] bytes = new byte[HeaderLength + Payload.Length];
        WriteHeader(bytes, crc32: 0);
        Payload.CopyTo(bytes.AsSpan(HeaderLength));
        return Protocol.Crc32.Compute(bytes) == Crc32;
    }

    private void WriteHeader(Span<byte> destination, uint crc32)
    {
        // Layout dos primeiros 32 bits:
        // bit 31 SYN, bit 30 FIN, bits 29-16 SEQ(14), bit 15 ACK, bit 14 NACK, bits 13-0 ACK(14).
        uint first =
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
}
