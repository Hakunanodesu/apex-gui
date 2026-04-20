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
            uint? selectedGamepadInstanceId = null;
            if (values.TryGetValue("SelectedGamepadInstanceId", out var selectedInstanceIdRaw)
                && uint.TryParse(selectedInstanceIdRaw, out var parsedInstanceId))
            {
                selectedGamepadInstanceId = parsedInstanceId;
            }

            snapshot = new WindowStateSnapshot
            {
                Width = width,
                Height = height,
                IsMaximized = isMaximized,
                SelectedGamepadInstanceId = selectedGamepadInstanceId
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
            $"IsMaximized={snapshot.IsMaximized}",
            $"SelectedGamepadInstanceId={(snapshot.SelectedGamepadInstanceId.HasValue ? snapshot.SelectedGamepadInstanceId.Value.ToString() : string.Empty)}") + Environment.NewLine;
        File.WriteAllText(filePath, content);
    }
}

internal sealed class WindowStateSnapshot
{
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsMaximized { get; init; }
    public uint? SelectedGamepadInstanceId { get; init; }
}
