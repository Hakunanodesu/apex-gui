internal interface IScreenCapturer : IDisposable
{
    void SetCaptureRegion(int width, int height);

    bool TryWaitForFrame(int timeoutMs);

    bool TryCaptureFrame(
        int timeoutMs,
        bool captureWeaponRoi,
        out byte[] frameData,
        out int width,
        out int height,
        out byte[] weaponRoiData,
        out int weaponRoiWidth,
        out int weaponRoiHeight,
        out string? error);
}
