namespace Winland.Windows;

/// <summary>Asks the daemon's message loop to stop. The host installs <see cref="Stop"/> while it is pumping.</summary>
internal static class SessionHalt
{
    internal static Action? Stop { get; set; }

    internal static void Request() => Stop?.Invoke();
}
