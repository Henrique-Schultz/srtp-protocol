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

        var modeChoice = ReadRequired("Escolha o modo [1/2]: ");
        var listen = modeChoice == "1" ||
            modeChoice.Equals("receiver", StringComparison.OrdinalIgnoreCase) ||
            modeChoice.Equals("r", StringComparison.OrdinalIgnoreCase);

        if (!listen && modeChoice != "2" &&
            !modeChoice.Equals("sender", StringComparison.OrdinalIgnoreCase) &&
            !modeChoice.Equals("s", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Modo invalido. Use 1 para receiver ou 2 para sender.");
        }

        var port = ReadInt("Porta base [5000]: ", defaultValue: 5000);
        if (port is < 1 or > 65534)
        {
            throw new ArgumentException("A porta deve estar entre 1 e 65534.");
        }

        var window = ReadInt("Janela proposta [1]: ", defaultValue: 1);
        if (window is < 1 or > 255)
        {
            throw new ArgumentException("A janela deve estar entre 1 e 255.");
        }

        if (listen)
        {
            var outputPath = ReadRequired("Arquivo de saida [recebido.bin]: ", "recebido.bin");
            return new TerminalOptions
            {
                Listen = true,
                Port = port,
                OutputPath = outputPath,
                Window = (byte)window
            };
        }

        var host = ReadRequired("Host do receiver [127.0.0.1]: ", "127.0.0.1");
        var filePath = ReadRequired("Arquivo de entrada [entrada.bin]: ", "entrada.bin");
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
            Window = (byte)window
        };
    }

    private static string ReadRequired(string prompt, string? defaultValue = null)
    {
        Console.Write(prompt);
        var value = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!string.IsNullOrWhiteSpace(defaultValue))
            {
                return defaultValue;
            }

            throw new ArgumentException("Valor obrigatorio nao informado.");
        }

        return value.Trim();
    }

    private static int ReadInt(string prompt, int defaultValue)
    {
        Console.Write(prompt);
        var value = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value.Trim(), out var parsed)
            ? parsed
            : throw new ArgumentException($"Numero invalido: {value}");
    }
}
