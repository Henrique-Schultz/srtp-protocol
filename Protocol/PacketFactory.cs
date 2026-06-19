namespace Srtp.Protocol;

public static class PacketFactory
{
    public static SrtpPacket CreateAck(ushort ackNumber)
    {
        return new SrtpPacket
        {
            AckFlag = true,
            Ack = ackNumber,
            Length = 0,
            Payload = Array.Empty<byte>()
        };
    }

    public static SrtpPacket CreateSyn(byte proposedWindow)
    {
        return new SrtpPacket
        {
            Syn = true,
            Seq = 0,
            Ack = 0,
            Length = proposedWindow,
            Payload = Array.Empty<byte>()
        };
    }

    public static SrtpPacket CreateSynAck(byte proposedWindow)
    {
        return new SrtpPacket
        {
            Syn = true,
            AckFlag = true,
            Seq = 0,
            Ack = 0,
            Length = proposedWindow,
            Payload = Array.Empty<byte>()
        };
    }

    public static SrtpPacket CreateFin()
    {
        return new SrtpPacket
        {
            Fin = true,
            Length = 0,
            Payload = Array.Empty<byte>()
        };
    }

    public static SrtpPacket CreateFinAck()
    {
        return new SrtpPacket
        {
            Fin = true,
            AckFlag = true,
            Length = 0,
            Payload = Array.Empty<byte>()
        };
    }
}
