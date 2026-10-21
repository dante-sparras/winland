namespace Winland.Domain;

/// <summary>Border colors blend in the OkLab space, so the middle of a change is as bright as its ends.</summary>
internal static class OkLab
{
    internal static double[] From(BorderColor color)
    {
        var r = Linear(color.R / 255.0);
        var g = Linear(color.G / 255.0);
        var b = Linear(color.B / 255.0);
        var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
        var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
        var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));
        return
        [
            (0.2104542553 * l) + (0.7936177850 * m) - (0.0040720468 * s),
            (1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s),
            (0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s),
            color.A,
        ];
    }

    internal static BorderColor To(double[] lab)
    {
        var l = lab[0] + (0.3963377774 * lab[1]) + (0.2158037573 * lab[2]);
        var m = lab[0] - (0.1055613458 * lab[1]) - (0.0638541728 * lab[2]);
        var s = lab[0] - (0.0894841775 * lab[1]) - (1.2914855480 * lab[2]);
        l = l * l * l;
        m = m * m * m;
        s = s * s * s;
        var r = (4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s);
        var g = (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s);
        var b = (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s);
        return new BorderColor(Byte(lab[3]), Byte(Gamma(r) * 255), Byte(Gamma(g) * 255), Byte(Gamma(b) * 255));
    }

    private static double Linear(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static double Gamma(double channel) =>
        channel <= 0.0031308 ? channel * 12.92 : (1.055 * Math.Pow(channel, 1 / 2.4)) - 0.055;

    private static byte Byte(double value) =>
        (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}
