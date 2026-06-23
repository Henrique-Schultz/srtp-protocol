using System.Net;
using System.Net.Sockets;
using Srtp.Protocol;
using Srtp.Stats;

namespace Srtp.Modes;

public sealed class SelectiveRepeatReceiver : ITransferMode
{
    public string Name => "sr";

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
            Console.WriteLine($"Receiver SR escutando na porta {port}.");

            IPEndPoint sender = await HandshakeAsync(udp, proposedWindow);
            Console.WriteLine($"Conexao estabelecida com {sender}.");

            ReceiverStats stats = new ReceiverStats();
            stats.Start();

            ushort expectedSeq = 0;
            bool eofReceived = false;
            Dictionary<ushort, SrtpPacket> buffer = new Dictionary<ushort, SrtpPacket>();

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
                        if (eofReceived && buffer.Count == 0)
                        {
                            await SendAsync(udp, PacketFactory.CreateFinAck(), sender);
                            break;
                        }

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

                    if (!IsInsideWindow(packet.Seq, expectedSeq, proposedWindow))
                    {
                        stats.OutOfOrderPackets++;
                        await SendAsync(udp, PacketFactory.CreateAck(packet.Seq), sender);
                        continue;
                    }

                    if (buffer.ContainsKey(packet.Seq))
                    {
                        stats.DuplicatePackets++;
                    }
                    else
                    {
                        buffer.Add(packet.Seq, packet);
                        stats.AcceptedPackets++;
                    }

                    await SendAsync(udp, PacketFactory.CreateAck(packet.Seq), sender);

                    while (buffer.ContainsKey(expectedSeq))
                    {
                        SrtpPacket nextPacket = buffer[expectedSeq];
                        await output.WriteAsync(nextPacket.Payload);
                        stats.ApplicationBytes += nextPacket.Length;
                        buffer.Remove(expectedSeq);

                        if (nextPacket.Length < SrtpPacket.MaxPayloadLength)
                        {
                            eofReceived = true;
                        }

                        expectedSeq = SequenceNumber.NextSeq(expectedSeq);
                    }

                    if (buffer.Count > 0)
                    {
                        await SendAsync(udp, PacketFactory.CreateNack(expectedSeq), sender);
                    }
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

    private static bool IsInsideWindow(ushort seq, ushort expectedSeq, byte window)
    {
        int sequenceSpace = SequenceNumber.MaxValue + 1;
        int distance = (seq - expectedSeq + sequenceSpace) % sequenceSpace;
        return distance < window;
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
