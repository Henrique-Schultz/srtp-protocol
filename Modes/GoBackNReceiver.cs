using System.Net;
using System.Net.Sockets;
using Srtp.Protocol;
using Srtp.Stats;

namespace Srtp.Modes;

public sealed class GoBackNReceiver : ITransferMode
{
    public string Name => "gbn";

    public async Task ReceiveFileAsync(int port, string outputPath, byte proposedWindow, CancellationToken ct)
    {
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        using var udp = new UdpClient(port);
        Console.WriteLine($"Receiver escutando na porta {port}.");

        var sender = await HandshakeAsync(udp, proposedWindow, ct);
        Console.WriteLine($"Conexao estabelecida com {sender}.");

        var stats = new ReceiverStats();
        stats.Start();

        ushort expectedSeq = 0;
        ushort? lastAcceptedSeq = null;
        var eofReceived = false;

        await using var output = File.Create(outputPath);

        while (true)
        {
            var receive = await udp.ReceiveAsync(ct);
            if (!sender.Equals(receive.RemoteEndPoint))
            {
                continue;
            }

            if (!SrtpPacket.TryParse(receive.Buffer, out var packet) || packet is null)
            {
                stats.OutOfOrderPackets++;
                continue;
            }

            if (!packet.IsValidChecksum())
            {
                stats.InvalidCrcPackets++;
                continue;
            }

            if (packet.Fin)
            {
                if (eofReceived)
                {
                    await SendAsync(udp, PacketFactory.CreateFinAck(), sender, ct);
                    break;
                }

                stats.OutOfOrderPackets++;
                continue;
            }

            if (packet.Syn)
            {
                await SendAsync(udp, PacketFactory.CreateSynAck(proposedWindow), sender, ct);
                continue;
            }

            if (packet.AckFlag)
            {
                continue;
            }

            if (packet.Seq == expectedSeq)
            {
                await output.WriteAsync(packet.Payload, ct);
                await SendAsync(udp, PacketFactory.CreateAck(packet.Seq), sender, ct);

                stats.ApplicationBytes += packet.Length;
                stats.AcceptedPackets++;
                lastAcceptedSeq = packet.Seq;
                expectedSeq = SequenceNumber.NextSeq(expectedSeq);

                if (packet.Length < SrtpPacket.MaxPayloadLength)
                {
                    eofReceived = true;
                }

                continue;
            }

            if (lastAcceptedSeq.HasValue && packet.Seq == lastAcceptedSeq.Value)
            {
                stats.DuplicatePackets++;
                await SendAsync(udp, PacketFactory.CreateAck(packet.Seq), sender, ct);
                continue;
            }

            stats.OutOfOrderPackets++;
            await SendAsync(udp, PacketFactory.CreateNack(expectedSeq), sender, ct);
        }

        stats.Stop();
        stats.Print(outputPath);
    }

    private static async Task<IPEndPoint> HandshakeAsync(UdpClient udp, byte proposedWindow, CancellationToken ct)
    {
        IPEndPoint? sender = null;
        var synAck = PacketFactory.CreateSynAck(proposedWindow);

        while (true)
        {
            var receive = await udp.ReceiveAsync(ct);

            if (!SrtpPacket.TryParse(receive.Buffer, out var packet) || packet is null || !packet.IsValidChecksum())
            {
                continue;
            }

            if (packet.Syn && !packet.AckFlag && !packet.Fin)
            {
                sender = receive.RemoteEndPoint;
                await SendAsync(udp, synAck, sender, ct);
                break;
            }
        }

        while (true)
        {
            var receive = await udp.ReceiveAsync(ct);
            if (!sender.Equals(receive.RemoteEndPoint))
            {
                continue;
            }

            if (!SrtpPacket.TryParse(receive.Buffer, out var packet) || packet is null || !packet.IsValidChecksum())
            {
                continue;
            }

            if (packet.Syn && !packet.AckFlag && !packet.Fin)
            {
                await SendAsync(udp, synAck, sender, ct);
                continue;
            }

            if (packet.AckFlag && !packet.Syn && !packet.Fin && !packet.Nack)
            {
                return sender;
            }
        }
    }

    private static Task SendAsync(UdpClient udp, SrtpPacket packet, IPEndPoint endpoint, CancellationToken ct)
    {
        var bytes = packet.ToBytes();
        return udp.SendAsync(bytes, bytes.Length, endpoint).WaitAsync(ct);
    }
}
