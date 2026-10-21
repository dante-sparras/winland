namespace Winland.Core;

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

            path = SourcePath.Expand(path);
            if (SourcePath.HasGlob(path))
            {
                var matches = sourceReader?.Match(path) ?? [];
                if (matches.Count == 0)
                {
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
            _source = path;
            try
            {
                ParseLines(text);
            }
            finally
            {
                _source = previous;
                _openSources.Remove(key);
            }

            WarnUnclosed(_categories.Count - depth);
        }

        // Windows paths are compared without case, after a relative path is joined to the
        // config's folder and the result is normalized, so one file has one key however it is named.
        private string SourceKey(string path)
        {
            var joined = Path.IsPathFullyQualified(path) || _rootFolder is null ? path : Path.Join(_rootFolder, path);
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

        private void ApplyDirective(ConfigDirective directive, int lineNumber)
        {
            switch (directive.Kind)
            {
                case ConfigDirectiveKind.EndIf:
                    if (_ifFailed.Count == 0)
                        Warn(lineNumber, "Stray endif.");
                    else
                        _ifFailed.RemoveAt(_ifFailed.Count - 1);
                    break;
                case ConfigDirectiveKind.If:
                    if (directive.Argument.Length == 0)
                    {
                        Warn(lineNumber, "Malformed conditional.");
                        break;
                    }

                    var truthy = IsTruthy(directive.Argument);
                    _ifFailed.Add(directive.Negated ? truthy : !truthy);
                    break;
                case ConfigDirectiveKind.NoError:
                    _suppressWarnings = directive.Enabled;
                    break;
            }
        }

        private bool IsTruthy(string name)
        {
            var environment = Environment.GetEnvironmentVariable(name);
            if (environment is not null)
                return environment.Length > 0;

            return _variables.TryGetValue(name, out var value) && value.Length > 0;
        }

        private bool Skipping => _ifFailed.Contains(true);
    }
}
