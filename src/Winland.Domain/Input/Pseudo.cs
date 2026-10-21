using System.Globalization;

namespace Winland.Domain;

public sealed record Pseudo(WindowId Window) : Command;
