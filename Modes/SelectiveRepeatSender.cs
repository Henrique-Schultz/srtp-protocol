using System.Net;
using System.Net.Sockets;
using Srtp.Protocol;
using Srtp.Stats;

namespace Srtp.Modes;

public sealed class SelectiveRepeatSender : ITransferMode
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    public string Name => "sr";

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

            Console.WriteLine($"Sender SR ligado na porta local {port + 1}; destino {receiver}.");
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
        bool[] acked = new bool[packets.Count];
        bool[] sent = new bool[packets.Count];
        DateTime[] sentAt = new DateTime[packets.Count];
        int baseIndex = 0;
        int nextIndex = 0;
        int ackedCount = 0;

        while (ackedCount < packets.Count)
        {
            while (nextIndex < packets.Count && nextIndex < baseIndex + window)
            {
                await SendPacketAsync(udp, packets[nextIndex]);
                sent[nextIndex] = true;
                sentAt[nextIndex] = DateTime.UtcNow;
                nextIndex++;
            }

            DateTime now = DateTime.UtcNow;
            for (int index = baseIndex; index < nextIndex; index++)
            {
                if (!acked[index] && sent[index] && now - sentAt[index] >= Timeout)
                {
                    await SendPacketAsync(udp, packets[index]);
                    sentAt[index] = DateTime.UtcNow;
                    stats.Retransmissions++;
                }
            }

            UdpReceiveResult? result = await ReceivePacketOrTimeoutAsync(udp);
            if (!result.HasValue)
            {
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
                int nackIndex = FindPacketIndex(packets, baseIndex, nextIndex, response.Ack);
                if (nackIndex >= 0 && !acked[nackIndex])
                {
                    await SendPacketAsync(udp, packets[nackIndex]);
                    sentAt[nackIndex] = DateTime.UtcNow;
                    stats.Retransmissions++;
                }

                continue;
            }

            int ackIndex = FindPacketIndex(packets, baseIndex, nextIndex, response.Ack);
            if (ackIndex >= 0 && !acked[ackIndex])
            {
                acked[ackIndex] = true;
                ackedCount++;

                while (baseIndex < packets.Count && acked[baseIndex])
                {
                    baseIndex++;
                }
            }
        }
    }

    private static int FindPacketIndex(List<SrtpPacket> packets, int startIndex, int endIndex, ushort seq)
    {
        for (int index = startIndex; index < endIndex; index++)
        {
            if (packets[index].Seq == seq)
            {
                return index;
            }
        }

        return -1;
    }

    private static async Task SendPacketAsync(UdpClient udp, SrtpPacket packet)
    {
        byte[] bytes = packet.ToBytes();
        await udp.SendAsync(bytes);
    }

    private static async Task<byte> HandshakeAsync(UdpClient udp, byte proposedWindow)
    {
        byte[] syn = PacketFactory.CreateSyn(proposedWindow).ToBytes();

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
