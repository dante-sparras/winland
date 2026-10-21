using System.Globalization;

namespace Winland.Domain;

public sealed record Configure(ConfigDocument Document) : Command;
