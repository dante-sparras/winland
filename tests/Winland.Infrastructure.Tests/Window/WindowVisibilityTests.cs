using Events = Windows.Win32.PInvoke;

namespace Winland.Infrastructure.Tests;

public class WindowVisibilityTests
{
    private static readonly WindowFacts ShownClient = new(Minimized: false, Visible: true, Managed: true, Concealed: false);

    [Fact]
    public void AMinimizeWinlandDidNotAskForIsTakenBack()
    {
        Assert.Equal(VisibilityAction.UndoMinimize, WindowVisibility.Decide(Events.EVENT_OBJECT_HIDE, ShownClient with { Minimized = true }));
    }

    [Fact]
    public void AMinimizedClientHiddenToTheTrayStopsBeingAClient()
    {
        var tray = ShownClient with { Minimized = true, Visible = false };

        Assert.Equal(VisibilityAction.Close, WindowVisibility.Decide(Events.EVENT_OBJECT_HIDE, tray));
        Assert.Equal(VisibilityAction.None, WindowVisibility.Decide(Events.EVENT_OBJECT_HIDE, tray with { Concealed = true }));
    }

    [Fact]
    public void AClientItsApplicationHidesStopsBeingAClientUnlessWinlandHidIt()
    {
        var hidden = ShownClient with { Visible = false };

        Assert.Equal(VisibilityAction.Close, WindowVisibility.Decide(Events.EVENT_OBJECT_HIDE, hidden));
        Assert.Equal(VisibilityAction.None, WindowVisibility.Decide(Events.EVENT_OBJECT_HIDE, hidden with { Concealed = true }));
        Assert.Equal(VisibilityAction.None, WindowVisibility.Decide(Events.EVENT_OBJECT_HIDE, ShownClient));
        Assert.Equal(VisibilityAction.None, WindowVisibility.Decide(Events.EVENT_OBJECT_HIDE, hidden with { Managed = false }));
    }

    [Fact]
    public void AHiddenClientThatIsCloakedStaysAClient()
    {
        Assert.Equal(VisibilityAction.None, WindowVisibility.Decide(Events.EVENT_OBJECT_CLOAKED, ShownClient with { Visible = false }));
        Assert.Equal(VisibilityAction.Close, WindowVisibility.Decide(Events.EVENT_OBJECT_CLOAKED, ShownClient));
    }

    [Theory]
    [InlineData(Events.EVENT_OBJECT_SHOW)]
    [InlineData(Events.EVENT_OBJECT_UNCLOAKED)]
    [InlineData(Events.EVENT_OBJECT_CREATE)]
    public void AShownClientIsRestoredAndAnyOtherWindowOffered(uint @event)
    {
        Assert.Equal(VisibilityAction.Restore, WindowVisibility.Decide(@event, ShownClient));
        Assert.Equal(VisibilityAction.Adopt, WindowVisibility.Decide(@event, ShownClient with { Managed = false }));
        Assert.Equal(VisibilityAction.None, WindowVisibility.Decide(@event, ShownClient with { Minimized = true }));
    }

    [Fact]
    public void ARenamedClientIsRetitledAndARenamedWindowOffered()
    {
        Assert.Equal(VisibilityAction.Retitle, WindowVisibility.Decide(Events.EVENT_OBJECT_NAMECHANGE, ShownClient));
        Assert.Equal(VisibilityAction.Adopt, WindowVisibility.Decide(Events.EVENT_OBJECT_NAMECHANGE, ShownClient with { Managed = false }));
    }

    [Fact]
    public void ADeclinedWindowIsOfferedAgainOnlyWhenTheRenameCouldChangeTheAnswer()
    {
        var declined = ShownClient with { Managed = false, Declined = true };

        Assert.Equal(VisibilityAction.None, WindowVisibility.Decide(Events.EVENT_OBJECT_NAMECHANGE, declined));
        Assert.Equal(VisibilityAction.Adopt, WindowVisibility.Decide(Events.EVENT_OBJECT_NAMECHANGE, declined with { Declined = false }));
        Assert.Equal(VisibilityAction.Adopt, WindowVisibility.Decide(Events.EVENT_OBJECT_SHOW, declined));
    }
}
