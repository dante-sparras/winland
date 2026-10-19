using Winland;
using Winland.Windows;

namespace Winland.Windows.Tests;

public class KeyboardLayoutSwitcherTests
{
    [Fact]
    public void LoadsTheLayoutsAndActivatesTheChosenOne()
    {
        var system = new FakeLayouts(0x409);
        var switcher = new KeyboardLayoutSwitcher(system);

        switcher.Apply(new KeyboardLayoutRequest("00000409,0000041D", 1));

        Assert.Equal([0x409, 0x41D], system.Layouts);
        Assert.Equal(0x41D, system.Active);

        // The same request does nothing again, and switching loads nothing again.
        system.Active = 0;
        switcher.Apply(new KeyboardLayoutRequest("00000409,0000041D", 1));
        Assert.Equal(0, system.Active);
        switcher.Apply(new KeyboardLayoutRequest("00000409,0000041D", 0));
        Assert.Equal(0x409, system.Active);
        Assert.Equal(2, system.Loads);
    }

    [Fact]
    public void UnloadsOnlyTheLayoutsItLoaded()
    {
        var system = new FakeLayouts(0x409);
        var switcher = new KeyboardLayoutSwitcher(system);
        switcher.Apply(new KeyboardLayoutRequest("00000409,0000041D,00000415", 0));

        switcher.Apply(new KeyboardLayoutRequest("00000409,00000415", 0));
        Assert.Equal([0x409, 0x415], system.Layouts);

        switcher.Apply(null);
        Assert.Equal([0x409], system.Layouts);
        Assert.Equal(0x409, system.Active);
    }

    [Fact]
    public void ALayoutWindowsLacksLoadsNothing()
    {
        var system = new FakeLayouts();
        var switcher = new KeyboardLayoutSwitcher(system);

        switcher.Apply(new KeyboardLayoutRequest(",0000041D", 0));

        Assert.Equal([0x41D], system.Layouts);
        Assert.Equal(0, system.Active);
    }

    private sealed class FakeLayouts(params nint[] loaded) : IKeyboardLayouts
    {
        public List<nint> Layouts { get; } = [.. loaded];

        public nint Active { get; set; }

        public IReadOnlyList<nint> Loaded() => [.. Layouts];

        public nint Current() => Active == 0 && Layouts.Count > 0 ? Layouts[0] : Active;

        public int Loads { get; private set; }

        public nint Load(string id)
        {
            Loads++;
            var layout = (nint)Convert.ToInt32(id, 16);
            if (!Layouts.Contains(layout))
                Layouts.Add(layout);

            return layout;
        }

        public void Unload(nint layout) => Layouts.Remove(layout);

        public void Activate(nint layout) => Active = layout;
    }

    [Fact]
    public void TheFirstLayoutIsPublishedForBinds()
    {
        var system = new FakeLayouts(0x409);
        var switcher = new KeyboardLayoutSwitcher(system);
        Assert.Equal(0, switcher.First);

        switcher.Apply(new KeyboardLayoutRequest("0000041D,00000409", 1));
        Assert.Equal(0x41D, switcher.First);

        switcher.Apply(null);
        Assert.Equal(0, switcher.First);
    }
}
