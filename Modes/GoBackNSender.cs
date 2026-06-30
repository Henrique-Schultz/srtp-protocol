using System.Net;
using System.Net.Sockets;
using Srtp.Protocol;
using Srtp.Stats;

namespace Srtp.Modes;

public sealed class GoBackNSender : ITransferMode
{
    // Go-Back-N:
    // - Usa janela deslizante para manter varios pacotes em voo.
    // - ACK e cumulativo: ao receber ACK de SEQ X, o sender considera confirmados todos os pacotes ate X.
    // - NACK aponta o proximo SEQ esperado pelo receiver.
    // - Em timeout ou NACK, retransmite a janela a partir da falha, inclusive pacotes que talvez ja tenham chegado.
    // - Por isso tem bom ganho com latencia, mas sofre muito com perda e reordenacao.
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    public string Name => "gbn";

    public async Task SendFileAsync(string host, int port, string filePath, byte proposedWindow)
    {
        IPAddress[] receiverAddresses = await Dns.GetHostAddressesAsync(host);
        IPAddress receiverAddress = receiverAddresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
            ?? receiverAddresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Host do receiver nao foi resolvido.");
        IPEndPoint receiver = new IPEndPoint(receiverAddress, port);

        UdpClient udp = new UdpClient(port + 1);
        try
        {
            udp.Connect(receiver);

            Console.WriteLine($"Sender GBN ligado na porta local {port + 1}; destino {receiver}.");
            byte window = await HandshakeAsync(udp, proposedWindow);

            SenderStats stats = new SenderStats();
            stats.Start();

            List<SrtpPacket> packets = await ReadPacketsAsync(filePath, stats);
            await SendPacketsAsync(udp, packets, window, stats);
            await CloseAsync(udp, stats);

            stats.Stop();
            stats.Print(filePath);
        }
        finally
        {
            udp.Dispose();
        }
    }

    private static async Task<List<SrtpPacket>> ReadPacketsAsync(string filePath, SenderStats stats)
    {
        // GBN monta a lista completa para conseguir voltar e retransmitir uma janela anterior.
        List<SrtpPacket> packets = new List<SrtpPacket>();
        ushort seq = 0;
        byte[] buffer = new byte[SrtpPacket.MaxPayloadLength];

        FileStream stream = File.OpenRead(filePath);
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer);
                byte[] payload = buffer.AsSpan(0, read).ToArray();
                SrtpPacket packet = new SrtpPacket
                {
                    Seq = seq,
                    Length = (byte)read,
                    Payload = payload
                };

                packets.Add(packet);
                stats.ApplicationBytes += read;
                stats.OriginalDataPackets++;
                seq = SequenceNumber.NextSeq(seq);

