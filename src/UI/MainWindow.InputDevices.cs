public sealed partial class MainWindow
{
    private void MirrorSelectedGamepadToVirtualGamepad()
    {
        _smartCoreMappingService.Update(
            _dependencyService.IsViGemBusReady(),
            _homeSelectedGamepadIndex,
            _cachedConnectedGamepads,
            _sdlGamepadWorker,
            _viGEmMappingWorker,
            _smartCoreMappingState);

        if (_smartCoreMappingState.EffectiveSelectedIndex >= 0)
        {
            _homeSelectedGamepadIndex = _smartCoreMappingState.EffectiveSelectedIndex;
        }
    }

    private void RefreshHomeInputDevices()
    {
        RefreshInputDevicesCore(ref _homeSelectedGamepadIndex, shouldResetVirtualMappingHandle: false, forceRefresh: true);
    }

    private void RefreshDebugInputDevices()
    {
        RefreshInputDevicesCore(ref _debugSelectedGamepadIndex, shouldResetVirtualMappingHandle: true, forceRefresh: true);
    }

    private void RefreshInputDevicesCore(ref int selectedIndex, bool shouldResetVirtualMappingHandle, bool forceRefresh)
    {
        UpdateConnectedGamepadCache(forceRefresh);
        selectedIndex = _inputDeviceService.NormalizeSelectedIndex(selectedIndex, _cachedGamepadOptions.Length);
        if (shouldResetVirtualMappingHandle)
        {
            _sdlGamepadWorker?.SetSelectedGamepad(null);
            _smartCoreMappingState.LastError = string.Empty;
        }
    }

    private string[] GetConnectedGamepadOptions()
    {
        if (_cachedGamepadOptions.Length == 0)
        {
            return Array.Empty<string>();
        }

        return _cachedGamepadOptions;
    }

    private void UpdateConnectedGamepadCache(bool forceRefresh = false)
    {
        _cachedConnectedGamepads = _inputDeviceService.GetConnectedGamepads(_sdlGamepadWorker, forceRefresh);
        _cachedGamepadOptions = _inputDeviceService.BuildGamepadOptions(_cachedConnectedGamepads);
    }
}
