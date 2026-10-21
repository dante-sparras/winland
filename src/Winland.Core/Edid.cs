using System.Globalization;

namespace Winland;

/// <summary>
/// Make, model and serial from an EDID block. The model is the monitor-name descriptor and the serial is
/// the serial-number descriptor. With no descriptor, the model is the product code and the serial is the
/// binary serial, each written as hex. A zero product code or binary serial is empty.
/// </summary>
internal readonly record struct Edid(string Make, string Model, string Serial)
{
    public static Edid Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 128 || !Header(bytes))
            return new Edid("", "", "");

        var make = Manufacturer((ushort)((bytes[8] << 8) | bytes[9]));
        var product = (ushort)(bytes[10] | (bytes[11] << 8));
        var serialNumber = (uint)(bytes[12] | (bytes[13] << 8) | (bytes[14] << 16) | (bytes[15] << 24));
        string? name = null;
        string? serialText = null;
        for (var offset = 54; offset <= 108; offset += 18)
        {
            var descriptor = bytes.Slice(offset, 18);
            if (descriptor[0] != 0 || descriptor[1] != 0 || descriptor[2] != 0)
                continue;

            if (descriptor[3] == 0xFC && name is null)
                name = Text(descriptor);
            else if (descriptor[3] == 0xFF && serialText is null)
                serialText = Text(descriptor);
        }

        var model = name is { Length: > 0 }
            ? name
            : product == 0 ? "" : "0x" + product.ToString("X4", CultureInfo.InvariantCulture);
        var serial = serialText is { Length: > 0 }
            ? serialText
            : serialNumber == 0 ? "" : "0x" + serialNumber.ToString("X8", CultureInfo.InvariantCulture);
        return new Edid(make, model, serial);
    }

    private static bool Header(ReadOnlySpan<byte> bytes) =>
        bytes[0] == 0x00
        && bytes[1] == 0xFF
        && bytes[2] == 0xFF
        && bytes[3] == 0xFF
        && bytes[4] == 0xFF
        && bytes[5] == 0xFF
        && bytes[6] == 0xFF
        && bytes[7] == 0x00;

    private static string Manufacturer(ushort id)
    {
        if (id == 0)
            return "";

        Span<char> letters = stackalloc char[3];
        letters[0] = (char)('@' + ((id >> 10) & 0x1F));
        letters[1] = (char)('@' + ((id >> 5) & 0x1F));
        letters[2] = (char)('@' + (id & 0x1F));
        if (letters[0] is < 'A' or > 'Z' || letters[1] is < 'A' or > 'Z' || letters[2] is < 'A' or > 'Z')
            return "";

        return new string(letters);
    }

    // A descriptor's text runs from byte 5 and stops at a newline or a zero. The rest is spaces.
    private static string Text(ReadOnlySpan<byte> descriptor)
    {
        Span<char> chars = stackalloc char[13];
        var count = 0;
        for (var i = 5; i < 18; i++)
        {
            var value = descriptor[i];
            if (value < 0x20)
                break;

            chars[count++] = (char)value;
        }

        return new string(chars[..count]).Trim();
    }
}
