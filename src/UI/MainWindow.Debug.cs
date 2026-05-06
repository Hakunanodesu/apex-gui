#if DEBUG
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenTK.Windowing.Common;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

public sealed partial class MainWindow
{
    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Keys.Home || e.IsRepeat)
        {
            return;
        }

        SaveLatestWeaponSobelToTemplate();
    }

    private void SaveLatestWeaponSobelToTemplate()
    {
        var worker = _weaponRecWorker;
        if (worker is null)
        {
            return;
        }

        byte[] sobel = Array.Empty<byte>();
        var lastFrameId = 0;
        if (!worker.TryCopyLatestSobel(ref sobel, ref lastFrameId, out var width, out var height) ||
            width <= 0 ||
            height <= 0 ||
            sobel.Length != width * height)
        {
            return;
        }

        var outputPath = Path.Combine(ContentRootDirectory, "WeaponTemplates", "new.png");
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var bgra = new byte[sobel.Length * 4];
        for (var i = 0; i < sobel.Length; i++)
        {
            var gray = sobel[i];
            var dst = i * 4;
            bgra[dst + 0] = gray;
            bgra[dst + 1] = gray;
            bgra[dst + 2] = gray;
            bgra[dst + 3] = 255;
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(outputPath, ImageFormat.Png);
    }
}
#endif
