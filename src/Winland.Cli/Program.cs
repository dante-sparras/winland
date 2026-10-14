using System.Text;
using System.Text.Json;
using Winland.Ipc;

namespace Winland.Cli;

/// <summary>Asks the running window manager. Like <c>hyprctl</c>, it starts, prints, and leaves.</summary>
public static class Program
{
    /// <summary>Runs one line.</summary>
    /// <param name="args">The words after <c>winland</c>.</param>
    /// <returns>0 when Winland answered. 1 when the line was refused or Winland is absent.</returns>
    public static int Main(string[] args)
    {
        var request = WinlandWords.Read(args);
        if (request.Word == WinlandWord.Help)
        {
            Console.WriteLine(WinlandWords.Usage);
            return 0;
        }

        if (request.Error.Length > 0)
        {
            Console.Error.WriteLine(request.Error);
            return 1;
        }

        using var client = WinlandClient.Start(BuildStamp.Value, Console.Error.WriteLine);
        var until = Environment.TickCount64 + 2000;
        while (!client.Connected && Environment.TickCount64 < until)
        {
            Thread.Sleep(20);
        }

        if (!client.Connected)
        {
            Console.Error.WriteLine("Winland is not running");
            return 1;
        }

        switch (request.Word)
        {
            case WinlandWord.Workspaces:
                return Print(client, "workspaces", request.Json);
            case WinlandWord.Clients:
                return Print(client, "clients", request.Json);
            case WinlandWord.ActiveWindow:
                return Print(client, "activewindow", request.Json);
            case WinlandWord.Dispatch:
                return Dispatch(client, request);
            default:
                Console.Error.WriteLine(WinlandWords.Usage);
                return 1;
        }
    }

    private static int Print(WinlandClient client, string name, bool json)
    {
        if (!client.TryQuery(name, json, out var body))
        {
            Console.Error.WriteLine("Winland is not running");
            return 1;
        }

        Console.WriteLine(body);
        return 0;
    }

    private static int Dispatch(WinlandClient client, WinlandRequest request)
    {
        if (!client.TryDispatch(request.Command, request.Args, out var error))
        {
            if (request.Json)
            {
                Console.WriteLine("{\"ok\":false,\"error\":" + Quoted(error) + "}");
            }
            else
            {
                Console.Error.WriteLine(error);
            }

            return 1;
        }

        Console.WriteLine(request.Json ? "{\"ok\":true}" : "ok");
        return 0;
    }

    private static string Quoted(string text)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStringValue(text);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
