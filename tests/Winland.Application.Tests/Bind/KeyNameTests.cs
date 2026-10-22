namespace Winland.Application.Tests;

public class KeyNameTests
{
    [Fact]
    public void GermanLayoutNamesTheUnshiftedCharacter()
    {
        var german = German();

        Assert.Equal("adiaeresis", KeyNames.Name(0xDE, 0x28, false, german, default));
        Assert.Equal("odiaeresis", KeyNames.Name(0xBA, 0x27, false, german, default));
        Assert.Equal("udiaeresis", KeyNames.Name(0xDB, 0x1A, false, german, default));
        Assert.Equal("ssharp", KeyNames.Name(0xBD, 0x0C, false, german, default));
        Assert.Equal("Q", KeyNames.Name(0x51, 0x10, false, german, default));
    }

    [Theory]
    [InlineData("ä")]
    [InlineData("Ä")]
    [InlineData("adiaeresis")]
    [InlineData("Adiaeresis")]
    [InlineData("U+00E4")]
    [InlineData("u+00e4")]
    public void ConfigNamesForAUmlautResolveToTheLayoutSymbol(string written)
    {
        var document = ConfigParser.Parse($"bind = SUPER, {written}, killactive\n", layout: German());

        var bind = Assert.IsType<Bind>(Assert.Single(document.Binds));
        Assert.Equal("adiaeresis", bind.Key);
    }

    [Fact]
    public void OumlautConfigNamesResolveToOdiaeresis()
    {
        var german = German();

        Assert.Equal("odiaeresis", KeyNames.Resolve("ö", german));
        Assert.Equal("odiaeresis", KeyNames.Resolve("odiaeresis", german));
        Assert.Equal("odiaeresis", KeyNames.Resolve("U+00F6", german));
    }

    [Fact]
    public void ListedNonAsciiLettersResolveToTheirSymbols()
    {
        var layout = KeyLayout.From(new Dictionary<int, string>
        {
            [0x41] = "à",
            [0x45] = "é",
            [0xBA] = "è",
            [0xBC] = "ç",
            [0xBD] = "ñ",
            [0xBE] = "å",
            [0xBF] = "ø",
        });

        Assert.Equal("agrave", KeyNames.Resolve("à", layout));
        Assert.Equal("eacute", KeyNames.Resolve("é", layout));
        Assert.Equal("egrave", KeyNames.Resolve("è", layout));
        Assert.Equal("ccedilla", KeyNames.Resolve("ç", layout));
        Assert.Equal("ntilde", KeyNames.Resolve("ñ", layout));
        Assert.Equal("aring", KeyNames.Resolve("å", layout));
        Assert.Equal("oslash", KeyNames.Resolve("ø", layout));
    }

    [Fact]
    public void UsPunctuationNamesStayOnTheUsLayout()
    {
        var layout = KeyLayout.UnitedStates;

        Assert.Equal("Q", KeyNames.Name(0x51, 0x10, false, layout, default));
        Assert.Equal("slash", KeyNames.Name(0xBF, 0x35, false, layout, default));
        Assert.Equal("period", KeyNames.Name(0xBE, 0x34, false, layout, default));
        Assert.Equal("comma", KeyNames.Name(0xBC, 0x33, false, layout, default));
        Assert.Equal("bracketleft", KeyNames.Name(0xDB, 0x1A, false, layout, default));
        Assert.Equal("Q", KeyNames.Resolve("Q", layout));
        Assert.Equal("slash", KeyNames.Resolve("slash", layout));
        Assert.Equal("bracketleft", KeyNames.Resolve("bracketleft", layout));
    }

    [Fact]
    public void VkNameIsUsedWhenTheLayoutDoesNotProduceThatSymbol()
    {
        var german = German();

        Assert.Equal("udiaeresis", KeyNames.Resolve("bracketleft", german));
        Assert.Equal("minus", KeyNames.Resolve("slash", german));
        Assert.Equal("comma", KeyNames.Resolve("comma", german));
        Assert.Equal("Q", KeyNames.Resolve("Q", german));
    }

    [Fact]
    public void KeysymWinsWhenItAndAVkNameWouldChooseDifferentKeys()
    {
        var layout = KeyLayout.From(new Dictionary<int, string>
        {
            [0xBF] = "#",
            [0xBD] = "/",
        });

        Assert.Equal("slash", KeyNames.Resolve("slash", layout));
        Assert.Equal("numbersign", KeyNames.Name(0xBF, 0x35, false, layout, default));
        Assert.Equal("slash", KeyNames.Name(0xBD, 0x0C, false, layout, default));
    }

    [Fact]
    public void DeadKeysFallBackToTheVkName()
    {
        var german = German();

        Assert.Equal("equal", KeyNames.Name(0xBB, 0x0D, false, german, default));
        Assert.Equal("grave", KeyNames.Name(0xC0, 0x29, false, german, default));
    }

