
namespace Winland.Domain.Tests;

public class EdidTests
{
    [Fact]
    public void NameAndSerialDescriptorsAreTheModelAndSerial()
    {
        var bytes = Block();
        bytes[8] = 0x10;
        bytes[9] = 0xAC;
        bytes[10] = 0xC4;
        bytes[11] = 0x40;
        bytes[12] = 0x78;
        bytes[13] = 0x56;
        bytes[14] = 0x34;
        bytes[15] = 0x12;
        bytes[126] = 1;
        Descriptor(bytes, 54, 0xFC, "DELL U2723QE");
        Descriptor(bytes, 72, 0xFF, "ABC123");
        Descriptor(bytes, 90, 0xFC, "OTHER NAME");
        Finish(bytes);

        var edid = Edid.Parse(bytes);

        Assert.Equal("DEL", edid.Make);
        Assert.Equal("DELL U2723QE", edid.Model);
        Assert.Equal("ABC123", edid.Serial);
    }

    [Fact]
    public void ATimingDescriptorIsNotAName()
    {
        var bytes = Block();
        bytes[8] = 0x10;
        bytes[9] = 0xAC;
        bytes[54] = 0x01;
        bytes[57] = 0xFC;
        Descriptor(bytes, 72, 0xFC, "DELL U2723QE");
        Finish(bytes);

        Assert.Equal("DELL U2723QE", Edid.Parse(bytes).Model);
    }

    [Fact]
    public void TextStopsAtANewlineAndDropsThePadding()
    {
        var bytes = Block();
        bytes[8] = 0x10;
        bytes[9] = 0xAC;
        Descriptor(bytes, 54, 0xFC, "DELL U2723QE");
        bytes[59 + 4] = 0x0A;
        bytes[59 + 5] = (byte)'X';
        Finish(bytes);

        Assert.Equal("DELL", Edid.Parse(bytes).Model);
    }

    [Fact]
    public void ProductCodeAndBinarySerialFillInForMissingDescriptors()
    {
        var bytes = Block();
        bytes[8] = 0x4C;
        bytes[9] = 0x2D;
        bytes[10] = 0xC4;
        bytes[11] = 0x40;
        bytes[12] = 0x78;
        bytes[13] = 0x56;
        bytes[14] = 0x34;
        bytes[15] = 0x12;
        Finish(bytes);

        var edid = Edid.Parse(bytes);

        Assert.Equal("SAM", edid.Make);
        Assert.Equal("0x40C4", edid.Model);
        Assert.Equal("0x12345678", edid.Serial);
    }

    [Fact]
    public void AZeroProductCodeAndBinarySerialAreEmpty()
    {
        var bytes = Block();
        bytes[8] = 0x10;
        bytes[9] = 0xAC;
        Finish(bytes);

        var edid = Edid.Parse(bytes);

        Assert.Equal("DEL", edid.Make);
        Assert.Equal("", edid.Model);
        Assert.Equal("", edid.Serial);
    }

    [Fact]
    public void AShortOrHeaderlessBlockIsEmpty()
    {
        Assert.Equal(new Edid("", "", ""), Edid.Parse([]));
        var shortBlock = Block()[..127];
        Assert.Equal(new Edid("", "", ""), Edid.Parse(shortBlock));
        var broken = Block();
        broken[0] = 0x01;
        Finish(broken);
        Assert.Equal(new Edid("", "", ""), Edid.Parse(broken));
    }

    [Fact]
    public void LettersOutsideTheAlphabetAreNotAMake()
    {
        var bytes = Block();
        bytes[8] = 0x00;
        bytes[9] = 0x01;
        Descriptor(bytes, 54, 0xFC, "PANEL");
        Finish(bytes);

        var edid = Edid.Parse(bytes);

        Assert.Equal("", edid.Make);
        Assert.Equal("PANEL", edid.Model);
    }

    private static byte[] Block()
    {
        var bytes = new byte[128];
        bytes[0] = 0x00;
        bytes[1] = 0xFF;
        bytes[2] = 0xFF;
        bytes[3] = 0xFF;
        bytes[4] = 0xFF;
        bytes[5] = 0xFF;
        bytes[6] = 0xFF;
        bytes[7] = 0x00;
        bytes[18] = 1;
        bytes[19] = 4;
        return bytes;
    }

    private static void Descriptor(byte[] bytes, int offset, byte tag, string text)
    {
        bytes[offset + 3] = tag;
        var written = 0;
        foreach (var letter in text)
        {
            if (written == 13)
                break;

            bytes[offset + 5 + written] = (byte)letter;
            written++;
        }

        if (written < 13)
            bytes[offset + 5 + written] = 0x0A;
    }

    private static void Finish(byte[] bytes)
    {
        byte sum = 0;
        for (var i = 0; i < 127; i++)
            sum += bytes[i];

        bytes[127] = unchecked((byte)-sum);
    }
}
