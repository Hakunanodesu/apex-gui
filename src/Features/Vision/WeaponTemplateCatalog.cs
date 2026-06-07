using System.Reflection;
using StbImageSharp;

internal readonly record struct WeaponTemplateEntry(string GameFolder, string Name, int Width, int Height, byte[] GrayPixels);

internal static class WeaponTemplateCatalog
{
    public const int TemplateWidth = 160;
    public const int TemplateHeight = 40;
    public const float EmptyHandSsimThreshold = 0.4f;
    public const string EmptyHandName = "empty";

    public static readonly string[] GameOptions = { "Apex Legends", "The Finals" };

    private static readonly Dictionary<string, string> GameFolderByDisplayName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Apex Legends"] = "apexlegends",
        ["The Finals"] = "thefinals",
    };

    private static readonly Lazy<IReadOnlyList<WeaponTemplateEntry>> CachedTemplates = new(LoadEmbeddedTemplatesInternal);

    public static IReadOnlyList<WeaponTemplateEntry> LoadEmbeddedTemplates()
    {
        return CachedTemplates.Value;
    }

    public static string GetGameFolder(string gameDisplayName)
    {
        return GameFolderByDisplayName.TryGetValue(gameDisplayName, out var folder)
            ? folder
            : GameFolderByDisplayName["Apex Legends"];
    }

    public static int ResolveGameIndex(string? gameDisplayName, int fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(gameDisplayName))
        {
            return fallback;
        }

        for (var i = 0; i < GameOptions.Length; i++)
        {
            if (string.Equals(GameOptions[i], gameDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return fallback;
    }

    public static string[] GetWeaponNamesForGame(string gameDisplayName)
    {
        var gameFolder = GetGameFolder(gameDisplayName);
        return CachedTemplates.Value
            .Where(entry => string.Equals(entry.GameFolder, gameFolder, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<WeaponTemplateEntry> LoadEmbeddedTemplatesInternal()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var entries = new List<WeaponTemplateEntry>();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                resourceName.IndexOf("WeaponTemplates", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (!TryExtractGameAndWeaponName(resourceName, out var gameFolder, out var weaponName))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                continue;
            }

            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlue);
            if (image.Width != TemplateWidth || image.Height != TemplateHeight)
            {
                continue;
            }

            var gray = new byte[TemplateWidth * TemplateHeight];
            for (var i = 0; i < gray.Length; i++)
            {
                var rgbIndex = i * 3;
                var r = image.Data[rgbIndex + 0];
                var g = image.Data[rgbIndex + 1];
                var b = image.Data[rgbIndex + 2];
                gray[i] = ToGray(r, g, b);
            }

            entries.Add(new WeaponTemplateEntry(gameFolder, weaponName, TemplateWidth, TemplateHeight, gray));
        }

        entries.Sort((a, b) =>
        {
            var gameCompare = string.Compare(a.GameFolder, b.GameFolder, StringComparison.OrdinalIgnoreCase);
            return gameCompare != 0
                ? gameCompare
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return entries;
    }

    private static bool TryExtractGameAndWeaponName(string resourceName, out string gameFolder, out string weaponName)
    {
        gameFolder = string.Empty;
        weaponName = string.Empty;
        var normalized = resourceName.Replace('\\', '/');
        const string marker = "WeaponTemplates";
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var suffix = normalized[(markerIndex + marker.Length)..].TrimStart('/', '.');
        suffix = suffix.Replace('/', '.');
        if (suffix.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            suffix = suffix[..^4];
        }

        var parts = suffix.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        gameFolder = parts[0];
        weaponName = string.Join('.', parts.Skip(1));
        return !string.IsNullOrWhiteSpace(gameFolder) && !string.IsNullOrWhiteSpace(weaponName);
    }

    private static byte ToGray(byte r, byte g, byte b)
    {
        return (byte)Math.Clamp((int)MathF.Round(0.299f * r + 0.587f * g + 0.114f * b), 0, 255);
    }
}
