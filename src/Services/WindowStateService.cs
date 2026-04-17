internal sealed class WindowStateService
{
    public bool TryLoad(string filePath, out WindowStateSnapshot snapshot)
    {
        snapshot = new WindowStateSnapshot();
        try
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            var lines = File.ReadAllLines(filePath);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('['))
                {
                    continue;
                }

                var equalIndex = line.IndexOf('=');
                if (equalIndex <= 0 || equalIndex >= line.Length - 1)
                {
                    continue;
                }

                values[line[..equalIndex].Trim()] = line[(equalIndex + 1)..].Trim();
            }

            if (!values.TryGetValue("Width", out var widthRaw) || !int.TryParse(widthRaw, out var width) || width <= 0)
            {
                return false;
            }

            if (!values.TryGetValue("Height", out var heightRaw) || !int.TryParse(heightRaw, out var height) || height <= 0)
            {
                return false;
            }

            var isMaximized = values.TryGetValue("IsMaximized", out var maximizedRaw)
                && bool.TryParse(maximizedRaw, out var parsedMaximized)
                && parsedMaximized;

            snapshot = new WindowStateSnapshot
            {
                Width = width,
                Height = height,
                IsMaximized = isMaximized
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Save(string filePath, WindowStateSnapshot snapshot)
    {
        var content = string.Join(
            Environment.NewLine,
            "[WindowState]",
            $"Width={snapshot.Width}",
            $"Height={snapshot.Height}",
            $"IsMaximized={snapshot.IsMaximized}") + Environment.NewLine;
        File.WriteAllText(filePath, content);
    }
}
