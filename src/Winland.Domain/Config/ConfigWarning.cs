namespace Winland.Domain;

public sealed record ConfigWarning(int Line, string Message, string? Source = null);