    [Fact]
    public void LatchedShiftDoesNotChangeTheUnshiftedName()
    {
        var shifted = new KeyModifierState(Shift: true, Control: false, Alt: false, Super: false);

        Assert.Equal("Q", KeyNames.Name(0x51, 0x10, false, KeyLayout.UnitedStates, shifted));
        Assert.Equal("1", KeyNames.Name(0x31, 0x02, false, KeyLayout.UnitedStates, shifted));
        Assert.Equal("adiaeresis", KeyNames.Name(0xDE, 0x28, false, German(), shifted));
    }

    [Fact]
    public void SyntheticAltGrControlIsNotACtrlKey()
    {
        var german = German();

        Assert.Equal("", KeyNames.Name(0xA2, 0x21D, false, german, default));
        Assert.Equal("", KeyNames.Name(0xA2, 0x1D, true, german, default));
        Assert.Equal("Control_L", KeyNames.Name(0xA2, 0x1D, false, german, default));
        Assert.Equal("Control_R", KeyNames.Name(0xA3, 0x1D, true, german, default));
    }

    [Fact]
    public void RightAltIsLevel3ShiftOnlyWhenTheLayoutHasAltGr()
    {
        Assert.Equal("ISO_Level3_Shift", KeyNames.Name(0xA5, 0x38, true, German(), default));
        Assert.Equal("Alt_R", KeyNames.Name(0xA5, 0x38, true, KeyLayout.UnitedStates, default));
    }

    [Fact]
    public void AltGrChordDoesNotRunACtrlAltBind()
    {
        var german = German();
        var core = Load("bind = CTRL ALT, Q, killactive\n", german);
        Appear(core);

        Press(core, KeyNames.Name(0xA2, 0x21D, false, german, default));
        Press(core, KeyNames.Name(0xA5, 0x38, true, german, default));

        AssertPassed(Press(core, "Q"));
    }

    [Fact]
    public void RealCtrlAltStillRunsACtrlAltBind()
    {
        var core = Load("bind = CTRL ALT, Q, killactive\n", KeyLayout.UnitedStates);
        Appear(core);
        Press(core, "Control_L");
        Press(core, "Alt_L");

        AssertRan(Press(core, "Q"), "killactive");
    }

    [Fact]
    public void ImeKeysHaveNoName()
    {
        Assert.True(KeyNames.PassesThrough(0xE5));
        Assert.True(KeyNames.PassesThrough(0xE7));
        Assert.Equal("", KeyNames.Name(0xE5, 0, false, KeyLayout.UnitedStates, default));
        Assert.Equal("", KeyNames.Name(0xE7, 0, false, KeyLayout.UnitedStates, default));
        Assert.False(KeyNames.PassesThrough(0x51));
    }

    [Theory]
    [InlineData(0x51, 0x10, false, 24)]
    [InlineData(0x54, 0x14, false, 28)]
    [InlineData(0x41, 0x1E, false, 38)]
    [InlineData(0x5A, 0x2C, false, 52)]
    [InlineData(0x5A, 0x15, false, 29)]
    [InlineData(0x31, 0x02, false, 10)]
    [InlineData(0x30, 0x0B, false, 19)]
    [InlineData(0x1B, 0x01, false, 9)]
    [InlineData(0x0D, 0x1C, false, 36)]
    [InlineData(0x0D, 0x1C, true, 104)]
    [InlineData(0x20, 0x39, false, 65)]
    [InlineData(0x09, 0x0F, false, 23)]
    [InlineData(0x08, 0x0E, false, 22)]
    [InlineData(0x26, 0x48, true, 111)]
    [InlineData(0x28, 0x50, true, 116)]
    [InlineData(0x25, 0x4B, true, 113)]
    [InlineData(0x27, 0x4D, true, 114)]
    [InlineData(0x24, 0x47, true, 110)]
    [InlineData(0x23, 0x4F, true, 115)]
    [InlineData(0x21, 0x49, true, 112)]
    [InlineData(0x22, 0x51, true, 117)]
    [InlineData(0x2D, 0x52, true, 118)]
    [InlineData(0x2E, 0x53, true, 119)]
    [InlineData(0x2C, 0x37, true, 107)]
    [InlineData(0x91, 0x46, false, 78)]
    [InlineData(0x13, 0x45, false, 127)]
    [InlineData(0x90, 0x45, false, 77)]
    [InlineData(0x5D, 0x5D, true, 135)]
    [InlineData(0x70, 0x3B, false, 67)]
    [InlineData(0x7B, 0x58, false, 96)]
    [InlineData(0x60, 0x52, false, 90)]
    [InlineData(0x61, 0x4F, false, 87)]
    [InlineData(0x67, 0x47, false, 79)]
    [InlineData(0x6F, 0x35, true, 106)]
    [InlineData(0x6A, 0x37, false, 63)]
    [InlineData(0x6B, 0x4E, false, 86)]
    [InlineData(0x6D, 0x4A, false, 82)]
    [InlineData(0x6E, 0x53, false, 91)]
    [InlineData(0xA0, 0x2A, false, 50)]
    [InlineData(0xA1, 0x36, false, 62)]
    [InlineData(0xA2, 0x1D, false, 37)]
    [InlineData(0xA3, 0x1D, true, 105)]
    [InlineData(0xA4, 0x38, false, 64)]
    [InlineData(0xA5, 0x38, true, 108)]
    [InlineData(0x5B, 0x5B, true, 133)]
    [InlineData(0x5C, 0x5C, true, 134)]
    [InlineData(0xA2, 0x21D, false, 0)]
    public void CodeNumberIsTheXkbKeycode(int virtualKey, int scanCode, bool extended, int code)
    {
        Assert.Equal(code, KeyNames.XkbCode(virtualKey, scanCode, extended));
    }

