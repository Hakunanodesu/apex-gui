internal sealed class SmartCoreMappingService
{
    public void Update(
        bool isViGemBusReady,
        int selectedIndex,
        (uint InstanceId, string Name)[] gamepads,
        SdlGamepadWorker? sdlGamepadWorker,
        ViGEmMappingWorker? viGEmMappingWorker,
        SmartCoreMappingState state)
    {
        state.IsViGemBusReady = isViGemBusReady;
        state.HasInputDevice = gamepads.Length > 0;
        state.IsEnabled = state.RequestedEnabled && state.IsDependenciesReady;
        state.IsMappingActive = false;
        state.EffectiveSelectedIndex = -1;
        state.EffectiveSelectedInstanceId = null;
        state.LastError = string.Empty;

        if (!state.IsDependenciesReady)
        {
            state.RequestedEnabled = false;
            state.IsEnabled = false;
            sdlGamepadWorker?.SetSelectedGamepad(null);
            return;
        }

        if (viGEmMappingWorker is null || !viGEmMappingWorker.IsConnected)
        {
            sdlGamepadWorker?.SetSelectedGamepad(null);
            return;
        }

        if (!state.IsEnabled)
        {
            sdlGamepadWorker?.SetSelectedGamepad(null);
            return;
        }

        var safeIndex = selectedIndex >= 0 && selectedIndex < gamepads.Length ? selectedIndex : 0;
        var selected = gamepads[safeIndex];
        sdlGamepadWorker?.SetSelectedGamepad(selected.InstanceId);

        state.IsMappingActive = true;
        state.EffectiveSelectedIndex = safeIndex;
        state.EffectiveSelectedInstanceId = selected.InstanceId;

        var mappingError = viGEmMappingWorker.GetLastError();
        state.LastError = string.IsNullOrWhiteSpace(mappingError) ? string.Empty : mappingError;
    }
}
