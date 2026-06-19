namespace Srtp.Cli;

public sealed class CliOptions
{
    public bool Listen { get; private init; }
    public string? Host { get; private init; }
    public int Port { get; private init; }
    public string? FilePath { get; private init; }
    public string? OutputPath { get; private init; }
    public byte Window { get; private init; } = 1;
    public string Mode { get; private init; } = "saw";

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Nenhum argumento informado.");
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var listen = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--listen")
            {
                listen = true;
                continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Argumento inesperado: {arg}");
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Argumento {arg} precisa de valor.");
            }

            values[arg] = args[++i];
        }

        var port = ParseInt(values, "--port", required: true, defaultValue: 0);
        if (port is < 1 or > 65534)
        {
            throw new ArgumentException("A porta deve estar entre 1 e 65534.");
        }

        var window = ParseInt(values, "--window", required: false, defaultValue: 1);
        if (window is < 1 or > 255)
        {
            throw new ArgumentException("A janela deve estar entre 1 e 255.");
        }

        var mode = values.TryGetValue("--mode", out var modeValue) ? modeValue! : "saw";
        if (!string.Equals(mode, "saw", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Apenas o modo 'saw' esta implementado nesta parte.");
        }

        if (listen)
        {
            var outputPath = Require(values, "--out");
            return new CliOptions
            {
                Listen = true,
                Port = port,
                OutputPath = outputPath,
                Window = (byte)window,
                Mode = mode
            };
        }

        var host = Require(values, "--host");
        var filePath = Require(values, "--file");
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Arquivo de entrada nao encontrado.", filePath);
        }

        return new CliOptions
        {
            Listen = false,
            Host = host,
            Port = port,
            FilePath = filePath,
            Window = (byte)window,
            Mode = mode
        };
    }

    public static void PrintUsage()
    {
        Console.WriteLine("Uso:");
        Console.WriteLine("  Receiver: dotnet run -- --listen --port 5000 --out recebido.bin --window 1 --mode saw");
        Console.WriteLine("  Sender:   dotnet run -- --host 127.0.0.1 --port 5000 --file entrada.bin --window 1 --mode saw");
    }

    private static string Require(Dictionary<string, string?> values, string name)
    {
        if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Argumento obrigatorio ausente: {name}");
        }

        return value;
    }

    private static int ParseInt(Dictionary<string, string?> values, string name, bool required, int defaultValue)
    {
        if (!values.TryGetValue(name, out var value))
        {
            if (required)
            {
                throw new ArgumentException($"Argumento obrigatorio ausente: {name}");
            }

            return defaultValue;
        }

        return int.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"Valor invalido para {name}: {value}");
    }
}
