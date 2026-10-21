namespace Winland.Install;

public interface ITaskCatalog
{
    bool Contains(string taskName);

    void Create(LogonTask task);

    void Replace(LogonTask task);

    void Delete(string taskName);
}
