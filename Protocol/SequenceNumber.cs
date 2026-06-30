namespace Srtp.Protocol;

public static class SequenceNumber
{
    // O protocolo reserva 14 bits para numeros de sequencia e ACK: 0 ate 16383.
    // Depois de 0x3FFF, o proximo numero volta para 0. Esse comportamento e chamado de wrap-around.
    public const ushort MaxValue = 0x3FFF;

    public static ushort NextSeq(ushort seq)
    {
        // O AND com MaxValue faz o wrap-around voltar para zero automaticamente.
        return (ushort)((seq + 1) & MaxValue);
    }

    public static ushort PreviousSeq(ushort seq)
    {
        return (ushort)((seq - 1) & MaxValue);
    }
}
