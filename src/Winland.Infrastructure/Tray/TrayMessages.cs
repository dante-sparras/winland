
namespace Winland.Infrastructure;

/// <summary>The window messages the notification-area icon has to receive from Explorer.</summary>
internal static class TrayMessages
{
    /// <summary><c>WM_APP</c> plus a private offset. Explorer delivers a click as this message.</summary>
    internal const uint Callback = 0x8000 + 0x71;

    /// <summary>
    /// The callback, and <paramref name="taskbarCreated"/> when that broadcast was registered.
    /// Explorer is not elevated, so an elevated window drops both unless its message filter allows them.
    /// </summary>
    internal static uint[] Allowed(uint taskbarCreated) =>
        taskbarCreated == 0 ? [Callback] : [Callback, taskbarCreated];
}
