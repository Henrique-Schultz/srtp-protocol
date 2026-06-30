using System.Net;
using System.Net.Sockets;
using Srtp.Protocol;
using Srtp.Stats;

namespace Srtp.Modes;

public sealed class StopAndWaitSender : ITransferMode
{
    // Stop-and-Wait:
    // - Envia exatamente um pacote de dados por vez.
    // - Depois de enviar, fica bloqueado esperando ACK com o mesmo SEQ do pacote.
    // - Se o ACK nao chega em 100 ms, retransmite o mesmo pacote.
    // - Por isso, sofre muito com latencia: a cada pacote existe um periodo ocioso esperando o ACK voltar.
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    public string Name => "saw";

    public async Task SendFileAsync(string host, int port, string filePath, byte proposedWindow)
    {
        // O sender usa a porta base + 1 para facilitar os testes locais com receiver e sender na mesma maquina.
        IPAddress[] receiverAddresses = await Dns.GetHostAddressesAsync(host);
        IPAddress receiverAddress = receiverAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? receiverAddresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Host do receiver nao foi resolvido.");
        IPEndPoint receiver = new IPEndPoint(receiverAddress, port);

        UdpClient udp = new UdpClient(port + 1);
        try
        {
            udp.Connect(receiver);

            Console.WriteLine($"Sender ligado na porta local {port + 1}; destino {receiver}.");
            await HandshakeAsync(udp, proposedWindow);

            SenderStats stats = new SenderStats();
            stats.Start();

            ushort seq = 0;
            FileStream stream = File.OpenRead(filePath);
            try
            {
                byte[] buffer = new byte[SrtpPacket.MaxPayloadLength];
                bool sentFinalZeroLength = false;

                while (true)
                {
                    // Stop-and-Wait so avanca para o proximo pacote depois do ACK do pacote atual.
                    // Nao existe janela de pacotes em voo: a janela efetiva e sempre 1.
                    int read = await stream.ReadAsync(buffer);
                    bool isEnd = read < SrtpPacket.MaxPayloadLength;

                    if (read == 0 && stream.Position == stream.Length && sentFinalZeroLength)
                    {
                        break;
                    }

                    byte[] payload = buffer.AsSpan(0, read).ToArray();
                    SrtpPacket packet = new SrtpPacket
                    {
                        Seq = seq,
                        Length = (byte)read,
                        Payload = payload
                    };

                    stats.ApplicationBytes += read;
                    stats.OriginalDataPackets++;
                    await SendWithAckAsync(udp, packet, stats);
                    seq = SequenceNumber.NextSeq(seq);

                    if (isEnd)
                    {
                        break;
                    }

                    if (stream.Position == stream.Length)
                    {
                        // Se o arquivo termina exatamente no limite de 255 bytes, envia um pacote vazio como EOF.
                        sentFinalZeroLength = true;
                        SrtpPacket finalPacket = new SrtpPacket
                        {
                            Seq = seq,
                            Length = 0,
                            Payload = Array.Empty<byte>()
                        };

                        stats.OriginalDataPackets++;
                        await SendWithAckAsync(udp, finalPacket, stats);
                        break;
                    }
                }
            }
            finally
            {
                stream.Dispose();
            }

            await CloseAsync(udp, stats);
            stats.Stop();
            stats.Print(filePath);
        }
        finally
        {
            udp.Dispose();
        }
    }

    private static async Task HandshakeAsync(UdpClient udp, byte proposedWindow)
    {
        byte[] syn = PacketFactory.CreateSyn(proposedWindow).ToBytes();

        // Three-way handshake: SYN, SYN+ACK e ACK final.
        // Mesmo que uma janela maior seja negociada, o Stop-and-Wait usa apenas 1 pacote em voo.
        while (true)
        {
            await udp.SendAsync(syn);
            UdpReceiveResult? result = await ReceivePacketOrTimeoutAsync(udp, Timeout);
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
                int effectiveWindow = Math.Min(proposedWindow, packet.Length);
                Console.WriteLine($"Handshake concluido. Janela efetiva negociada: {effectiveWindow}; Stop-and-Wait usa 1.");
                byte[] ack = PacketFactory.CreateAck(0).ToBytes();
                await udp.SendAsync(ack);
                return;
            }
        }
    }

    private static async Task SendWithAckAsync(UdpClient udp, SrtpPacket packet, SenderStats stats)
    {
        byte[] bytes = packet.ToBytes();

        // Sem ACK dentro do timeout fixo de 100 ms, o mesmo pacote e retransmitido.
        // O ACK esperado precisa carregar o mesmo SEQ enviado no pacote de dados.
        while (true)
        {
            await udp.SendAsync(bytes);

            bool ackReceived = await WaitForAckAsync(udp, packet.Seq, Timeout);
            if (ackReceived)
            {
                return;
            }

            stats.Retransmissions++;
        }
    }

    private static async Task CloseAsync(UdpClient udp, SenderStats stats)
    {
        byte[] fin = PacketFactory.CreateFin().ToBytes();

        // O FIN tambem e confiavel: repete ate receber FIN+ACK.
        while (true)
        {
            await udp.SendAsync(fin);

            bool finAckReceived = await WaitForFinAckAsync(udp, Timeout);
            if (finAckReceived)
            {
                return;
            }

            stats.Retransmissions++;
        }
    }

    private static async Task<bool> WaitForAckAsync(UdpClient udp, ushort expectedSeq, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            UdpReceiveResult? result = await ReceivePacketOrTimeoutAsync(udp, remaining);
            if (!result.HasValue)
            {
                return false;
            }

            UdpReceiveResult receive = result.Value;
            SrtpPacket? ack = SrtpPacket.Parse(receive.Buffer);
            if (ack == null || !ack.IsValidChecksum())
            {
                continue;
            }

            if (ack.AckFlag && !ack.Nack && !ack.Syn && !ack.Fin && ack.Ack == expectedSeq)
            {
                // ACK do mesmo SEQ confirma que aquele pacote especifico chegou corretamente.
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForFinAckAsync(UdpClient udp, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            UdpReceiveResult? result = await ReceivePacketOrTimeoutAsync(udp, remaining);
            if (!result.HasValue)
            {
                return false;
            }

            UdpReceiveResult receive = result.Value;
            SrtpPacket? packet = SrtpPacket.Parse(receive.Buffer);
            if (packet == null || !packet.IsValidChecksum())
            {
                continue;
            }

            if (packet.Fin && packet.AckFlag && !packet.Nack)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<UdpReceiveResult?> ReceivePacketOrTimeoutAsync(UdpClient udp, TimeSpan timeout)
    {
        CancellationTokenSource timeoutCts = new CancellationTokenSource();
        try
        {
            timeoutCts.CancelAfter(timeout);

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
