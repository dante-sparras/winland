using Winland;

namespace Winland.Windows.Tests;

public class DisplayColorSessionTests
{
    private const string Device = @"\\.\DISPLAY1";

    [Fact]
    public void TenBitTurnsWideColorAndRemembersWhatItReplaced()
    {
        var colors = new FakeColor();
        colors.Readings[Device] = Split(hdr: false, wide: false);
        var session = new DisplayColorSession(colors, _ => { });

        session.Apply([new DisplayColorRequest(Device, "auto", TenBit: true)]);
        session.Apply([new DisplayColorRequest(Device, "auto", TenBit: true)]);

        Assert.Equal([new DisplayColorChange(null, true)], colors.Sets);
        Assert.Equal(new DisplayColorOriginal(Device, false, false, true), Assert.Single(session.Originals));
        Assert.True(colors.Readings[Device].WideOn);
    }

    [Fact]
    public void ASwitchAlreadyShowingTheRuleIsNotRemembered()
    {
        var colors = new FakeColor();
        colors.Readings[Device] = Split(hdr: true, wide: false);
        var session = new DisplayColorSession(colors, _ => { });

        session.Apply([new DisplayColorRequest(Device, "hdr")]);

        Assert.Empty(colors.Sets);
        Assert.Empty(session.Originals);
    }

    [Fact]
    public void RestorePutsBackWhatTheRuleTurnedNewestFirst()
    {
        var colors = new FakeColor();
        colors.Readings[Device] = Split(hdr: false, wide: false);
        colors.Readings[@"\\.\DISPLAY2"] = Split(hdr: true, wide: true);
        var session = new DisplayColorSession(colors, _ => { });
        session.Apply(
        [
            new DisplayColorRequest(Device, "hdr"),
            new DisplayColorRequest(@"\\.\DISPLAY2", "srgb"),
        ]);

        session.Restore();

        Assert.Equal(
            [
                new DisplayColorChange(true, null),
                new DisplayColorChange(false, false),
                new DisplayColorChange(true, true),
                new DisplayColorChange(false, false),
            ],
            colors.Sets);
        Assert.Empty(session.Originals);
        Assert.False(colors.Readings[Device].HdrOn);
        Assert.True(colors.Readings[@"\\.\DISPLAY2"].HdrOn);
    }

    [Fact]
    public void ARuleThatNoLongerAsksPutsTheDisplayBack()
    {
        var colors = new FakeColor();
        colors.Readings[Device] = Split(hdr: false, wide: false);
        var session = new DisplayColorSession(colors, _ => { });
        session.Apply([new DisplayColorRequest(Device, "hdr")]);

        session.Apply([]);

        Assert.False(colors.Readings[Device].HdrOn);
        Assert.Empty(session.Originals);
    }

    [Fact]
    public void AskingForTheColorFirstFoundDropsTheRecord()
    {
        var colors = new FakeColor();
        colors.Readings[Device] = Split(hdr: true, wide: false);
        var session = new DisplayColorSession(colors, _ => { });
        session.Apply([new DisplayColorRequest(Device, "srgb")]);

        session.Apply([new DisplayColorRequest(Device, "hdr")]);

        Assert.True(colors.Readings[Device].HdrOn);
        Assert.Empty(session.Originals);
    }

    [Fact]
    public void TenBitWithStandardColorIsSaidOnceAndStillTurnsTheSwitchesOff()
    {
        var logged = new List<string>();
        var colors = new FakeColor();
        colors.Readings[Device] = Split(hdr: true, wide: true);
        var session = new DisplayColorSession(colors, logged.Add);
        var request = new DisplayColorRequest(Device, "srgb", TenBit: true);

        session.Apply([request]);
        session.Apply([request]);

        Assert.Equal(
            [@"The display \\.\DISPLAY1 cannot show 10-bit color while cm srgb is set, because Windows only raises bit depth with HDR or wide color."],
            logged);
        Assert.False(colors.Readings[Device].HdrOn);
        Assert.False(colors.Readings[Device].WideOn);
    }

    [Fact]
    public void ARefusalIsSaidAgainOnlyAfterTheRuleStopsNamingTheDisplay()
    {
        var logged = new List<string>();
        var colors = new FakeColor { Refuse = true };
        colors.Readings[Device] = Split(hdr: false, wide: false);
        var session = new DisplayColorSession(colors, logged.Add);

        session.Apply([new DisplayColorRequest(Device, "hdr")]);
        session.Apply([new DisplayColorRequest(Device, "hdr")]);
        session.Apply([]);
        session.Apply([new DisplayColorRequest(Device, "hdr")]);
        session.Apply([new DisplayColorRequest(@"\\.\DISPLAY2", "hdr")]);
        session.Apply([new DisplayColorRequest(@"\\.\DISPLAY2", "hdr")]);

        Assert.Equal(
            [
                @"The display \\.\DISPLAY1 could not be switched.",
                @"The display \\.\DISPLAY1 could not be given back the color it showed.",
                @"The display \\.\DISPLAY1 could not be switched.",
                @"The display \\.\DISPLAY1 could not be given back the color it showed.",
                @"The display \\.\DISPLAY2 could not be read.",
            ],
            logged);
    }

    [Fact]
    public void BeforeTheSwitchesSplitTenBitTurnsHdr()
    {
        var colors = new FakeColor();
        colors.Readings[Device] = new DisplayColorReading(true, false, false, false, true, false, 8);
        var session = new DisplayColorSession(colors, _ => { });

        session.Apply([new DisplayColorRequest(Device, "auto", TenBit: true)]);

        Assert.Equal([new DisplayColorChange(true, null)], colors.Sets);
        Assert.Equal(new DisplayColorOriginal(Device, false, false, false), Assert.Single(session.Originals));
    }

    [Fact]
    public void RestoreSavedPutsBackWithoutRemembering()
    {
        var colors = new FakeColor();
        colors.Readings[Device] = Split(hdr: true, wide: true);
        var session = new DisplayColorSession(colors, _ => { });

        session.RestoreSaved([new DisplayColorOriginal(Device, false, false, true)]);

        Assert.False(colors.Readings[Device].HdrOn);
        Assert.False(colors.Readings[Device].WideOn);
        Assert.Empty(session.Originals);
    }

    private static DisplayColorReading Split(bool hdr, bool wide) => new(true, true, hdr, wide, true, true, 8);

    private sealed class FakeColor : IDisplayColorControl
    {
        public Dictionary<string, DisplayColorReading> Readings { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<DisplayColorChange> Sets { get; } = [];

        public bool Refuse { get; init; }

        public bool TryRead(string device, out DisplayColorReading reading) => Readings.TryGetValue(device, out reading);

        public bool TrySet(string device, DisplayColorChange change)
        {
            Sets.Add(change);
            if (Refuse || !Readings.TryGetValue(device, out var reading))
                return false;

            Readings[device] = reading with
            {
                HdrOn = change.Hdr ?? reading.HdrOn,
                WideOn = change.Wide ?? reading.WideOn,
            };
            return true;
        }
    }
}
