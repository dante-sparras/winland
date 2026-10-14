namespace Winland;

public abstract record Input;

public abstract record Event : Input;

public abstract record Command : Input;

public readonly record struct WindowId(long Value);

public readonly record struct Rectangle(int X, int Y, int Width, int Height);

public sealed record WindowAppeared(WindowId Window, Rectangle WorkArea) : Event;
