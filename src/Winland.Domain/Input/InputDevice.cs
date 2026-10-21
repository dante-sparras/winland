using System.Text;

namespace Winland.Domain;

/// <summary>
/// An input device as the system names it. <paramref name="Address"/> tells it apart from the others for as long as it
/// is plugged in, and <paramref name="Name"/> is its product name, which <see cref="InputDeviceNames"/> turns into the
/// name the replies give.
/// </summary>
public sealed record InputDevice(InputDeviceKind Kind, long Address, string Name);
