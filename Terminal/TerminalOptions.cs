namespace Srtp.Terminal;

public sealed class TerminalOptions
{
    public bool Listen { get; private init; }
    public string? Host { get; private init; }
    public int Port { get; private init; }
    public string? FilePath { get; private init; }
    public string? OutputPath { get; private init; }
    public byte Window { get; private init; } = 1;
    public string Mode { get; private init; } = "saw";

    public static TerminalOptions Read()
    {
        Console.WriteLine("SRTP - Simple Reliable Transport Protocol");
        Console.WriteLine("Modo de execucao:");
        Console.WriteLine("1 - Receiver");
        Console.WriteLine("2 - Sender");

        Console.Write("Escolha o modo [1/2]: ");
        string? modeChoice = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(modeChoice))
        {
            throw new ArgumentException("Valor obrigatorio nao informado.");
        }

        modeChoice = modeChoice.Trim();
        bool listen = modeChoice == "1";

        if (!listen && modeChoice != "2")
        {
            throw new ArgumentException("Modo invalido. Use 1 para receiver ou 2 para sender.");
        }

        Console.WriteLine("Protocolo:");
        Console.WriteLine("1 - Stop-and-Wait");
        Console.WriteLine("2 - Go-Back-N");
        Console.Write("Escolha o protocolo [1/2]: ");
        string? protocolChoice = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(protocolChoice))
        {
            protocolChoice = "1";
        }

        protocolChoice = protocolChoice.Trim();
        string protocolMode = protocolChoice == "2" ? "gbn" : "saw";
        if (protocolChoice != "1" && protocolChoice != "2")
        {
            throw new ArgumentException("Protocolo invalido. Use 1 para Stop-and-Wait ou 2 para Go-Back-N.");
        }

        Console.Write("Porta base [5000]: ");
        string? portText = Console.ReadLine();
        int port = string.IsNullOrWhiteSpace(portText)
            ? 5000
            : int.Parse(portText.Trim());

        if (port is < 1 or > 65534)
        {
            throw new ArgumentException("A porta deve estar entre 1 e 65534.");
        }

        Console.Write("Janela proposta [1]: ");
        string? windowText = Console.ReadLine();
        int window = string.IsNullOrWhiteSpace(windowText)
            ? 1
            : int.Parse(windowText.Trim());

        if (window is < 1 or > 255)
        {
            throw new ArgumentException("A janela deve estar entre 1 e 255.");
        }

        if (listen)
        {
            Console.Write("Arquivo de saida [recebido.bin]: ");
            string? outputPath = Console.ReadLine();
            outputPath = string.IsNullOrWhiteSpace(outputPath)
                ? "recebido.bin"
                : outputPath.Trim();

            return new TerminalOptions
            {
                Listen = true,
                Port = port,
                OutputPath = outputPath,
                Window = (byte)window,
                Mode = protocolMode
            };
        }

        Console.Write("Host do receiver [127.0.0.1]: ");
        string? host = Console.ReadLine();
        host = string.IsNullOrWhiteSpace(host)
            ? "127.0.0.1"
            : host.Trim();

        Console.Write("Arquivo de entrada [entrada.bin]: ");
        string? filePath = Console.ReadLine();
        filePath = string.IsNullOrWhiteSpace(filePath)
            ? "entrada.bin"
            : filePath.Trim();

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Arquivo de entrada nao encontrado.", filePath);
        }

        return new TerminalOptions
        {
            Listen = false,
            Host = host,
            Port = port,
            FilePath = filePath,
            Window = (byte)window,
            Mode = protocolMode
        };
    }

}
