using Winland;
using Winland.Windows;

namespace Winland.Windows.Tests;

public class GpuPreferenceTests
{
    private const string Game = @"C:\Games\Game.exe";
    private const string Other = @"C:\Apps\Other.exe";

    [Theory]
    [InlineData(null, false, "VRROptimizeEnable=0;")]
    [InlineData("", true, "VRROptimizeEnable=1;")]
    [InlineData(";", false, "VRROptimizeEnable=0;")]
    [InlineData("GpuPreference=2;SwapEffectUpgradeEnable=1;", false, "GpuPreference=2;SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;")]
    [InlineData("GpuPreference=2;VRROptimizeEnable=1;SwapEffectUpgradeEnable=1;", false, "GpuPreference=2;VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;")]
    [InlineData("vrroptimizeenable=1;GpuPreference=2;", false, "VRROptimizeEnable=0;GpuPreference=2;")]
    [InlineData("VRROptimizeEnable=1;GpuPreference=2;VRROptimizeEnable=0;", true, "VRROptimizeEnable=1;GpuPreference=2;")]
    [InlineData("note;GpuPreference=2;", false, "note;GpuPreference=2;VRROptimizeEnable=0;")]
    [InlineData("GpuPreference=2; AutoHDREnable=0;", false, "GpuPreference=2; AutoHDREnable=0;VRROptimizeEnable=0;")]
    public void TheFlagIsSetWithoutTouchingTheOtherFlags(string? current, bool enabled, string expected) =>
        Assert.Equal(expected, GpuPreferenceText.WithVrr(current, enabled));

    [Fact]
    public void AProgramIsTurnedOffAndTheExactPreviousValueComesBack()
    {
        var store = new FakePreferences();
        store.Values[Game] = "GpuPreference=2; AutoHDREnable=0;";
        var session = new GpuPreferenceSession(store);

        session.Apply(new VrrRequest([Game], null));
        session.Apply(new VrrRequest([Game], null));

        Assert.Equal("GpuPreference=2; AutoHDREnable=0;VRROptimizeEnable=0;", store.Values[Game]);
        Assert.Equal(1, store.Writes);
        Assert.Equal(new VrrExecutableOriginal(Game, "GpuPreference=2; AutoHDREnable=0;"), Assert.Single(session.Originals!.Executables));

        session.Apply(new VrrRequest([], null));

        Assert.Equal("GpuPreference=2; AutoHDREnable=0;", store.Values[Game]);
        Assert.Null(session.Originals);
    }

    [Fact]
    public void AnAbsentValueIsDeletedWhenTheProgramNoLongerNeedsIt()
    {
        var store = new FakePreferences();
        var session = new GpuPreferenceSession(store);

        session.Apply(new VrrRequest(["", Game], null));
        Assert.False(store.Values.ContainsKey(""));
        session.Apply(new VrrRequest([Game], null));
        Assert.Equal("VRROptimizeEnable=0;", store.Values[Game]);
        Assert.Null(Assert.Single(session.Originals!.Executables).Value);

        session.Restore();

        Assert.False(store.Values.ContainsKey(Game));
        Assert.Null(session.Originals);
    }

    [Fact]
    public void TwoProgramsAndACaseChangeDoNotReadTheOneAlreadyHeld()
    {
        var store = new FakePreferences();
        store.Values[Game] = "GpuPreference=1;";
        store.Values[Other] = "SwapEffectUpgradeEnable=1;";
        var session = new GpuPreferenceSession(store);

        session.Apply(new VrrRequest([Game, Other], null));
        var reads = store.Reads;
        session.Apply(new VrrRequest([@"c:\games\game.exe"], null));

        Assert.Equal(reads, store.Reads);
        Assert.Equal("GpuPreference=1;VRROptimizeEnable=0;", store.Values[Game]);
        Assert.Equal("SwapEffectUpgradeEnable=1;", store.Values[Other]);
    }

    [Fact]
    public void TheAccountWideFlagUsesTheSavedOriginalWhenItFlips()
    {
        var store = new FakePreferences();
        store.Values[GpuPreferenceText.GlobalName] = "GpuPreference=2;AutoHDREnable=1;";
        var session = new GpuPreferenceSession(store);

        session.Apply(new VrrRequest([], false));
        var reads = store.Reads;
        session.Apply(new VrrRequest([], false));
        session.Apply(new VrrRequest([], true));

        Assert.Equal(reads, store.Reads);
        Assert.Equal("GpuPreference=2;AutoHDREnable=1;VRROptimizeEnable=1;", store.Values[GpuPreferenceText.GlobalName]);
        Assert.Equal("GpuPreference=2;AutoHDREnable=1;", session.Originals!.GlobalValue);

        session.Apply(new VrrRequest([], null));

        Assert.Equal("GpuPreference=2;AutoHDREnable=1;", store.Values[GpuPreferenceText.GlobalName]);
        Assert.Null(session.Originals);
    }

