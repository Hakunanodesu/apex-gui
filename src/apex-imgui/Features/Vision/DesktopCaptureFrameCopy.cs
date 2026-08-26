using System.Runtime.InteropServices;

internal static unsafe class DesktopCaptureFrameCopy
{
    private const float WeaponRoiBaseWidth = 1920f;
    private const float WeaponRoiOffsetX = 384f;
    private const float WeaponRoiOffsetY = 122f;
    private const float WeaponRoiBaseWidthPixels = WeaponTemplateCatalog.TemplateWidth;
    private const float WeaponRoiBaseHeightPixels = WeaponTemplateCatalog.TemplateHeight;

    public static void CopyMappedFrame(
        nint dataPointer,
        int rowPitch,
        int outputWidth,
        int outputHeight,
        int captureLeft,
        int captureTop,
        int captureWidth,
        int captureHeight,
        byte[] frameBuffer,
        bool captureWeaponRoi,
        ref byte[] weaponRoiBuffer,
        out byte[] weaponRoiData,
        out int weaponRoiWidth,
        out int weaponRoiHeight)
    {
        weaponRoiData = Array.Empty<byte>();
        weaponRoiWidth = 0;
        weaponRoiHeight = 0;

        var rowBytes = captureWidth * 4;
        for (var y = 0; y < captureHeight; y++)
        {
            var sourceY = captureTop + y;
            var sourceOffset = sourceY * rowPitch + captureLeft * 4;
            var source = dataPointer + sourceOffset;
            var destination = y * rowBytes;
            Marshal.Copy(source, frameBuffer, destination, rowBytes);
        }

        var (roiLeft, roiTop, roiWidth, roiHeight) = CalcWeaponRoi(outputWidth, outputHeight);
        if (!captureWeaponRoi || roiWidth <= 0 || roiHeight <= 0)
        {
            return;
        }

        var requiredBytes = roiWidth * roiHeight * 3;
        if (weaponRoiBuffer.Length != requiredBytes)
        {
            weaponRoiBuffer = new byte[requiredBytes];
        }

        var srcPtr = (byte*)dataPointer;
        for (var y = 0; y < roiHeight; y++)
        {
            var sourceY = roiTop + y;
            var sourceOffset = sourceY * rowPitch + roiLeft * 4;
            var destination = y * roiWidth * 3;
            for (var x = 0; x < roiWidth; x++)
            {
                var pixelOffset = sourceOffset + x * 4;
                weaponRoiBuffer[destination + x * 3 + 0] = srcPtr[pixelOffset + 2];
                weaponRoiBuffer[destination + x * 3 + 1] = srcPtr[pixelOffset + 1];
                weaponRoiBuffer[destination + x * 3 + 2] = srcPtr[pixelOffset + 0];
            }
        }

        weaponRoiData = weaponRoiBuffer;
        weaponRoiWidth = roiWidth;
        weaponRoiHeight = roiHeight;
    }

    public static (int Left, int Top, int Width, int Height) CalcWeaponRoi(int frameWidth, int frameHeight)
    {
        var scale = frameWidth / WeaponRoiBaseWidth;
        var left = frameWidth - (int)MathF.Round(WeaponRoiOffsetX * scale);
        var top = frameHeight - (int)MathF.Round(WeaponRoiOffsetY * scale);
        var width = (int)MathF.Round(WeaponRoiBaseWidthPixels * scale);
        var height = (int)MathF.Round(WeaponRoiBaseHeightPixels * scale);

        left = Math.Clamp(left, 0, frameWidth);
        top = Math.Clamp(top, 0, frameHeight);
        width = Math.Clamp(width, 0, frameWidth - left);
        height = Math.Clamp(height, 0, frameHeight - top);
        return (left, top, width, height);
    }
}