                if (read < SrtpPacket.MaxPayloadLength)
                {
                    break;
                }
            }
        }
        finally
        {
            stream.Dispose();
        }

        return packets;
    }

    private static async Task SendPacketsAsync(UdpClient udp, List<SrtpPacket> packets, byte window, SenderStats stats)
    {
        // baseIndex aponta para o primeiro pacote ainda nao confirmado; nextIndex para o proximo envio novo.
        int baseIndex = 0;
        int nextIndex = 0;

        while (baseIndex < packets.Count)
        {
            // Preenche a janela: envia pacotes novos enquanto houver espaco entre baseIndex e baseIndex + window.
            while (nextIndex < packets.Count && nextIndex < baseIndex + window)
            {
                await SendPacketAsync(udp, packets[nextIndex]);
                nextIndex++;
            }

            UdpReceiveResult? result = await ReceivePacketOrTimeoutAsync(udp);
            if (!result.HasValue)
            {
                // No timeout do GBN, toda a janela em voo e reenviada.
                // Essa e a principal causa da explosao de retransmissoes em perdas/reordenacao.
                await ResendWindowAsync(udp, packets, baseIndex, nextIndex, stats);
                continue;
            }

            UdpReceiveResult receive = result.Value;
            SrtpPacket? response = SrtpPacket.Parse(receive.Buffer);
            if (response == null || !response.IsValidChecksum() || !response.AckFlag)
            {
                continue;
            }

            if (response.Nack)
            {
                // NACK indica o primeiro pacote que o receiver ainda espera.
                // O sender move a base para esse pacote e reenvia tudo dali ate o fim da janela atual.
                int nackIndex = FindPacketIndex(packets, baseIndex, response.Ack);
                if (nackIndex >= 0)
                {
                    baseIndex = nackIndex;
                    await ResendWindowAsync(udp, packets, baseIndex, nextIndex, stats);
                }
                continue;
            }

            int ackIndex = FindPacketIndex(packets, baseIndex, response.Ack);
            if (ackIndex >= 0)
            {
                // ACK cumulativo: tudo ate esse indice pode sair da janela.
                // Exemplo: ACK 10 confirma que 0..10 chegaram em ordem.
                baseIndex = ackIndex + 1;
            }
        }
    }

    private static int FindPacketIndex(List<SrtpPacket> packets, int startIndex, ushort seq)
    {
        for (int index = startIndex; index < packets.Count; index++)
        {
            if (packets[index].Seq == seq)
            {
                return index;
            }
        }

        return -1;
    }

    private static async Task ResendWindowAsync(UdpClient udp, List<SrtpPacket> packets, int baseIndex, int nextIndex, SenderStats stats)
    {
        for (int index = baseIndex; index < nextIndex; index++)
        {
            await SendPacketAsync(udp, packets[index]);
            stats.Retransmissions++;
        }
    }

    private static async Task SendPacketAsync(UdpClient udp, SrtpPacket packet)
    {
        byte[] bytes = packet.ToBytes();
        await udp.SendAsync(bytes);
    }

    private static async Task<byte> HandshakeAsync(UdpClient udp, byte proposedWindow)
    {
        byte[] syn = PacketFactory.CreateSyn(proposedWindow).ToBytes();

        // A janela efetiva e o menor valor anunciado pelos dois lados.
        while (true)
        {
            await udp.SendAsync(syn);
            UdpReceiveResult? result = await ReceivePacketOrTimeoutAsync(udp);
            if (!result.HasValue)
            {
                continue;
            }

            UdpReceiveResult receive = result.Value;
            SrtpPacket? packet = SrtpPacket.Parse(receive.Buffer);
            if (packet == null || !packet.IsValidChecksum())
            {
                continue;
            }

            if (packet.Syn && packet.AckFlag && !packet.Nack && packet.Seq == 0 && packet.Ack == 0)
            {
                byte window = Math.Min(proposedWindow, packet.Length);
                Console.WriteLine($"Handshake concluido. Janela efetiva negociada: {window}.");
                byte[] ack = PacketFactory.CreateAck(0).ToBytes();
                await udp.SendAsync(ack);
                return window;
            }
        }
    }

    private static async Task CloseAsync(UdpClient udp, SenderStats stats)
    {
        byte[] fin = PacketFactory.CreateFin().ToBytes();
        bool firstSend = true;

        while (true)
        {
            await udp.SendAsync(fin);
            if (!firstSend)
            {
                stats.Retransmissions++;
            }
            firstSend = false;

            UdpReceiveResult? result = await ReceivePacketOrTimeoutAsync(udp);
            if (!result.HasValue)
            {
                continue;
            }

            UdpReceiveResult receive = result.Value;
            SrtpPacket? packet = SrtpPacket.Parse(receive.Buffer);
            if (packet == null || !packet.IsValidChecksum())
            {
                continue;
            }

            if (packet.Fin && packet.AckFlag && !packet.Nack)
            {
                return;
            }
        }
    }

    private static async Task<UdpReceiveResult?> ReceivePacketOrTimeoutAsync(UdpClient udp)
    {
        CancellationTokenSource timeoutCts = new CancellationTokenSource();
        try
        {
            timeoutCts.CancelAfter(Timeout);

            try
            {
                return await udp.ReceiveAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (SocketException)
            {
                // Ignora o erro de porta fechada/duplicada e trata como se fosse um timeout comum
                // para que o fluxo do Stop-and-Wait continue sem travar o programa
                return null;
            }
        }
        finally
        {
            timeoutCts.Dispose();
        }
    }
}
