using Srtp.Modes;
using Srtp.Terminal;

TerminalOptions options = TerminalOptions.Read(args);

// Ponto de entrada: escolhe entre receiver e sender e depois seleciona o algoritmo confiavel.
// Os tres modos compartilham o mesmo formato de pacote SRTP, mas mudam a politica de ACK, janela e retransmissao.
if (options.Listen)
{
    if (options.Mode == "sr")
    {
        SelectiveRepeatReceiver receiver = new SelectiveRepeatReceiver();
        await receiver.ReceiveFileAsync(options.Port, options.OutputPath!, options.Window);
    }
    else if (options.Mode == "gbn")
    {
        GoBackNReceiver receiver = new GoBackNReceiver();
        await receiver.ReceiveFileAsync(options.Port, options.OutputPath!, options.Window);
    }
    else
    {
        StopAndWaitReceiver receiver = new StopAndWaitReceiver();
        await receiver.ReceiveFileAsync(options.Port, options.OutputPath!, options.Window);
    }
}
else
{
    if (options.Mode == "sr")
    {
        SelectiveRepeatSender sender = new SelectiveRepeatSender();
        await sender.SendFileAsync(options.Host!, options.Port, options.FilePath!, options.Window);
    }
    else if (options.Mode == "gbn")
    {
        GoBackNSender sender = new GoBackNSender();
        await sender.SendFileAsync(options.Host!, options.Port, options.FilePath!, options.Window);
    }
    else
    {
        StopAndWaitSender sender = new StopAndWaitSender();
        await sender.SendFileAsync(options.Host!, options.Port, options.FilePath!, options.Window);
    }
}
