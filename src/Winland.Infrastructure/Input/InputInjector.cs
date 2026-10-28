using System.Collections.Concurrent;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Infrastructure;

/// <summary>
/// Sends input a hook made up, such as a key <c>kb_options</c> moved, the mask key or a wheel turn of button scrolling, from a thread of
/// its own. Input sent from inside a low-level hook has to pass that same hook, whose thread is still busy in the call, so
/// sending there can stall the hook and all input with it. The order of what is posted is kept.
/// </summary>
// Stryker disable once all : sends real input; what to send is decided and tested elsewhere.
internal static class InputInjector
{
    private static readonly BlockingCollection<INPUT[]> Pending = new();
    private static readonly Lock Gate = new();
    private static Thread? _worker;

    internal static void Post(params INPUT[] inputs)
    {
        if (inputs.Length == 0)
            return;

        lock (Gate)
        {
            if (_worker is null)
            {
                _worker = new Thread(Run) { IsBackground = true, Name = "Winland input injector" };
                _worker.Start();
            }
        }

        Pending.Add(inputs);
    }

    private static void Run()
    {
        foreach (var inputs in Pending.GetConsumingEnumerable())
        {
            try
            {
                KeyboardHook.Send(inputs);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine(exception);
            }
        }
    }
}
