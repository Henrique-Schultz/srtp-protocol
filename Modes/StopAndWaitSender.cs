using System.Net;
using System.Net.Sockets;
using Srtp.Protocol;
using Srtp.Stats;

namespace Srtp.Modes;

public sealed class StopAndWaitSender : ITransferMode
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    public string Name => "saw";

    public async Task SendFileAsync(string host, int port, string filePath, byte proposedWindow, CancellationToken ct)
    {
        var receiverAddresses = await Dns.GetHostAddressesAsync(host, ct);
        var receiverAddress = receiverAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? receiverAddresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Host do receiver nao foi resolvido.");
        var receiver = new IPEndPoint(receiverAddress, port);

        using var udp = new UdpClient(port + 1);
        udp.Connect(receiver);

        Console.WriteLine($"Sender ligado na porta local {port + 1}; destino {receiver}.");
        await HandshakeAsync(udp, proposedWindow, ct);

        var stats = new SenderStats();
        stats.Start();

        ushort seq = 0;
        await using var stream = File.OpenRead(filePath);
        var buffer = new byte[SrtpPacket.MaxPayloadLength];
        var sentFinalZeroLength = false;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            var isEnd = read < SrtpPacket.MaxPayloadLength;

            if (read == 0 && stream.Position == stream.Length && sentFinalZeroLength)
            {
                break;
            }

            var payload = buffer.AsSpan(0, read).ToArray();
            var packet = new SrtpPacket
            {
                Seq = seq,
                Length = (byte)read,
                Payload = payload
            };

            stats.ApplicationBytes += read;
            stats.OriginalDataPackets++;
            await SendWithAckAsync(udp, packet, stats, ct);
            seq = SequenceNumber.NextSeq(seq);

            if (isEnd)
            {
                break;
            }

            if (stream.Position == stream.Length)
            {
                sentFinalZeroLength = true;
                var finalPacket = new SrtpPacket
                {
                    Seq = seq,
                    Length = 0,
                    Payload = Array.Empty<byte>()
                };

                stats.OriginalDataPackets++;
                await SendWithAckAsync(udp, finalPacket, stats, ct);
                break;
            }
        }

        await CloseAsync(udp, stats, ct);
        stats.Stop();
        stats.Print(filePath);
    }

    private static async Task HandshakeAsync(UdpClient udp, byte proposedWindow, CancellationToken ct)
    {
        var syn = PacketFactory.CreateSyn(proposedWindow).ToBytes();

        while (true)
        {
            await udp.SendAsync(syn, ct);
            var result = await ReceivePacketOrTimeoutAsync(udp, ct);
            if (result is not { } receive)
            {
                continue;
            }

            if (!SrtpPacket.TryParse(receive.Buffer, out var packet) || packet is null || !packet.IsValidChecksum())
            {
                continue;
            }

            if (packet.Syn && packet.AckFlag && !packet.Nack && packet.Seq == 0 && packet.Ack == 0)
            {
                var effectiveWindow = Math.Min(proposedWindow, packet.Length);
                Console.WriteLine($"Handshake concluido. Janela efetiva negociada: {effectiveWindow}; Stop-and-Wait usa 1.");
                var ack = PacketFactory.CreateAck(0).ToBytes();
                await udp.SendAsync(ack, ct);
                return;
            }
        }
    }

    private static async Task SendWithAckAsync(UdpClient udp, SrtpPacket packet, SenderStats stats, CancellationToken ct)
    {
        var bytes = packet.ToBytes();
        var retransmitting = false;

        while (true)
        {
            await udp.SendAsync(bytes, ct);
            if (retransmitting)
            {
                stats.Retransmissions++;
            }

            var result = await ReceivePacketOrTimeoutAsync(udp, ct);
            if (result is not { } receive)
            {
                retransmitting = true;
                continue;
            }

            if (!SrtpPacket.TryParse(receive.Buffer, out var ack) || ack is null || !ack.IsValidChecksum())
            {
                continue;
            }

            if (ack.AckFlag && !ack.Nack && !ack.Syn && !ack.Fin && ack.Ack == packet.Seq)
            {
                return;
            }
        }
    }

    private static async Task CloseAsync(UdpClient udp, SenderStats stats, CancellationToken ct)
    {
        var fin = PacketFactory.CreateFin().ToBytes();
        var retransmitting = false;

        while (true)
        {
            await udp.SendAsync(fin, ct);
            if (retransmitting)
            {
                stats.Retransmissions++;
            }

            var result = await ReceivePacketOrTimeoutAsync(udp, ct);
            if (result is not { } receive)
            {
                retransmitting = true;
                continue;
            }

            if (!SrtpPacket.TryParse(receive.Buffer, out var packet) || packet is null || !packet.IsValidChecksum())
            {
                continue;
            }

            if (packet.Fin && packet.AckFlag && !packet.Nack)
            {
                return;
            }
        }
    }

    private static async Task<UdpReceiveResult?> ReceivePacketOrTimeoutAsync(UdpClient udp, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);

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
