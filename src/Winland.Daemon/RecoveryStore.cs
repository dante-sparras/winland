namespace Winland;

sealed class RecoveryStore
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private bool _armed;
    private bool _closed;
    private string? _written;

    public RecoveryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public RecoverySnapshot? Read()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            var json = File.ReadAllText(_path);
            return RecoverySnapshot.TryParse(json, out var snapshot) ? snapshot : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not read '{_path}'.");
            return null;
        }
    }

    public void Arm()
    {
        lock (_gate)
            _armed = true;
    }

    public void Save(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var json = snapshot.ToJson();
        lock (_gate)
        {
            if (!_armed || _closed || json == _written)
                return;

            try
            {
                WriteAtomic(json);
                _written = json;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not write '{_path}'.");
            }
        }
    }

    public void Delete()
    {
        lock (_gate)
        {
            _closed = true;
            _armed = false;
            TryDelete(_path);
            TryDelete(_path + ".tmp");
            _written = null;
        }
    }

    private void WriteAtomic(string json)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(_path))
            File.Replace(temp, _path, destinationBackupFileName: null);
        else
            File.Move(temp, _path);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
