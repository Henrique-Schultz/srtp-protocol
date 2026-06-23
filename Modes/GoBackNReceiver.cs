using System.Net;
using System.Net.Sockets;
using Srtp.Protocol;
using Srtp.Stats;

namespace Srtp.Modes;

public sealed class GoBackNReceiver : ITransferMode
{
    public string Name => "gbn";

    public async Task ReceiveFileAsync(int port, string outputPath, byte proposedWindow)
    {
        string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        UdpClient udp = new UdpClient(port);
        try
        {
            Console.WriteLine($"Receiver GBN escutando na porta {port}.");

            IPEndPoint sender = await HandshakeAsync(udp, proposedWindow);
            Console.WriteLine($"Conexao estabelecida com {sender}.");

            ReceiverStats stats = new ReceiverStats();
            stats.Start();

            ushort expectedSeq = 0;
            ushort? lastAcceptedSeq = null;
            bool eofReceived = false;

            FileStream output = File.Create(outputPath);
            try
            {
                while (true)
                {
                    UdpReceiveResult receive = await udp.ReceiveAsync();
                    if (!sender.Equals(receive.RemoteEndPoint))
                    {
                        continue;
                    }

                    SrtpPacket? packet = SrtpPacket.Parse(receive.Buffer);
                    if (packet == null)
                    {
                        stats.OutOfOrderPackets++;
                        continue;
                    }

                    if (!packet.IsValidChecksum())
                    {
                        stats.InvalidCrcPackets++;
                        await SendAsync(udp, PacketFactory.CreateNack(expectedSeq), sender);
                        continue;
                    }

                    if (packet.Fin)
                    {
                        if (eofReceived)
                        {
                            await SendAsync(udp, PacketFactory.CreateFinAck(), sender);
                            break;
                        }

                        stats.OutOfOrderPackets++;
                        await SendAsync(udp, PacketFactory.CreateNack(expectedSeq), sender);
                        continue;
                    }

                    if (packet.Syn)
                    {
                        await SendAsync(udp, PacketFactory.CreateSynAck(proposedWindow), sender);
                        continue;
                    }

                    if (packet.AckFlag)
                    {
                        continue;
                    }

                    if (packet.Seq == expectedSeq)
                    {
                        await output.WriteAsync(packet.Payload);
                        await SendAsync(udp, PacketFactory.CreateAck(packet.Seq), sender);

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
                        await SendAsync(udp, PacketFactory.CreateAck(packet.Seq), sender);
                        continue;
                    }

                    stats.OutOfOrderPackets++;
                    await SendAsync(udp, PacketFactory.CreateNack(expectedSeq), sender);
                }
            }
            finally
            {
                output.Dispose();
            }

            stats.Stop();
            stats.Print(outputPath);
        }
        finally
        {
            udp.Dispose();
        }
    }

    private static async Task<IPEndPoint> HandshakeAsync(UdpClient udp, byte proposedWindow)
    {
        IPEndPoint? sender = null;
        SrtpPacket synAck = PacketFactory.CreateSynAck(proposedWindow);

        while (true)
        {
            UdpReceiveResult receive = await udp.ReceiveAsync();

            SrtpPacket? packet = SrtpPacket.Parse(receive.Buffer);
            if (packet == null || !packet.IsValidChecksum())
            {
                continue;
            }

            if (packet.Syn && !packet.AckFlag && !packet.Fin)
            {
                sender = receive.RemoteEndPoint;
                await SendAsync(udp, synAck, sender);
                break;
            }
        }

        while (true)
        {
            UdpReceiveResult receive = await udp.ReceiveAsync();
            if (!sender.Equals(receive.RemoteEndPoint))
            {
                continue;
            }

            SrtpPacket? packet = SrtpPacket.Parse(receive.Buffer);
            if (packet == null || !packet.IsValidChecksum())
            {
                continue;
            }

            if (packet.Syn && !packet.AckFlag && !packet.Fin)
            {
                await SendAsync(udp, synAck, sender);
                continue;
            }

            if (packet.AckFlag && !packet.Syn && !packet.Fin && !packet.Nack)
            {
                return sender;
            }
        }
    }

    private static Task SendAsync(UdpClient udp, SrtpPacket packet, IPEndPoint endpoint)
    {
        byte[] bytes = packet.ToBytes();
        return udp.SendAsync(bytes, bytes.Length, endpoint);
    }
}
