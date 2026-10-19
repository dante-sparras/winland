using Windows.Win32.Devices.Display;
using Winland;

namespace Winland.Windows.Tests;

public class DisplayMirrorTests
{
    private const string Left = @"\\.\DISPLAY1";
    private const string Right = @"\\.\DISPLAY2";
    private static readonly DisplayMirrorRequest RightOnLeft = new(Right, Left);

    [Fact]
    public void AMirrorIsMadeOnceAndRemembered()
    {
        var topology = new FakeTopology();
        var session = new DisplayMirrorSession(topology, _ => { });

        session.Apply([RightOnLeft]);
        session.Apply([RightOnLeft]);

        Assert.Equal([RightOnLeft], topology.Mirrored);
        Assert.Equal(Right, Assert.Single(session.Originals).Device);
    }

    [Fact]
    public void AMirrorTheRulesNoLongerAskForIsPutBack()
    {
        var topology = new FakeTopology();
        var session = new DisplayMirrorSession(topology, _ => { });
        session.Apply([RightOnLeft]);

        session.Apply([]);

        Assert.Equal(Right, Assert.Single(topology.PutBack).Device);
        Assert.Empty(session.Originals);
    }

    [Fact]
    public void AMirrorOfAnotherSourceIsPutBackAndMadeAgain()
    {
        var topology = new FakeTopology();
        var session = new DisplayMirrorSession(topology, _ => { });
        session.Apply([RightOnLeft]);

        session.Apply([new DisplayMirrorRequest(Right, @"\\.\DISPLAY3")]);

        Assert.Equal(Left, Assert.Single(topology.PutBack).Source);
        Assert.Equal([RightOnLeft, new DisplayMirrorRequest(Right, @"\\.\DISPLAY3")], topology.Mirrored);
    }

    [Fact]
    public void AMirrorWindowsRefusesIsSaidOnceAndTriedAgainOnlyOnceAskedAfresh()
    {
        var topology = new FakeTopology { Refusal = "the two displays are on different graphics adapters." };
        var logged = new List<string>();
        var session = new DisplayMirrorSession(topology, logged.Add);

        session.Apply([RightOnLeft]);
        session.Apply([RightOnLeft]);

        Assert.Equal(
            [@"The display \\.\DISPLAY2 cannot mirror \\.\DISPLAY1: the two displays are on different graphics adapters."],
            logged);
        Assert.Single(topology.Mirrored);
        Assert.Empty(session.Originals);

        session.Apply([]);
        session.Apply([RightOnLeft]);

        Assert.Equal(2, topology.Mirrored.Count);
    }

    [Fact]
    public void RestorePutsBackEveryMirrorNewestFirst()
    {
        var topology = new FakeTopology();
        var session = new DisplayMirrorSession(topology, _ => { });
        session.Apply([RightOnLeft, new DisplayMirrorRequest(@"\\.\DISPLAY3", Left)]);

        session.Restore();

        Assert.Equal([@"\\.\DISPLAY3", Right], topology.PutBack.Select(mirror => mirror.Device));
        Assert.Empty(session.Originals);
    }

    [Fact]
    public void SavedMirrorsArePutBack()
    {
        var topology = new FakeTopology();
        var session = new DisplayMirrorSession(topology, _ => { });
        var saved = new DisplayMirrorOriginal(Right, Left, "Samsung", 5, 2, 1, null);

        session.RestoreSaved([saved]);

        Assert.Equal([saved], topology.PutBack);
        Assert.Empty(session.Originals);
    }

    [Fact]
    public void AMirrorThatCannotBePutBackIsSaid()
    {
        var topology = new FakeTopology { CanPutBack = false };
        var logged = new List<string>();
        var session = new DisplayMirrorSession(topology, logged.Add);
        session.Apply([RightOnLeft]);

        session.Restore();

        Assert.Equal([@"The display \\.\DISPLAY2 could not be given back its own picture."], logged);
    }

    [Fact]
    public void CloningGivesTheMirrorItsSourcesSourceAndDropsItsModes()
    {
        DISPLAYCONFIG_PATH_INFO[] paths = [Path(adapter: 5, source: 0, target: 10, sourceMode: 0, targetMode: 1), Path(adapter: 5, source: 1, target: 11, sourceMode: 2, targetMode: 3)];

        Assert.Null(DisplayTopology.Clone(paths, mirror: 1, source: 0));

        Assert.Equal(0u, paths[1].sourceInfo.id);
        Assert.Equal(DisplayTopology.NoMode, paths[1].sourceInfo.Anonymous.modeInfoIdx);
        Assert.Equal(DisplayTopology.NoMode, paths[1].targetInfo.Anonymous.modeInfoIdx);
        Assert.Equal(0u, paths[0].sourceInfo.Anonymous.modeInfoIdx);
        Assert.Equal(DisplayTopology.NoMode, paths[0].targetInfo.Anonymous.modeInfoIdx);
        Assert.True(DisplayTopology.Shares(paths, 1));
    }

    [Fact]
    public void DisplaysOnDifferentAdaptersAreNotCloned()
    {
        DISPLAYCONFIG_PATH_INFO[] paths = [Path(adapter: 5, source: 0, target: 10, sourceMode: 0, targetMode: 1), Path(adapter: 6, source: 0, target: 11, sourceMode: 2, targetMode: 3)];
        var before = paths.ToArray();

        Assert.Equal("the two displays are on different graphics adapters.", DisplayTopology.Clone(paths, mirror: 1, source: 0));
        Assert.Equal(before, paths);
    }

