namespace Winland;

public sealed partial class Core
{
    private readonly Dictionary<WindowId, OpacityChoice> _opacity = [];

    private readonly record struct OpacityChoice(OpacityRule? Rule, bool Opaque);

    private void RememberOpacity(WindowId window, WindowDecision decision)
    {
        if (decision.Opacity is null && !decision.Opaque)
        {
            _opacity.Remove(window);
            return;
        }

        _opacity[window] = new OpacityChoice(decision.Opacity, decision.Opaque);
    }

    private void ForgetOpacity(WindowId window) => _opacity.Remove(window);

    private OpacityChoice OpacityOf(WindowId window) =>
        _opacity.TryGetValue(window, out var choice) ? choice : default;
}
