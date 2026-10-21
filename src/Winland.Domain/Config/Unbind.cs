namespace Winland.Domain;

public sealed record Unbind(Modifier Modifiers, string Key) : BindDirective;