    [Fact]
    public void AFailedReadDoesNotWriteAndAFailedRestoreStaysHeld()
    {
        var store = new FakePreferences { FailReads = { Game } };
        var session = new GpuPreferenceSession(store);

        session.Apply(new VrrRequest([Game], null));

        Assert.Equal(0, store.Writes);
        Assert.Null(session.Originals);
        Assert.False(store.Values.ContainsKey(Game));

        store.FailReads.Clear();
        store.Values[Game] = "GpuPreference=2;";
        session.Apply(new VrrRequest([Game], null));
        store.FailWrites.Add(Game);
        session.Apply(new VrrRequest([], null));

        Assert.Equal("GpuPreference=2;VRROptimizeEnable=0;", store.Values[Game]);
        Assert.NotNull(session.Originals);

        store.FailWrites.Clear();
        session.Restore();

        Assert.Equal("GpuPreference=2;", store.Values[Game]);
        Assert.Null(session.Originals);
    }

    [Fact]
    public void AFailedGlobalWriteIsTriedAgain()
    {
        var store = new FakePreferences { FailWrites = { GpuPreferenceText.GlobalName } };
        store.Values[GpuPreferenceText.GlobalName] = "AutoHDREnable=1;";
        var session = new GpuPreferenceSession(store);

        session.Apply(new VrrRequest([], false));
        Assert.Null(session.Originals);
        Assert.Equal("AutoHDREnable=1;", store.Values[GpuPreferenceText.GlobalName]);

        store.FailWrites.Clear();
        session.Apply(new VrrRequest([], false));

        Assert.Equal("AutoHDREnable=1;VRROptimizeEnable=0;", store.Values[GpuPreferenceText.GlobalName]);
        Assert.True(session.Originals!.Global);
    }

    [Fact]
    public void NothingToDoDoesNotTouchTheStore()
    {
        var store = new FakePreferences();
        var session = new GpuPreferenceSession(store);

        session.Apply(new VrrRequest([], null));

        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Writes);
        Assert.Null(session.Originals);
    }

    [Fact]
    public void AFailedGlobalRestoreStaysHeld()
    {
        var store = new FakePreferences();
        store.Values[GpuPreferenceText.GlobalName] = "AutoHDREnable=1;";
        var session = new GpuPreferenceSession(store);
        session.Apply(new VrrRequest([], false));
        store.FailWrites.Add(GpuPreferenceText.GlobalName);

        session.Apply(new VrrRequest([], null));

        Assert.True(session.Originals!.Global);
        Assert.Equal("AutoHDREnable=1;VRROptimizeEnable=0;", store.Values[GpuPreferenceText.GlobalName]);

        store.FailWrites.Clear();
        session.Apply(new VrrRequest([], null));

        Assert.Equal("AutoHDREnable=1;", store.Values[GpuPreferenceText.GlobalName]);
        Assert.Null(session.Originals);
    }

    [Fact]
    public void SavedOriginalsAreWrittenBackAndNotHeld()
    {
        var store = new FakePreferences();
        store.Values[Game] = "VRROptimizeEnable=0;";
        store.Values[GpuPreferenceText.GlobalName] = "VRROptimizeEnable=0;";
        var session = new GpuPreferenceSession(store);
        var saved = new VrrOriginals
        {
            Global = true,
            GlobalValue = "GpuPreference=2;",
            Executables = [new VrrExecutableOriginal(Game, null), new VrrExecutableOriginal(Other, "SwapEffectUpgradeEnable=1;")],
        };

        session.RestoreSaved(saved);

        Assert.Equal("GpuPreference=2;", store.Values[GpuPreferenceText.GlobalName]);
        Assert.False(store.Values.ContainsKey(Game));
        Assert.Equal("SwapEffectUpgradeEnable=1;", store.Values[Other]);
        Assert.Null(session.Originals);

        session.Apply(new VrrRequest([Other], null));
        Assert.Equal("SwapEffectUpgradeEnable=1;", Assert.Single(session.Originals!.Executables).Value);
    }

    [Fact]
    public void ApplyAndRestoreSavedRejectNull()
    {
        var session = new GpuPreferenceSession(new FakePreferences());

        Assert.Throws<ArgumentNullException>(() => session.Apply(null!));
        Assert.Throws<ArgumentNullException>(() => session.RestoreSaved(null!));
    }

    private sealed class FakePreferences : IGpuPreferences
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> FailReads { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> FailWrites { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Reads { get; private set; }

        public int Writes { get; private set; }

        public bool TryRead(string name, out string? value)
        {
            Reads++;
            if (FailReads.Contains(name))
            {
                value = null;
                return false;
            }

            if (Values.TryGetValue(name, out var found))
            {
                value = found;
                return true;
            }

            value = null;
            return true;
        }

        public bool TryWrite(string name, string? value)
        {
            Writes++;
            if (FailWrites.Contains(name))
                return false;

            if (value is null)
                Values.Remove(name);
            else
                Values[name] = value;

            return true;
        }
    }
}
