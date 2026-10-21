using System.Globalization;

namespace Winland.Domain;

public readonly record struct WindowId(long Value)
{
    /// <summary>The window as the replies and selectors write it: <c>0x</c> and the handle in hexadecimal.</summary>
    public string Address => "0x" + Value.ToString("x", CultureInfo.InvariantCulture);
}
