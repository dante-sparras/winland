using System.Globalization;

namespace Winland.Domain;

public sealed record ConfigLoaded(ConfigDocument Config) : Command;
