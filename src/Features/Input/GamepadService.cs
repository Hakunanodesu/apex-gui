internal sealed class GamepadService
{
    public (uint InstanceId, string Name)[] GetConnectedGamepads(SdlGamepadWorker? sdlGamepadWorker, bool forceRefresh)
    {
        return sdlGamepadWorker?.GetConnectedGamepads(forceRefresh) ?? Array.Empty<(uint InstanceId, string Name)>();
    }

    public string[] BuildGamepadOptions((uint InstanceId, string Name)[] gamepads)
    {
        if (gamepads.Length == 0)
        {
            return Array.Empty<string>();
        }

        var options = new string[gamepads.Length];
        for (var i = 0; i < gamepads.Length; i++)
        {
            options[i] = gamepads[i].Name;
        }

        return options;
    }

    public int NormalizeSelectedIndex(int selectedIndex, int optionCount)
    {
        return optionCount > 0
            ? (selectedIndex >= 0 && selectedIndex < optionCount ? selectedIndex : 0)
            : -1;
    }
}