    [Fact]
    public void ADisplayShowingThatSourceAlreadyIsNotClonedAgain()
    {
        DISPLAYCONFIG_PATH_INFO[] paths = [Path(adapter: 5, source: 0, target: 10, sourceMode: 0, targetMode: 1), Path(adapter: 5, source: 0, target: 11, sourceMode: 0, targetMode: 2)];

        Assert.NotNull(DisplayTopology.Clone(paths, mirror: 1, source: 0));
        Assert.NotNull(DisplayTopology.Clone(paths, mirror: 0, source: 0));
    }

    [Fact]
    public void CompactingKeepsOnlyTheModesAPathNamesAndMovesTheIndexes()
    {
        DISPLAYCONFIG_PATH_INFO[] paths = [Path(adapter: 5, source: 0, target: 10, sourceMode: 2, targetMode: DisplayTopology.NoMode)];
        DISPLAYCONFIG_MODE_INFO[] modes = [Mode(DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET, 10), Mode(DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET, 11), Mode(DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE, 0)];

        var kept = DisplayTopology.Compact(paths, modes);

        Assert.Equal(0u, Assert.Single(kept).id);
        Assert.Equal(0u, paths[0].sourceInfo.Anonymous.modeInfoIdx);
        Assert.Equal(DisplayTopology.NoMode, paths[0].targetInfo.Anonymous.modeInfoIdx);
    }

    [Fact]
    public void SeparatingGivesBackTheSourceAndItsMode()
    {
        DISPLAYCONFIG_PATH_INFO[] paths = [Path(adapter: 5, source: 0, target: 10, sourceMode: 0, targetMode: 1), Path(adapter: 5, source: 0, target: 11, sourceMode: DisplayTopology.NoMode, targetMode: DisplayTopology.NoMode)];
        var modes = new List<DISPLAYCONFIG_MODE_INFO> { Mode(DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE, 0), Mode(DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET, 10) };
        var original = new DisplayMirrorOriginal(Right, Left, "Samsung", 5, 11, 1, new DisplaySourceMode(1280, 1024, 4, 1920, -10));
        var index = DisplayTopology.FindTarget(paths, 5, 11);

        DisplayTopology.Separate(paths, modes, index, original);

        Assert.Equal(1, index);
        Assert.Equal(1u, paths[1].sourceInfo.id);
        Assert.False(DisplayTopology.Shares(paths, 1));
        Assert.Equal(3, modes.Count);
        Assert.Equal(2u, paths[1].sourceInfo.Anonymous.modeInfoIdx);
        Assert.Equal(original.Mode, DisplayTopology.SourceMode(paths[1], modes.ToArray()));
        Assert.Equal(1u, modes[2].id);
    }

    [Fact]
    public void SeparatingWithoutASavedModeLetsWindowsPickIt()
    {
        DISPLAYCONFIG_PATH_INFO[] paths = [Path(adapter: 5, source: 0, target: 10, sourceMode: 0, targetMode: 1), Path(adapter: 5, source: 0, target: 11, sourceMode: DisplayTopology.NoMode, targetMode: DisplayTopology.NoMode)];
        var modes = new List<DISPLAYCONFIG_MODE_INFO>();

        DisplayTopology.Separate(paths, modes, 1, new DisplayMirrorOriginal(Right, Left, "", 5, 11, 1, null));

        Assert.Equal(1u, paths[1].sourceInfo.id);
        Assert.Equal(DisplayTopology.NoMode, paths[1].sourceInfo.Anonymous.modeInfoIdx);
        Assert.Empty(modes);
        Assert.Equal(-1, DisplayTopology.FindTarget(paths, 6, 11));
    }

    [Fact]
    public void AnAdapterKeyRoundTrips()
    {
        var luid = DisplayTopology.Luid(-4294967291);

        Assert.Equal(-4294967291, DisplayTopology.Key(luid));
    }

    private static DISPLAYCONFIG_PATH_INFO Path(long adapter, uint source, uint target, uint sourceMode, uint targetMode)
    {
        var path = new DISPLAYCONFIG_PATH_INFO();
        path.sourceInfo.adapterId = DisplayTopology.Luid(adapter);
        path.sourceInfo.id = source;
        path.sourceInfo.Anonymous.modeInfoIdx = sourceMode;
        path.targetInfo.adapterId = DisplayTopology.Luid(adapter);
        path.targetInfo.id = target;
        path.targetInfo.Anonymous.modeInfoIdx = targetMode;
        return path;
    }

    private static DISPLAYCONFIG_MODE_INFO Mode(DISPLAYCONFIG_MODE_INFO_TYPE type, uint id) => new() { infoType = type, id = id };

    private sealed class FakeTopology : IDisplayTopology
    {
        public string? Refusal { get; init; }

        public bool CanPutBack { get; init; } = true;

        public List<DisplayMirrorRequest> Mirrored { get; } = [];

        public List<DisplayMirrorOriginal> PutBack { get; } = [];

        public DisplayMirrorOriginal? Mirror(DisplayMirrorRequest request, out string failure)
        {
            Mirrored.Add(request);
            failure = Refusal ?? "";
            return Refusal is null ? new DisplayMirrorOriginal(request.Device, request.Source, "", 5, 2, 1, null) : null;
        }

        public bool Unmirror(DisplayMirrorOriginal original)
        {
            PutBack.Add(original);
            return CanPutBack;
        }
    }
}
