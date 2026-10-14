namespace Winland.Core.Binds;

/// <summary>Receives the key edges of a <see cref="Dispatch.Global"/> bind. The shell implements this.</summary>
public interface IGlobalBindSink
{
    /// <summary>
    /// A global bind's key went down or came up.
    /// A tap is one call with <paramref name="down"/> true and <paramref name="hold"/> false.
    /// A hold calls both edges, with <paramref name="hold"/> true.
    /// May be called on the hook thread. The sink posts to the thread that runs the command.
    /// </summary>
    /// <param name="name">The opaque name from <see cref="Dispatch.Global"/>.</param>
    /// <param name="down">True when the key went down.</param>
    /// <param name="hold">True when the bind is a hold.</param>
    void OnGlobal(string name, bool down, bool hold);
}
