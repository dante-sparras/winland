namespace Winland.Domain;

/// <summary>
/// A window that is no Client, made transparent while it was created hidden until it is shown. Its process and class
/// tell it from a window that took its handle since.
/// </summary>
public sealed record HeldWindow(WindowId Window, string Class, long ProcessId);
