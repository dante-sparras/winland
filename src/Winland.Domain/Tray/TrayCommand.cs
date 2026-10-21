using System.Globalization;

namespace Winland.Domain;

/// <summary>One command on the notification-area icon's menu.</summary>
public enum TrayCommand
{
    Reload,
    EditConfig,
    OpenLog,
    Quit,
}
