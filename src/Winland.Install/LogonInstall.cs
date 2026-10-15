namespace Winland.Install;

public sealed class LogonInstall
{
    private readonly ITaskCatalog catalog;

    public LogonInstall(ITaskCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
    }

    public void Install(string executable, string user)
    {
        var task = LogonTaskDefinition.Build(executable, user);
        if (catalog.Contains(task.Name))
            catalog.Replace(task);
        else
            catalog.Create(task);
    }

    public void Uninstall()
    {
        if (catalog.Contains(LogonTaskDefinition.TaskName))
            catalog.Delete(LogonTaskDefinition.TaskName);
    }
}
