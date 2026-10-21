namespace Winland.Application;

/// <summary>The keyboards and pointers plugged in. The Windows adapter reads them from the system; tests supply their own.</summary>
public interface IInputDevices
{
    IReadOnlyList<InputDevice> Read();
}
