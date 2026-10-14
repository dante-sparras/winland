namespace Winland.Core.Shell;

/// <summary>Names the browser Windows opens a link with, for when the Host's settings name none.</summary>
public interface IDefaultBrowser
{
    /// <summary>The default browser's program, a path or a file name such as <c>msedge.exe</c>.</summary>
    /// <returns>The program, or an empty string when Windows names none.</returns>
    string Name();
}
