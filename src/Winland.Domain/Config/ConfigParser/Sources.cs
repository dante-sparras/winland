namespace Winland.Domain;

public static partial class ConfigParser
{
    private sealed partial class Parser
    {
        private void HandleSource(string path, int lineNumber)
        {
            if (path.Length == 0)
            {
                Warn(lineNumber, "Malformed line.");
                return;
            }

            path = SourceKey(SourcePath.Expand(path, Variable));
            if (SourcePath.HasGlob(path))
            {
                var matches = sourceReader?.Match(path) ?? [];
                if (matches.Count == 0)
                {
                    _sources.Add(path);
                    Warn(lineNumber, $"Could not read source '{path}'.");
                    return;
                }

                // A glob in the config's own folder also matches the file it is written in,
                // and every file that is already being read is passed over without a warning.
                foreach (var match in matches)
                {
                    if (!_openSources.Contains(SourceKey(match)))
                        IncludeSource(match, lineNumber);
                }

                return;
            }

            IncludeSource(path, lineNumber);
        }

        private void IncludeSource(string path, int lineNumber)
        {
            _sources.Add(path);
            if (sourceReader?.Read(path) is not string text)
            {
                Warn(lineNumber, $"Could not read source '{path}'.");
                return;
            }

            var key = SourceKey(path);
            if (!_openSources.Add(key))
            {
                Warn(lineNumber, $"Source '{path}' includes itself.");
                return;
            }

            var depth = _categories.Count;
            var previous = _source;
            var floor = _floor;
            _source = key;
            _floor = depth;
            try
            {
                ParseLines(text);
            }
            finally
            {
                _source = previous;
                _floor = floor;
                _openSources.Remove(key);
            }

            WarnUnclosed(_categories.Count - depth);
        }

        // Windows paths are compared without case, after a relative path is joined to the
        // current file's folder and the result is normalized, so one file has one key however it is named.
        private string SourceKey(string path)
        {
            var folder = _source is null ? _rootFolder : Path.GetDirectoryName(_source);
            var joined = Path.IsPathFullyQualified(path) || string.IsNullOrEmpty(folder) ? path : Path.Join(folder, path);
            if (!Path.IsPathFullyQualified(joined))
                return joined.Replace('/', '\\');

            try
            {
                return Path.GetFullPath(joined);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return joined;
            }
        }

        private void WarnUnclosed(int extra)
        {
            while (extra > 0)
            {
                var category = _categories[^1];
                var label = category.Key is null ? category.Name : $"{category.Name}[{category.Key}]";
                _warnings.Add(new ConfigWarning(category.Line, $"Unclosed category '{label}'.", category.Source));
                _categories.RemoveAt(_categories.Count - 1);
                extra--;
            }
        }

        private string? Variable(string name) => sourceReader?.Variable(name);
    }
}
