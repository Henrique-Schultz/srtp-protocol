using Srtp.Modes;
using Srtp.Terminal;

try
{
    if (args.Length > 0)
    {
        Console.WriteLine("Esta versao usa entrada interativa. Execute apenas: dotnet run");
        return;
    }

    var options = TerminalOptions.Read();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    if (options.Listen)
    {
        var receiver = new StopAndWaitReceiver();
        await receiver.ReceiveFileAsync(options.Port, options.OutputPath!, options.Window, cts.Token);
    }
    else
    {
        var sender = new StopAndWaitSender();
        await sender.SendFileAsync(options.Host!, options.Port, options.FilePath!, options.Window, cts.Token);
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operacao cancelada.");
    Environment.ExitCode = 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Erro: {ex.Message}");
    Environment.ExitCode = 1;
}
