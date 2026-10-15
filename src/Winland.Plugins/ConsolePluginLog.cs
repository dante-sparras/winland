namespace Winland.Plugins;

public sealed class ConsolePluginLog : IPluginLog
{
    public void Write(string message) => Console.Error.WriteLine(message);
}
