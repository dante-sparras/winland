namespace Winland.Infrastructure.Tests;

/// <summary>Outcomes decided in one order and applied in another, as when a pipe thread and the main thread race.</summary>
public class ApplyOrderTests
{
    private static readonly WindowId First = new(1);
    private static readonly WindowId Second = new(2);
    private static readonly State Clients = new([new Client(new ClientId(1), First), new Client(new ClientId(2), Second)]);

    [Fact]
    public void OutcomesInOrderPassUnchanged()
    {
        var order = new ApplyOrder();
        var older = Decided(1) with { Focus = First };
        var newer = Decided(2) with { Focus = Second };

        var admitted = order.Admit(older);
        Assert.Same(older, admitted.Outcome);
        Assert.True(admitted.Current);
        Assert.Same(newer, order.Admit(newer).Outcome);
    }

    [Fact]
    public void APlacementANewerOutcomeMadeSinceItWasAdmittedIsSkipped()
    {
        var order = new ApplyOrder();
        var older = order.Admit(Decided(1) with { Placements = [new Placement(new ClientId(1), new Rectangle(0, 0, 10, 10))] }).Outcome;

        Assert.False(order.Superseded(older, First));
        order.Admit(Decided(2) with { Placements = [new Placement(new ClientId(1), new Rectangle(5, 5, 10, 10))] });

        Assert.True(order.Superseded(older, First));
        Assert.False(order.Superseded(older, Second));
    }

