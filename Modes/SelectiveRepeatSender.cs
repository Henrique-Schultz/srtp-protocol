using System.Net;
using System.Net.Sockets;
using Srtp.Protocol;
using Srtp.Stats;

namespace Srtp.Modes;

public sealed class SelectiveRepeatSender : ITransferMode
{
    private static readonly TimeSpan PacketTimeout = TimeSpan.FromMilliseconds(100);

    public string Name => "sr";

    public async Task SendFileAsync(string host, int port, string filePath, byte proposedWindow, CancellationToken ct)
    {
        var receiverAddresses = await Dns.GetHostAddressesAsync(host, ct);
        var receiverAddress = receiverAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? receiverAddresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Host do receiver nao foi resolvido.");
        var receiver = new IPEndPoint(receiverAddress, port);

        using var udp = new UdpClient(port + 1);
        udp.Connect(receiver);

        Console.WriteLine($"Sender SR ligado na porta local {port + 1}; destino {receiver}.");

        var windowSize = await HandshakeAsync(udp, proposedWindow, ct);
        Console.WriteLine($"Janela efetiva SR: {windowSize} pacotes.");

        var stats = new SenderStats();
        stats.Start();

        await SendWithSlidingWindowAsync(udp, filePath, windowSize, stats, ct);

        await CloseAsync(udp, stats, ct);
        stats.Stop();
        stats.Print(filePath);
    }

    private static async Task<byte> HandshakeAsync(UdpClient udp, byte proposedWindow, CancellationToken ct)
    {
        var syn = PacketFactory.CreateSyn(proposedWindow).ToBytes();

        while (true)
        {
            await udp.SendAsync(syn, ct);
            var result = await ReceiveWithTimeoutAsync(udp, PacketTimeout, ct);
            if (result is not { } receive)
                continue;

            if (!SrtpPacket.TryParse(receive.Buffer, out var packet) || packet is null || !packet.IsValidChecksum())
                continue;

            if (packet.Syn && packet.AckFlag && !packet.Nack && packet.Seq == 0 && packet.Ack == 0)
            {
                var effectiveWindow = Math.Min(proposedWindow, packet.Length);
                Console.WriteLine($"Handshake concluido. Janela efetiva negociada: {effectiveWindow}.");
                var ack = PacketFactory.CreateAck(0).ToBytes();
                await udp.SendAsync(ack, ct);
                return effectiveWindow;
            }
        }
    }

