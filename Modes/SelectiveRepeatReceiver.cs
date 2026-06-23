using System.Net;
using System.Net.Sockets;
using Srtp.Protocol;
using Srtp.Stats;

namespace Srtp.Modes;

public sealed class SelectiveRepeatReceiver : ITransferMode
{
    // Espaço de sequência SRTP: 14 bits → 0 a 16383
    private const int SeqSpace = 16384;

    public string Name => "sr";

    public async Task ReceiveFileAsync(int port, string outputPath, byte proposedWindow, CancellationToken ct)
    {
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        using var udp = new UdpClient(port);
        Console.WriteLine($"Receiver SR escutando na porta {port}.");

        var (sender, effectiveWindow) = await HandshakeAsync(udp, proposedWindow, ct);
        Console.WriteLine($"Conexao estabelecida com {sender}. Janela SR: {effectiveWindow}.");

        var stats = new ReceiverStats();
        stats.Start();

        ushort expectedSeq = 0;
        // Buffer de recepção: SR aceita e armazena pacotes fora de ordem dentro da janela
        var receiveBuffer = new Dictionary<ushort, SrtpPacket>();
        bool eofReceived = false;

        await using var output = File.Create(outputPath);

        while (true)
        {
            var receive = await udp.ReceiveAsync(ct);
            if (!sender.Equals(receive.RemoteEndPoint)) continue;

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

            // FIN: encerramento de conexão
            if (packet.Fin)
            {
                // Só responde quando todos os dados foram entregues
                if (eofReceived && receiveBuffer.Count == 0)
                {
                    await SendAsync(udp, PacketFactory.CreateFinAck(), sender, ct);
                    break;
                }
                // Dados ainda pendentes no buffer; o sender retransmitirá o FIN após timeout
                continue;
            }

            // SYN retransmitido durante handshake
            if (packet.Syn)
            {
                await SendAsync(udp, PacketFactory.CreateSynAck(proposedWindow), sender, ct);
                continue;
            }

            // Pacotes de controle puros (ACK do sender) — ignorar
            if (packet.AckFlag && !packet.Nack)
                continue;

            // Pacote de dados
            var seq = packet.Seq;

            // Verifica se está dentro da janela de recepção [expectedSeq, expectedSeq + effectiveWindow)
            if (!IsInReceiveWindow(seq, expectedSeq, effectiveWindow))
            {
                stats.OutOfOrderPackets++;
                // Re-ACK para o caso de ser duplicata de pacote já entregue (desobstrói o sender)
                await SendAsync(udp, PacketFactory.CreateAck(seq), sender, ct);
                continue;
            }

            // Bufferiza se ainda não recebido
            if (receiveBuffer.TryAdd(seq, packet))
            {
                stats.AcceptedPackets++;
                Console.WriteLine($"SR Receiver: bufferizando SEQ={seq}");
            }
            else
            {
                stats.DuplicatePackets++;
                Console.WriteLine($"SR Receiver: duplicata descartada SEQ={seq}");
            }

            // ACK individual para este pacote (mesmo que fora de ordem)
            // Diferença chave do SR: confirma cada pacote individualmente, não cumulativamente
            await SendAsync(udp, PacketFactory.CreateAck(seq), sender, ct);

            // Entrega pacotes consecutivos em ordem a partir do expectedSeq
            while (receiveBuffer.TryGetValue(expectedSeq, out var nextPacket))
            {
                await output.WriteAsync(nextPacket.Payload, ct);
                stats.ApplicationBytes += nextPacket.Length;
                receiveBuffer.Remove(expectedSeq);
                Console.WriteLine($"SR Receiver: entregando SEQ={expectedSeq}");

                if (nextPacket.Length < SrtpPacket.MaxPayloadLength)
                    eofReceived = true;

                expectedSeq = SequenceNumber.NextSeq(expectedSeq);
            }

            // Se ainda há lacuna (buffer não vazio), envia NACK para o pacote faltante
            // O sender SR retransmite apenas este pacote específico (não toda a janela)
            // Nota: PacketFactory.CreateNack deve criar: Nack=true, AckFlag=true, Ack=missingSeq
            if (receiveBuffer.Count > 0)
            {
                await SendAsync(udp, PacketFactory.CreateNack(expectedSeq), sender, ct);
                Console.WriteLine($"SR Receiver: NACK enviado para SEQ={expectedSeq} (lacuna detectada)");
            }
        }

        stats.Stop();
        stats.Print(outputPath);
    }

    private static async Task<(IPEndPoint Sender, byte EffectiveWindow)> HandshakeAsync(
        UdpClient udp, byte proposedWindow, CancellationToken ct)
    {
        IPEndPoint? sender = null;
        byte effectiveWindow = proposedWindow;
        SrtpPacket synAck = PacketFactory.CreateSynAck(proposedWindow);

        // Aguarda SYN do initiator
        while (true)
        {
            var receive = await udp.ReceiveAsync(ct);

            if (!SrtpPacket.TryParse(receive.Buffer, out var packet) || packet is null || !packet.IsValidChecksum())
                continue;

            if (packet.Syn && !packet.AckFlag && !packet.Fin)
            {
                sender = receive.RemoteEndPoint;
                effectiveWindow = Math.Min(proposedWindow, packet.Length);
                synAck = PacketFactory.CreateSynAck(proposedWindow);
                await SendAsync(udp, synAck, sender, ct);
                break;
            }
        }

        // Aguarda ACK final do three-way handshake
        while (true)
        {
            var receive = await udp.ReceiveAsync(ct);
            if (!sender!.Equals(receive.RemoteEndPoint)) continue;

            if (!SrtpPacket.TryParse(receive.Buffer, out var packet) || packet is null || !packet.IsValidChecksum())
                continue;

            // Retransmissão do SYN — responde novamente
            if (packet.Syn && !packet.AckFlag && !packet.Fin)
            {
                await SendAsync(udp, synAck, sender, ct);
                continue;
            }

            if (packet.AckFlag && !packet.Syn && !packet.Fin && !packet.Nack)
                return (sender, effectiveWindow);
        }
    }

    /// <summary>
    /// Verifica se <paramref name="seq"/> está dentro da janela de recepção
    /// [expectedSeq, expectedSeq + windowSize), com suporte a wrap-around de 14 bits.
    /// </summary>
    private static bool IsInReceiveWindow(ushort seq, ushort expectedSeq, byte windowSize)
    {
        // Distância "para frente" de expectedSeq até seq no espaço circular de 14 bits
        int distance = ((int)seq - (int)expectedSeq + SeqSpace) % SeqSpace;
        return distance < windowSize;
    }

    private static Task SendAsync(UdpClient udp, SrtpPacket packet, IPEndPoint endpoint, CancellationToken ct)
    {
        var bytes = packet.ToBytes();
        return udp.SendAsync(bytes, bytes.Length, endpoint).WaitAsync(ct);
    }
}
