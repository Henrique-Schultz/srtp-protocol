using System.Net;
using System.Net.Sockets;
using Srtp.Protocol;
using Srtp.Stats;

namespace Srtp.Modes;

public sealed class StopAndWaitReceiver : ITransferMode
{
    // Receiver do Stop-and-Wait:
    // - Mantem apenas o proximo SEQ esperado.
    // - Aceita e grava somente esse pacote.
    // - Responde com ACK do mesmo SEQ recebido.
    // - Duplicatas recebem ACK novamente, mas nao sao gravadas outra vez.
    public string Name => "saw";

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
            Console.WriteLine($"Receiver escutando na porta {port}.");

            IPEndPoint sender = await HandshakeAsync(udp, proposedWindow);
            Console.WriteLine($"Conexao estabelecida com {sender}.");

            ReceiverStats stats = new ReceiverStats();
            stats.Start();

            ushort expectedSeq = 0;
            ushort? lastAcceptedSeq = null;
            bool eofReceived = false;

            // O receiver so entrega dados ao arquivo quando o SEQ recebido e exatamente o esperado.
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
                        // No Stop-and-Wait, pacote corrompido e descartado em silencio.
                        // O sender recupera por timeout e retransmite o mesmo SEQ.
                        stats.InvalidCrcPackets++;
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
                        // Pacote correto: grava, confirma e passa a esperar o proximo numero de sequencia.
                        await output.WriteAsync(packet.Payload);
                        await SendAsync(udp, PacketFactory.CreateAck(packet.Seq), sender);

                        stats.ApplicationBytes += packet.Length;
                        stats.AcceptedPackets++;
                        lastAcceptedSeq = packet.Seq;
                        expectedSeq = SequenceNumber.NextSeq(expectedSeq);

                        if (packet.Length < SrtpPacket.MaxPayloadLength)
                        {
                            // Payload menor que 255 bytes indica o ultimo bloco do arquivo.
                            eofReceived = true;
                        }

                        continue;
                    }

                    if (lastAcceptedSeq.HasValue && packet.Seq == lastAcceptedSeq.Value)
                    {
                        // ACK perdido no caminho: o sender retransmite, entao reenviamos o ACK sem duplicar dados.
                        stats.DuplicatePackets++;
                        await SendAsync(udp, PacketFactory.CreateAck(packet.Seq), sender);
                        continue;
                    }

                    stats.OutOfOrderPackets++;
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

        // Primeiro recebe SYN e fixa o endpoint do sender aceito para esta sessao.
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

        // Depois aguarda o ACK final; se o SYN+ACK se perdeu, outro SYN faz o receiver reenviar.
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
