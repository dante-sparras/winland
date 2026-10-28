using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure.Tests;

[Collection(DesktopCollection.Name)]
[Trait("Category", DesktopCollection.Name)]
public class AppWindowTests
{
    [DesktopFact]
    public void AWindowWithoutASizingFrameIsAdoptedFloating()
    {
        using var window = new Probe(resizable: false);
        var core = new WindowManager(ConfigParser.Parse("general:border_size = 0"));
        using var adapter = new WindowAdapter(core);
        core.ReducedMotion = () => true;

        adapter.AdoptExisting([(nint)window.Handle]);

        Assert.True(Assert.Single(core.Recovery().Clients).Floating);
    }

    [DesktopFact]
    public void AFloatIsAdoptedAtItsVisibleSizeWithoutGrowingOnReadoption()
    {
        using var window = new Probe();
        var expected = ProbeNative.Visible((nint)window.Handle);
        var core = new WindowManager(ConfigParser.Parse("general:border_size = 0\nwindowrule = float, class:.*"));
        using var adapter = new WindowAdapter(core);
        core.ReducedMotion = () => true;

        adapter.AdoptExisting([(nint)window.Handle]);
        var first = Assert.Single(core.Recovery().Clients).Bounds!.Value;
        Assert.Equal(expected.Width, first.Width);
        Assert.Equal(expected.Height, first.Height);
        core.Handle(new WindowClosed(window.Id));
        adapter.AdoptExisting([(nint)window.Handle]);
        Assert.Equal(first, Assert.Single(core.Recovery().Clients).Bounds);
    }

    [DesktopFact]
    public void ADialogWhoseOwnerWinlandHidStillFloatsOnItsOwnersWorkspace()
    {
        using var owner = new Probe();
        var core = new WindowManager(ConfigParser.Parse("general:border_size = 0"));
        using var adapter = new WindowAdapter(core);
        core.ReducedMotion = () => true;
        adapter.AdoptExisting([(nint)owner.Handle]);
        adapter.Apply(core.Handle(new Dispatch("workspace", "2")));
        using var dialog = new Probe(owner: owner.Handle);

        adapter.AdoptExisting([(nint)dialog.Handle]);

        var client = Assert.Single(core.Recovery().Clients, client => client.Window == dialog.Id);
        Assert.True(client.Floating);
        Assert.True(client.Hidden);
        Assert.Equal(new WorkspaceId("1"), client.Workspace);
    }

    [DesktopFact]
    public void ANoActivateWindowWithATaskbarButtonIsLeftUnmanaged()
    {
        using var window = new Probe(extended: WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_APPWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST);
        var core = new WindowManager(ConfigParser.Parse("windowrule = float, class:.*"));
        using var adapter = new WindowAdapter(core);

        adapter.AdoptExisting([(nint)window.Handle]);
        adapter.Apply(core.Handle(new Dispatch("workspace", "2")));

        Assert.False(core.Manages(window.Id));
        Assert.True(PInvoke.IsWindowVisible(window.Handle));
    }

    private sealed class Probe : IDisposable
    {
        public unsafe Probe(bool resizable = true, HWND owner = default, WINDOW_EX_STYLE extended = 0)
        {
            var style = WINDOW_STYLE.WS_OVERLAPPEDWINDOW;
            if (!resizable)
                style &= ~WINDOW_STYLE.WS_THICKFRAME;
            Handle = PInvoke.CreateWindowEx(extended, "STATIC", "Winland app compatibility probe", style,
                100, 100, 450, 300, owner, null, null, null);
            Assert.False(Handle.IsNull);
            PInvoke.ShowWindow(Handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        }

        public HWND Handle { get; }

        public WindowId Id => new((nint)Handle);

        public void Dispose() => PInvoke.DestroyWindow(Handle);
    }
}
