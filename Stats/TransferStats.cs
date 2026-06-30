using System.Diagnostics;

namespace Srtp.Stats;

public sealed class SenderStats
{
    private readonly Stopwatch _stopwatch = new();

    // Metricas usadas no relatorio para comparar os algoritmos.
    // SAW tende a perder throughput com latencia; GBN tende a acumular retransmissoes com perda/reordenacao;
    // SR normalmente preserva melhor o throughput porque retransmite pontualmente.
    public long ApplicationBytes { get; set; }
    public int OriginalDataPackets { get; set; }
    public int Retransmissions { get; set; }

    public void Start() => _stopwatch.Start();
    public void Stop() => _stopwatch.Stop();

    public void Print(string filePath)
    {
        long elapsedMs = Math.Max(1, _stopwatch.ElapsedMilliseconds);
        double bytesPerSecond = ApplicationBytes * 1000.0 / elapsedMs;
        double kbps = bytesPerSecond * 8.0 / 1000.0;

        Console.WriteLine($"Arquivo enviado: {filePath}");
        Console.WriteLine($"Bytes enviados de aplicacao: {ApplicationBytes}");
        Console.WriteLine($"Pacotes de dados originais: {OriginalDataPackets}");
        Console.WriteLine($"Retransmissoes: {Retransmissions}");
        Console.WriteLine($"Tempo total: {elapsedMs} ms");
        Console.WriteLine($"Throughput: {bytesPerSecond:F2} bytes/s");
        Console.WriteLine($"Throughput: {kbps:F2} kbps");
    }
}

public sealed class ReceiverStats
{
    private readonly Stopwatch _stopwatch = new();

    // No receiver, esses contadores mostram como a rede afetou a entrega:
    // CRC invalido indica corrupcao, fora de ordem aparece muito em GBN, e duplicatas surgem apos retransmissoes.
    public long ApplicationBytes { get; set; }
    public int AcceptedPackets { get; set; }
    public int InvalidCrcPackets { get; set; }
    public int OutOfOrderPackets { get; set; }
    public int DuplicatePackets { get; set; }

    public void Start() => _stopwatch.Start();
    public void Stop() => _stopwatch.Stop();

    public void Print(string outputPath)
    {
        long elapsedMs = Math.Max(1, _stopwatch.ElapsedMilliseconds);

        Console.WriteLine($"Arquivo recebido: {outputPath}");
        Console.WriteLine($"Bytes recebidos de aplicacao: {ApplicationBytes}");
        Console.WriteLine($"Pacotes aceitos: {AcceptedPackets}");
        Console.WriteLine($"Pacotes descartados por CRC invalido: {InvalidCrcPackets}");
        Console.WriteLine($"Pacotes fora de ordem descartados: {OutOfOrderPackets}");
        Console.WriteLine($"Duplicatas recebidas: {DuplicatePackets}");
        Console.WriteLine($"Tempo total: {elapsedMs} ms");
    }
}
