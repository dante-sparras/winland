namespace Winland.Domain;

public readonly record struct BindDispatch(string Dispatcher, string Arguments)
{
    /// <summary>Submap to enter after this dispatcher when the dispatcher left the map unchanged.</summary>
    public string? SubmapReset { get; init; }

    /// <summary>
    /// A mouse bind's drag, from <c>bindm</c> or the <c>mouse</c> dispatcher: it moves or resizes the window under the
    /// pointer until the button or key that started it comes up.
    /// </summary>
    public bool Drag { get; init; }
}
