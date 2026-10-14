namespace Winland.Core.Binds;

/// <summary>The shell turns a condition on or off. While it is on, binds that wait for it are visible.</summary>
public interface IBindAsks
{
    /// <summary>Makes every bind waiting on <paramref name="condition"/> visible, or hides them.</summary>
    /// <param name="condition">A name from <see cref="BindConditions"/>, or one the shell and the book agreed on.</param>
    /// <param name="active">True while the binds should be visible.</param>
    void Ask(string condition, bool active);
}
