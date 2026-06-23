using Srtp.Modes;
using Srtp.Terminal;

TerminalOptions options = TerminalOptions.Read();

if (options.Listen)
{
    StopAndWaitReceiver receiver = new StopAndWaitReceiver();
    await receiver.ReceiveFileAsync(options.Port, options.OutputPath!, options.Window);
}
else
{
    StopAndWaitSender sender = new StopAndWaitSender();
    await sender.SendFileAsync(options.Host!, options.Port, options.FilePath!, options.Window);
}
