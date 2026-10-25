namespace Winland.Infrastructure;

/// <summary>A system setting read on first use and kept until it is forgotten, so a step does not make a system call for it.</summary>
internal sealed class CachedSetting(Func<bool> read)
{
    private readonly Lock _gate = new();
    private bool _known;
    private bool _value;

    internal bool Value
    {
        get
        {
            lock (_gate)
            {
                if (!_known)
                {
                    _value = read();
                    _known = true;
                }

                return _value;
            }
        }
    }

    /// <summary>The next read asks the system again. Windows broadcasts a setting's change after it is made.</summary>
    internal void Forget()
    {
        lock (_gate)
            _known = false;
    }
}
