namespace Winland.Install.Tests;

sealed class MemoryTaskCatalog : ITaskCatalog
{
    private readonly List<string> operations = [];
    private readonly Dictionary<string, LogonTask> tasks = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Operations => operations;

    public void Seed(LogonTask task) => tasks[task.Name] = task;

    public LogonTask Only() => Assert.Single(tasks.Values);

    public bool Contains(string taskName) => tasks.ContainsKey(taskName);

    public void Create(LogonTask task)
    {
        if (!tasks.TryAdd(task.Name, task))
            throw new InvalidOperationException($"Task '{task.Name}' already exists.");

        operations.Add("create");
    }

    public void Replace(LogonTask task)
    {
        if (!tasks.ContainsKey(task.Name))
            throw new InvalidOperationException($"Task '{task.Name}' does not exist.");

        tasks[task.Name] = task;
        operations.Add("replace");
    }

    public void Delete(string taskName)
    {
        if (!tasks.Remove(taskName))
            throw new InvalidOperationException($"Task '{taskName}' does not exist.");

        operations.Add("delete");
    }
}
