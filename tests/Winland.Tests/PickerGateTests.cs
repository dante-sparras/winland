using FluentAssertions;
using Winland.Core.Input;

namespace Winland.Tests;

/// <summary>The hook keeps the picker's keys from the pushed list, and does not ask the shell.</summary>
[TestClass]
public sealed class PickerGateTests
{
    private const int Return = 0x0D;
    private const int A = 0x41;
    private const int R = 0x52;
    private const int S = 0x53;
    private const int Left = 0x25;
    private const int Up = 0x26;
    private const int Right = 0x27;
    private const int Down = 0x28;
    private const int Tab = 0x09;

    private readonly PickerGate _gate = new();

    public PickerGateTests() => _gate.Use(open: true, PickerGate.CaptureKeys);

    [TestMethod]
    public void S_toggles_scrolling_and_Control_S_is_kept_without_acting()
    {
        _gate.OnKey(S, down: true, super: false, ctrl: false, alt: false).Should().Be(new PickerTake(true, 8));
        _gate.OnKey(S, down: true, super: false, ctrl: true, alt: false).Should().Be(new PickerTake(true, null));
    }

    [TestMethod]
    public void Super_arrow_is_kept_and_moves_the_picker()
    {
        _gate.OnKey(Left, down: true, super: true, ctrl: false, alt: false).Should().Be(new PickerTake(true, 1));
        _gate.OnKey(Right, down: true, super: true, ctrl: false, alt: false).Should().Be(new PickerTake(true, 2));
        _gate.OnKey(Up, down: true, super: true, ctrl: false, alt: false).Should().Be(new PickerTake(true, 3));
        _gate.OnKey(Down, down: true, super: true, ctrl: false, alt: false).Should().Be(new PickerTake(true, 4));
    }

    [TestMethod]
    public void Return_R_and_Control_A_are_the_pickers()
    {
        _gate.OnKey(Return, down: true, super: false, ctrl: false, alt: false).Should().Be(new PickerTake(true, 5));
        _gate.OnKey(R, down: true, super: false, ctrl: false, alt: false).Should().Be(new PickerTake(true, 7));
        _gate.OnKey(A, down: true, super: false, ctrl: true, alt: false).Should().Be(new PickerTake(true, 6));
    }

    [TestMethod]
    public void A_key_the_list_does_not_name_is_kept_and_presses_nothing()
    {
        _gate.OnKey(Tab, down: true, super: false, ctrl: false, alt: false).Should().Be(new PickerTake(true, null));
        _gate.OnKey(A, down: true, super: false, ctrl: false, alt: false).Should().Be(new PickerTake(true, null));
        _gate.OnKey(R, down: true, super: true, ctrl: false, alt: false).Should().Be(new PickerTake(true, null));
    }

    [TestMethod]
    public void A_key_the_picker_took_has_its_key_up_kept_after_the_picker_closed()
    {
        _gate.OnKey(Return, down: true, super: false, ctrl: false, alt: false);
        _gate.Use(open: false, []);

        _gate.OnKey(Return, down: false, super: false, ctrl: false, alt: false).Should().Be(new PickerTake(true, null));
        _gate.OnKey(Return, down: true, super: false, ctrl: false, alt: false).Should().Be(PickerTake.Pass);
    }

    [TestMethod]
    public void A_key_that_went_down_before_the_picker_opened_still_releases()
    {
        _gate.OnKey(0x20, down: false, super: false, ctrl: false, alt: false).Should().Be(PickerTake.Pass);
    }

    [TestMethod]
    public void Super_and_Control_still_act_after_their_own_key_downs_were_kept()
    {
        const int super = 0x5B;
        const int control = 0xA2;

        _gate.OnKey(super, down: true, super: false, ctrl: false, alt: false);
        _gate.OnKey(Left, down: true, super: false, ctrl: false, alt: false).Should().Be(new PickerTake(true, 1));
        _gate.OnKey(super, down: false, super: false, ctrl: false, alt: false);
        _gate.OnKey(Left, down: true, super: false, ctrl: false, alt: false).Should().Be(new PickerTake(true, null));

        _gate.OnKey(control, down: true, super: false, ctrl: false, alt: false);
        _gate.OnKey(A, down: true, super: false, ctrl: false, alt: false).Should().Be(new PickerTake(true, 6));
    }

    [TestMethod]
    public void Print_Screen_still_reaches_the_chord_ledger()
    {
        const int printScreen = 0x2C;

        _gate.OnKey(printScreen, down: true, super: false, ctrl: false, alt: false).Should().Be(PickerTake.Pass);
        _gate.OnKey(printScreen, down: false, super: false, ctrl: false, alt: false).Should().Be(PickerTake.Pass);
    }

    [TestMethod]
    public void A_later_push_replaces_the_keys()
    {
        _gate.Use(open: true, [new KeptKey(Return, false, false, false, 5)]);

        _gate.OnKey(S, down: true, super: false, ctrl: false, alt: false).Should().Be(new PickerTake(true, null));
        _gate.OnKey(Return, down: true, super: false, ctrl: false, alt: false).Should().Be(new PickerTake(true, 5));
    }
}
