using System.Globalization;
using static Winland.Domain.OrdinalText;

namespace Winland.Domain;

public readonly record struct GradientValue(BorderColor Color, double AngleDegrees, bool HasAngle, int Colors);
