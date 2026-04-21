using System.Reflection;
using StbImageSharp;

internal readonly record struct WeaponTemplateEntry(string Name, int Width, int Height, byte[] GrayPixels);

internal static class WeaponTemplateCatalog
{
    public const int TemplateWidth = 160;
    public const int TemplateHeight = 40;
    public const float EmptyHandSsimThreshold = 0.4f;
    public const string EmptyHandName = "empty";

    private static readonly Lazy<IReadOnlyList<WeaponTemplateEntry>> CachedTemplates = new(LoadEmbeddedTemplatesInternal);

    public static IReadOnlyList<WeaponTemplateEntry> LoadEmbeddedTemplates()
    {
        return CachedTemplates.Value;
    }

    public static string[] GetWeaponNames()
    {
        return CachedTemplates.Value
            .Select(t => t.Name)
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

            var name = ExtractTemplateName(resourceName);
            if (string.IsNullOrWhiteSpace(name))
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

            entries.Add(new WeaponTemplateEntry(name, TemplateWidth, TemplateHeight, gray));
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    private static string ExtractTemplateName(string resourceName)
    {
        var normalized = resourceName.Replace('\\', '/');
        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex + 1 < normalized.Length)
        {
            normalized = normalized[(slashIndex + 1)..];
        }

        var pngIndex = normalized.LastIndexOf(".png", StringComparison.OrdinalIgnoreCase);
        if (pngIndex > 0)
        {
            normalized = normalized[..pngIndex];
        }

        // Handle default manifest name style: apex_imgui.WeaponTemplates.wingman.png
        var dotPngIndex = resourceName.LastIndexOf(".png", StringComparison.OrdinalIgnoreCase);
        if (dotPngIndex > 0)
        {
            var beforePng = resourceName[..dotPngIndex];
            var lastDot = beforePng.LastIndexOf('.');
            if (lastDot >= 0 && lastDot + 1 < beforePng.Length)
            {
                normalized = beforePng[(lastDot + 1)..];
            }
        }

        return normalized.Trim();
    }

    private static byte ToGray(byte r, byte g, byte b)
    {
        return (byte)Math.Clamp((int)MathF.Round(0.299f * r + 0.587f * g + 0.114f * b), 0, 255);
    }
}
