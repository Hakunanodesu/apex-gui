using System.Runtime.InteropServices;

internal sealed partial class ViGEmMappingWorker
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFScancode = 0x0008;

    private void UpdateKeyboardInjections(HashSet<ushort> desiredVirtualKeys, HashSet<ushort> injectedVirtualKeys)
    {
        if (injectedVirtualKeys.Count == desiredVirtualKeys.Count)
        {
            var isSameSet = true;
            foreach (var key in desiredVirtualKeys)
            {
                if (!injectedVirtualKeys.Contains(key))
                {
                    isSameSet = false;
                    break;
                }
            }

            if (isSameSet)
            {
                return;
            }
        }

        var releaseKeys = new List<ushort>();
        foreach (var key in injectedVirtualKeys)
        {
            if (!desiredVirtualKeys.Contains(key))
            {
                releaseKeys.Add(key);
            }
        }

        foreach (var key in releaseKeys)
        {
            if (!TrySendKeyboardKey(key, keyDown: false, out var releaseError))
            {
                if (!string.IsNullOrWhiteSpace(releaseError))
                {
                    lock (_sync)
                    {
                        _lastError = releaseError;
                    }
                }
                continue;
            }

            injectedVirtualKeys.Remove(key);
        }

        foreach (var key in desiredVirtualKeys)
        {
            if (injectedVirtualKeys.Contains(key))
            {
                continue;
            }

            if (!TrySendKeyboardKey(key, keyDown: true, out var pressError))
            {
                if (!string.IsNullOrWhiteSpace(pressError))
                {
                    lock (_sync)
                    {
                        _lastError = pressError;
                    }
                }
                continue;
            }

            injectedVirtualKeys.Add(key);
        }
    }

    private static bool TrySendKeyboardKey(ushort virtualKey, bool keyDown, out string? error)
    {
        var scanCode = (ushort)MapVirtualKey(virtualKey, 0);
        var flags = KeyEventFScancode | (keyDown ? 0u : KeyEventFKeyUp);
        var input = new INPUT
        {
            Type = InputKeyboard,
            Data = new InputData
            {
                Keyboard = new KEYBDINPUT
                {
                    // Scancode injection is closer to real key hardware events.
                    VirtualKey = 0,
                    ScanCode = scanCode,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };

        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 1)
        {
            error = null;
            return true;
        }

        var win32Error = Marshal.GetLastWin32Error();
        error = $"键盘按键注入失败(VK={virtualKey}): {win32Error}";
        return false;
    }

    private static ushort? ResolveCustomKeyboardVirtualKey(string? customKey)
    {
        return GamepadBindingCatalog.TryResolveCustomKeyboardVirtualKey(customKey, out var virtualKey, out _)
            ? virtualKey
            : null;
    }

    private static void AddResolvedCustomKeyboardVirtualKey(HashSet<ushort> keys, string? customKey)
    {
        var virtualKey = ResolveCustomKeyboardVirtualKey(customKey);
        if (virtualKey.HasValue)
        {
            keys.Add(virtualKey.Value);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;

        [FieldOffset(0)]
        public MOUSEINPUT Mouse;

        [FieldOffset(0)]
        public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Msg;
        public ushort ParamL;
        public ushort ParamH;
    }
}