    [Fact]
    public void AnOlderHideDoesNotUndoANewerShow()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2) with { Show = [First] });

        var admitted = order.Admit(Decided(1) with { Hide = [First, Second] });

        Assert.False(admitted.Current);
        Assert.Equal([Second], admitted.Outcome.Hide);
    }

    [Fact]
    public void AnOlderPlacementDoesNotMoveAWindowANewerOnePlaced()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2) with { Placements = [new Placement(new ClientId(1), new Rectangle(0, 0, 10, 10))] });

        var admitted = order.Admit(Decided(1) with
        {
            Placements =
            [
                new Placement(new ClientId(1), new Rectangle(5, 5, 10, 10)),
                new Placement(new ClientId(2), new Rectangle(20, 0, 10, 10)),
            ],
        });

        Assert.Equal([new Placement(new ClientId(2), new Rectangle(20, 0, 10, 10))], admitted.Outcome.Placements);
    }

    [Fact]
    public void AnOlderFocusLosesOnlyToANewerFocus()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2));
        Assert.Equal(First, order.Admit(Decided(1) with { Focus = First }).Outcome.Focus);

        order.Admit(Decided(4) with { Focus = Second });
        Assert.Null(order.Admit(Decided(3) with { Focus = First }).Outcome.Focus);
    }

    [Fact]
    public void AnOlderOutcomeStillCarriesOutItsOneTimeRequests()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2) with { Show = [First] });
        var launch = new Launch("app", null);

        var admitted = order.Admit(Decided(1) with { Close = First, Launch = launch, Kills = [new KillRequest(7)] });

        Assert.Equal(First, admitted.Outcome.Close);
        Assert.Same(launch, admitted.Outcome.Launch);
        Assert.Equal([new KillRequest(7)], admitted.Outcome.Kills);
    }

    // Z-order requests are one-off requests. An older one loses only the windows a
    // newer outcome restacked.
    [Fact]
    public void AnOlderZOrderRequestLosesOnlyTheWindowsANewerOneRestacked()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2) with { ZOrder = [new ZOrderRequest(First, Top: false)] });

        var admitted = order.Admit(Decided(1) with { ZOrder = [new ZOrderRequest(First, Top: true), new ZOrderRequest(Second, Top: true)] });

        Assert.False(admitted.Current);
        Assert.Equal([new ZOrderRequest(Second, Top: true)], admitted.Outcome.ZOrder);
    }

    // A warp is a one-off request too. An older warp loses only to a newer warp.
    [Fact]
    public void AnOlderWarpLosesOnlyToANewerWarp()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2));
        Assert.Equal(new WarpTo(new Point(5, 5)), order.Admit(Decided(1) with { Warp = new WarpTo(new Point(5, 5)) }).Outcome.Warp);

        order.Admit(Decided(4) with { Warp = new WarpTo(new Point(9, 9)) });
        Assert.Null(order.Admit(Decided(3) with { Warp = new WarpTo(new Point(1, 1)) }).Outcome.Warp);
    }

    [Fact]
    public void EachCoreKeepsItsOwnOrder()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(5, core: 1) with { Show = [First] });

        var other = order.Admit(Decided(1, core: 2) with { Hide = [First] });

        Assert.True(other.Current);
        Assert.Equal([First], other.Outcome.Hide);
    }

    [Fact]
    public void AnOutcomeMadeOutsideACoreIsApplied()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2) with { Show = [First] });
        var loose = new Outcome([], Clients) { Hide = [First] };

        var admitted = order.Admit(loose);

        Assert.True(admitted.Current);
        Assert.Same(loose, admitted.Outcome);
    }

    [Fact]
    public void TheSameOutcomeAgainIsNotCurrentButKeepsItsParts()
    {
        var order = new ApplyOrder();
        var outcome = Decided(3) with { Focus = First, Warp = new WarpTo(new Point(1, 1)), Show = [First] };

        order.Admit(outcome);
        var again = order.Admit(outcome);

        Assert.False(again.Current);
        Assert.Same(outcome, again.Outcome);
    }

    // A close motion always runs. A hide motion goes with its hide, and any other with its placement.
    [Fact]
    public void AMotionGoesWithThePartItAnimates()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2) with
        {
            Show = [First],
            Placements = [new Placement(new ClientId(2), new Rectangle(0, 0, 10, 10))],
        });
        var close = new Motion(First, new Rectangle(0, 0, 10, 10)) { End = MotionEnd.Close };
        var hide = new Motion(First, new Rectangle(0, 0, 10, 10)) { End = MotionEnd.Hide };
        var move = new Motion(Second, new Rectangle(5, 5, 10, 10));
        var free = new Motion(new WindowId(3), new Rectangle(5, 5, 10, 10));

        var admitted = order.Admit(Decided(1) with { Hide = [First], Motions = [close, hide, move, free] });

        Assert.Equal([close, free], admitted.Outcome.Motions);
        Assert.Empty(admitted.Outcome.Hide);
    }

    // A part an older outcome keeps whole is passed on as it is.
    [Fact]
    public void APartWithNothingDroppedIsPassedOnAsItIs()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2) with { Show = [First] });
        var older = Decided(1) with
        {
            Hide = [First],
            Show = [Second],
            ResetDecorations = [Second],
            Placements = [new Placement(new ClientId(2), new Rectangle(0, 0, 10, 10))],
            Decorations = [new Decoration(new ClientId(2), new BorderColor(0xFF, 0, 0, 0), CornerPreference.DoNotRound)],
            Motions = [new Motion(Second, new Rectangle(0, 0, 10, 10))],
            ZOrder = [new ZOrderRequest(Second, Top: true)],
        };

        var admitted = order.Admit(older).Outcome;

        Assert.Empty(admitted.Hide);
        Assert.Same(older.Show, admitted.Show);
        Assert.Same(older.ResetDecorations, admitted.ResetDecorations);
        Assert.Same(older.Placements, admitted.Placements);
        Assert.Same(older.Decorations, admitted.Decorations);
        Assert.Same(older.Motions, admitted.Motions);
        Assert.Same(older.ZOrder, admitted.ZOrder);
    }

    // Each part that loses something is trimmed, however many others lost something too.
    [Fact]
    public void EveryPartThatLosesAWindowIsTrimmed()
    {
        var newer = Decided(5) with
        {
            Show = [First],
            Hide = [Second],
            Placements = [new Placement(new ClientId(1), new Rectangle(0, 0, 10, 10))],
            Decorations = [new Decoration(new ClientId(2), new BorderColor(0xFF, 0, 0, 0), CornerPreference.DoNotRound)],
            ZOrder = [new ZOrderRequest(First, Top: true)],
        };
        Outcome Older(Outcome older)
        {
            var order = new ApplyOrder();
            order.Admit(newer);
            return order.Admit(older).Outcome;
        }

        var shown = Older(Decided(4) with { Hide = [First], Show = [Second] });
        var placed = Older(Decided(4) with
        {
            Placements = [new Placement(new ClientId(1), new Rectangle(5, 5, 10, 10))],
            Decorations = [new Decoration(new ClientId(2), new BorderColor(0xFF, 9, 9, 9), CornerPreference.DoNotRound)],
        });
        var moved = Older(Decided(4) with { Hide = [First], Motions = [new Motion(First, new Rectangle(5, 5, 10, 10))] });
        var stacked = Older(Decided(4) with { Hide = [First], ZOrder = [new ZOrderRequest(First, Top: false)] });

        Assert.Empty(shown.Hide);
        Assert.Empty(shown.Show);
        Assert.Empty(placed.Placements);
        Assert.Empty(placed.Decorations);
        Assert.Empty(moved.Hide);
        Assert.Empty(moved.Motions);
        Assert.Empty(stacked.Hide);
        Assert.Empty(stacked.ZOrder);
    }

    // The ledgers forget windows that are no longer Clients once they hold more than twice the Clients
    // and sixteen more. An older outcome then wins for a window that left.
    [Theory]
    [InlineData("show")]
    [InlineData("reset")]
    [InlineData("zorder")]
    public void ALedgerForgetsWindowsThatLeftOnceItIsFull(string ledger)
    {
        var limit = (2 * Clients.Clients.Count) + 16;
        var atLimit = Trimmed(ledger, windows: limit);
        var overLimit = Trimmed(ledger, windows: limit + 1);

        Assert.False(atLimit);
        Assert.True(overLimit);
    }

    [Fact]
    public void ThePlacementLedgerForgetsClientsThatLeftOnceItIsFull()
    {
        var many = Enumerable.Range(100, 30).Select(id => new Client(new ClientId(id), new WindowId(id))).ToArray();
        var order = new ApplyOrder();
        order.Admit(new Outcome([.. many.Select(client => new Placement(client.Id, new Rectangle(0, 0, 10, 10)))], new State(many)) { Order = new OutcomeOrder(1, 2) });
        order.Admit(Decided(3));

        var older = order.Admit(new Outcome([new Placement(new ClientId(110), new Rectangle(5, 5, 10, 10))], new State(many)) { Order = new OutcomeOrder(1, 1) });

        Assert.Single(older.Outcome.Placements);
    }

    // Whether an older outcome's hide of window 100 survives once newer outcomes showed `windows` windows
    // that are not Clients.
    private static bool Trimmed(string ledger, int windows)
    {
        var order = new ApplyOrder();
        var gone = Enumerable.Range(100, windows).Select(id => new WindowId(id)).ToArray();
        var newer = ledger switch
        {
            "show" => Decided(2) with { Show = gone },
            "reset" => Decided(2) with { ResetDecorations = gone },
            _ => Decided(2) with { ZOrder = [.. gone.Select(window => new ZOrderRequest(window, Top: true))] },
        };
        order.Admit(newer);
        var older = ledger switch
        {
            "show" => Decided(1) with { Hide = [gone[0]] },
            "reset" => Decided(1) with { ResetDecorations = [gone[0]] },
            _ => Decided(1) with { ZOrder = [new ZOrderRequest(gone[0], Top: false)] },
        };

        var admitted = order.Admit(older).Outcome;
        return ledger switch
        {
            "show" => admitted.Hide.Count == 1,
            "reset" => admitted.ResetDecorations.Count == 1,
            _ => admitted.ZOrder.Count == 1,
        };
    }

    private static Outcome Decided(long sequence, long core = 1) =>
        new([], Clients) { Order = new OutcomeOrder(core, sequence) };
}
