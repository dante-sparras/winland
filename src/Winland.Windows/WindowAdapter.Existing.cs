using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winland.Windows;

public sealed partial class WindowAdapter
{
    /// <summary>Adopts the windows that were already open before the event hooks started.</summary>
    public void AdoptExisting() => AdoptExisting(ExistingWindows());

    internal void AdoptExisting(IEnumerable<nint> windows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var handle in windows)
        {
            var hwnd = new HWND(handle);
            var window = new WindowId(handle);
            try
            {
                if (!TryDescribe(hwnd, window, out var appeared))
                    continue;

                var outcome = _core.Handle(appeared);
                NoteManaged(window, outcome, appeared);
                Apply(outcome);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
        }
    }

    /// <summary>Top-level windows that are shown and not minimized. The core decides which of them to manage.</summary>
    internal static IReadOnlyList<nint> ExistingWindows()
    {
        var found = new List<nint>();
        _ = PInvoke.EnumWindows((hwnd, _) =>
        {
            if (PInvoke.IsWindowVisible(hwnd) && !PInvoke.IsIconic(hwnd))
                found.Add((nint)hwnd);

            return true;
        }, default);
        return found;
    }
}
