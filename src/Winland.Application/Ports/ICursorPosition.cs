namespace Winland.Application;

/// <summary>Where the pointer is. The Windows adapter reads the system cursor; tests supply a fixed point.</summary>
public interface ICursorPosition
{
    Point Read();
}
