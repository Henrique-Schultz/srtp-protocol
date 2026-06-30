namespace Srtp.Protocol;

public static class PacketFactory
{
    // Metodos de fabrica evitam repetir a montagem de pacotes de controle pelo codigo dos modos.
    public static SrtpPacket CreateAck(ushort ackNumber)
    {
        // ACK confirma um numero de sequencia.
        // No Stop-and-Wait e no Selective Repeat ele confirma aquele pacote individual.
        // No Go-Back-N ele funciona como ACK cumulativo: tudo ate esse SEQ foi aceito em ordem.
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
            // No SYN, Length nao representa payload: ele transporta a janela desejada.
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
            // O receiver devolve sua janela para permitir negociar o menor valor entre os dois lados.
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

    public static SrtpPacket CreateNack(ushort ackNumber)
    {
        // NACK carrega o proximo SEQ esperado pelo receiver.
        // Em GBN isso faz o sender voltar a partir desse ponto.
        // Em SR isso permite retransmitir apenas o pacote faltante.
        return new SrtpPacket
        {
            AckFlag = true,
            Nack = true,
            Ack = ackNumber,
            Length = 0,
            Payload = Array.Empty<byte>()
        };
    }
}
