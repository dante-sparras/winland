namespace Winland.Core.Binds;

/// <summary>Runs a command Winland owns. The process that still executes windows implements this.</summary>
public interface IWinlandDispatch
{
    /// <summary>Runs <paramref name="command"/>.</summary>
    /// <param name="command">A window, workspace, or layout command.</param>
    void Run(WinlandCommand command);
}