    private static async Task SendWithSlidingWindowAsync(
        UdpClient udp, string filePath, byte windowSize, SenderStats stats, CancellationToken ct)
    {
        // Carrega todos os pacotes do arquivo para gerenciar a janela deslizante
        var allPackets = await BuildPacketListAsync(filePath, ct);
        int total = allPackets.Count;
        if (total == 0) return;

        stats.ApplicationBytes = allPackets.Sum(p => p.Length);
        stats.OriginalDataPackets = total;

        var acked = new bool[total];
        var sentAt = new DateTime[total];
        int windowBase = 0;  // índice do pacote mais antigo não confirmado
        int nextToSend = 0;  // índice do próximo pacote a enviar pela primeira vez
        int ackedCount = 0;

        while (ackedCount < total)
        {
            // 1. Preenche a janela com novos pacotes não enviados
            while (nextToSend < total && nextToSend - windowBase < windowSize)
            {
                var pkt = allPackets[nextToSend];
                await udp.SendAsync(pkt.ToBytes(), ct);
                sentAt[nextToSend] = DateTime.UtcNow;
                Console.WriteLine($"SR Sender: enviando SEQ={pkt.Seq} ({nextToSend + 1}/{total})");
                nextToSend++;
            }

            // 2. Verifica timeouts e retransmite individualmente (diferença chave do SR vs GBN)
            var now = DateTime.UtcNow;
            var waitTime = PacketTimeout;

            for (int i = windowBase; i < nextToSend; i++)
            {
                if (acked[i]) continue;

                var elapsed = now - sentAt[i];
                if (elapsed >= PacketTimeout)
                {
                    // SR: retransmite APENAS este pacote, não toda a janela
                    await udp.SendAsync(allPackets[i].ToBytes(), ct);
                    sentAt[i] = DateTime.UtcNow;
                    stats.Retransmissions++;
                    Console.WriteLine($"SR Sender: timeout, retransmitindo SEQ={allPackets[i].Seq}");
                    waitTime = PacketTimeout;
                }
                else
                {
                    var remaining = PacketTimeout - elapsed;
                    if (remaining < waitTime) waitTime = remaining;
                }
            }

            // 3. Aguarda ACK ou NACK até o próximo timeout
            var result = await ReceiveWithTimeoutAsync(udp, waitTime, ct);
            if (result is not { } receive)
                continue;

            if (!SrtpPacket.TryParse(receive.Buffer, out var ack) || ack is null || !ack.IsValidChecksum())
                continue;

            if (!ack.AckFlag || ack.Syn || ack.Fin)
                continue;

            if (ack.Nack)
            {
                // NACK: retransmite apenas o pacote específico faltante
                int idx = FindIndexBySeq(allPackets, ack.Ack, windowBase, nextToSend);
                if (idx >= 0 && !acked[idx])
                {
                    await udp.SendAsync(allPackets[idx].ToBytes(), ct);
                    sentAt[idx] = DateTime.UtcNow;
                    stats.Retransmissions++;
                    Console.WriteLine($"SR Sender: NACK recebido para SEQ={ack.Ack}, retransmitindo");
                }
                continue;
            }

            // ACK individual: confirma apenas o pacote com o SEQ indicado (não cumulativo)
            int ackedIdx = FindIndexBySeq(allPackets, ack.Ack, windowBase, nextToSend);
            if (ackedIdx >= 0 && !acked[ackedIdx])
            {
                acked[ackedIdx] = true;
                ackedCount++;
                Console.WriteLine($"SR Sender: ACK recebido SEQ={ack.Ack} ({ackedCount}/{total})");

                // Avança a base da janela sobre pacotes consecutivos confirmados
                while (windowBase < total && acked[windowBase])
                    windowBase++;
            }
        }
    }

    private static int FindIndexBySeq(List<SrtpPacket> packets, ushort seq, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (packets[i].Seq == seq) return i;
        }
        return -1;
    }

    // Pré-carrega todos os pacotes do arquivo (mesma lógica do SAW, agrupada para a janela)
    private static async Task<List<SrtpPacket>> BuildPacketListAsync(string filePath, CancellationToken ct)
    {
        var packets = new List<SrtpPacket>();
        var buffer = new byte[SrtpPacket.MaxPayloadLength];
        ushort seq = 0;
        bool sentFinalZeroLength = false;

        await using var stream = File.OpenRead(filePath);

        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            var isEnd = read < SrtpPacket.MaxPayloadLength;

            if (read == 0 && stream.Position == stream.Length && sentFinalZeroLength)
                break;

            packets.Add(new SrtpPacket
            {
                Seq = seq,
                Length = (byte)read,
                Payload = buffer.AsSpan(0, read).ToArray()
            });

            seq = SequenceNumber.NextSeq(seq);

            if (isEnd) break;

            if (stream.Position == stream.Length)
            {
                sentFinalZeroLength = true;
                packets.Add(new SrtpPacket
                {
                    Seq = seq,
                    Length = 0,
                    Payload = Array.Empty<byte>()
                });
                break;
            }
        }

        return packets;
    }

    private static async Task CloseAsync(UdpClient udp, SenderStats stats, CancellationToken ct)
    {
        var fin = PacketFactory.CreateFin().ToBytes();
        var retransmitting = false;

        while (true)
        {
            await udp.SendAsync(fin, ct);
            if (retransmitting) stats.Retransmissions++;

            var result = await ReceiveWithTimeoutAsync(udp, PacketTimeout, ct);
            if (result is not { } receive)
            {
                retransmitting = true;
                continue;
            }

            if (!SrtpPacket.TryParse(receive.Buffer, out var packet) || packet is null || !packet.IsValidChecksum())
                continue;

            if (packet.Fin && packet.AckFlag && !packet.Nack)
                return;
        }
    }

    private static async Task<UdpReceiveResult?> ReceiveWithTimeoutAsync(UdpClient udp, TimeSpan timeout, CancellationToken ct)
    {
        if (timeout <= TimeSpan.Zero)
            return null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            return await udp.ReceiveAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
