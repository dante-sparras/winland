namespace Winland.Domain;

public sealed record ConfigRead(ConfigDocument? Document, ConfigWarning? Failure = null);
