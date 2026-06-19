namespace Srtp.Protocol;

public static class SequenceNumber
{
    public const ushort MaxValue = 0x3FFF;

    public static ushort NextSeq(ushort seq)
    {
        return (ushort)((seq + 1) & MaxValue);
    }

    public static ushort PreviousSeq(ushort seq)
    {
        return (ushort)((seq - 1) & MaxValue);
    }
}
