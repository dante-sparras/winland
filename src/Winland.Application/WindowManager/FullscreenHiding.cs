namespace Winland.Application;

/// <summary>Which windows may stay over a fullscreen or maximized Client. <see cref="FullscreenCover"/> decides it.</summary>
public sealed partial class WindowManager
{
    private readonly FullscreenCover _cover;

    // Runs after every change. The z-order requests that lower a floating window the flag now keeps under a covering
    // Client, and raise one it lets out.
    private List<ZOrderRequest> SyncFullscreenHiding() =>
        [.. _cover.Sync(_workspaces, _stacking, window => Find(window) is { Floating: true } && IsShownClient(window))
            .Select(request => new ZOrderRequest(request.Window, request.Top))];
}
