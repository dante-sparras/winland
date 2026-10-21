using System.Globalization;

namespace Winland.Domain;

public sealed record Preselect(string Direction) : Command;