    [Fact]
    public void CodeBindUsesTheXkbNumber()
    {
        var core = Load("bind = SUPER, code:36, killactive\n", KeyLayout.UnitedStates);
        Appear(core);
        Press(core, "Super_L");

        AssertPassed(Press(core, "Return"));
        core.Handle(new KeyReleased("Return"));
        AssertRan(Press(core, "Return", 36), "killactive");
    }

    [Fact]
    public void ABareNumberAboveNineBindsThatKeycode()
    {
        var core = Load("bind = SUPER, 36, killactive\n", KeyLayout.UnitedStates);
        Appear(core);
        Press(core, "Super_L");

        AssertRan(Press(core, "Return", 36), "killactive");
    }

    [Theory]
    [InlineData("10", "code:10")]
    [InlineData("36", "code:36")]
    [InlineData("9", "9")]
    [InlineData("0", "0")]
    public void ABareNumberAboveNineIsAKeycodeAndADigitIsAKey(string text, string expected)
    {
        Assert.Equal(expected, KeyNames.Resolve(text, KeyLayout.UnitedStates));
    }

    [Fact]
    public void CodeTextIsKeptAsACode()
    {
        Assert.Equal("code:36", KeyNames.Resolve("code:36", German()));
        Assert.Equal("code:36", KeyNames.Resolve("CODE:36", KeyLayout.UnitedStates));
    }

    [Fact]
    public void MouseNamesAndCatchallAreNotRenamed()
    {
        var german = German();

        Assert.Equal("mouse:272", KeyNames.Resolve("mouse:272", german));
        Assert.Equal("catchall", KeyNames.Resolve("catchall", german));

        var document = ConfigParser.Parse("bindm = SUPER, mouse:272, movewindow\n", layout: german);
        var bind = Assert.IsType<Bind>(Assert.Single(document.Binds));
        Assert.Equal("mouse:272", bind.Key);
    }

    [Fact]
    public void MultiKeyPartsResolveSeparately()
    {
        Assert.Equal("adiaeresis&odiaeresis", KeyNames.Resolve("ä&ö", German()));
        Assert.Equal("A&B", KeyNames.Resolve("A&B", KeyLayout.UnitedStates));
    }

    [Fact]
    public void GermanUmlautBindMatchesTheNameTheLayoutProduces()
    {
        var german = German();
        var core = Load("bind = SUPER, odiaeresis, killactive\n", german);
        Appear(core);
        Press(core, "Super_L");

        var name = KeyNames.Name(0xBA, 0x27, false, german, default);
        AssertRan(Press(core, name), "killactive");
    }

    private static KeyLayout German() => KeyLayout.From(
        new Dictionary<int, string>
        {
            [0x30] = "0",
            [0x51] = "q",
            [0xBA] = "ö",
            [0xBC] = ",",
            [0xBD] = "ß",
            [0xBF] = "-",
            [0xDB] = "ü",
            [0xDC] = "#",
            [0xDE] = "ä",
        },
        hasAltGr: true,
        deadKeys: [0xBB, 0xC0]);

    private static WindowManager Load(string text, KeyLayout layout)
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text, layout: layout)));
        return core;
    }

    private static void Appear(WindowManager core) =>
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));

    private static Outcome Press(WindowManager core, string key, int scanCode = 0) =>
        core.Handle(new KeyPressed(key) { ScanCode = scanCode });

    private static void AssertPassed(Outcome outcome)
    {
        Assert.False(outcome.Consumed);
        Assert.Null(outcome.Dispatcher);
    }

    private static void AssertRan(Outcome outcome, string dispatcher)
    {
        Assert.True(outcome.Consumed);
        Assert.Equal(dispatcher, outcome.Dispatcher);
    }
}
