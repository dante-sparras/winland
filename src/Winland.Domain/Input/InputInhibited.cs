using System.Globalization;

namespace Winland.Domain;

public sealed record InputInhibited(bool Inhibited) : Event;
