namespace Winland.Domain;

internal readonly record struct ConfigDirective(ConfigDirectiveKind Kind, string Argument, bool Negated, bool Enabled);
