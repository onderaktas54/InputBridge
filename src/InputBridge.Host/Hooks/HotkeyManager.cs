using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace InputBridge.Host.Hooks;

public sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int HOTKEY_ID_HOST = 1;
    public const int HOTKEY_ID_CLIENT = 2;
    public const int HOTKEY_ID_EMERGENCY = 99;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private uint VK_HOST = 0x31; // '1'
    private uint VK_CLIENT = 0x32; // '2'
    private uint VK_ESCAPE = 0x1B;

    private uint MOD_HOST = MOD_CONTROL | MOD_WIN;
    private uint MOD_CLIENT = MOD_CONTROL | MOD_WIN;
    private uint MOD_EMERGENCY = MOD_CONTROL | MOD_ALT;

    public event Action? SwitchToHost;
    public event Action<int>? SwitchToClient;
    public event Action? EmergencyRelease;

    private bool _isRegistered;
    
    // In WPF, we can hook into the thread's message loop if we don't have a specific window handle
    
    public void ReRegister(string hostKey, string clientKey, string emergencyKey)
    {
        Unregister();
        ParseHotkey(hostKey, out MOD_HOST, out VK_HOST, MOD_CONTROL | MOD_ALT, 0x31);
        ParseHotkey(clientKey, out MOD_CLIENT, out VK_CLIENT, MOD_CONTROL | MOD_ALT, 0x32);
        ParseHotkey(emergencyKey, out MOD_EMERGENCY, out VK_ESCAPE, MOD_CONTROL | MOD_ALT, 0x1B);
        Register();
    }

    public void Register()
    {
        if (_isRegistered) return;

        // Note: For WPF without a specific window, passing IntPtr.Zero registers hotkeys 
        // to the thread. We must intercept them in the application message pump.
        
        RegisterHotKey(IntPtr.Zero, HOTKEY_ID_HOST, MOD_HOST, VK_HOST);
        RegisterHotKey(IntPtr.Zero, HOTKEY_ID_CLIENT, MOD_CLIENT, VK_CLIENT);
        RegisterHotKey(IntPtr.Zero, HOTKEY_ID_EMERGENCY, MOD_EMERGENCY, VK_ESCAPE);

        ComponentDispatcher.ThreadPreprocessMessage += ComponentDispatcher_ThreadPreprocessMessage;
        _isRegistered = true;
    }

    private void ParseHotkey(string input, out uint modifier, out uint key, uint defaultMod, uint defaultKey)
    {
        modifier = 0;
        key = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            modifier = defaultMod;
            key = defaultKey;
            return;
        }

        var parts = input.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.Equals("ctrl", StringComparison.OrdinalIgnoreCase)) modifier |= MOD_CONTROL;
            else if (part.Equals("alt", StringComparison.OrdinalIgnoreCase)) modifier |= MOD_ALT;
            else if (part.Equals("shift", StringComparison.OrdinalIgnoreCase)) modifier |= MOD_SHIFT;
            else if (part.Equals("win", StringComparison.OrdinalIgnoreCase)) modifier |= MOD_WIN;
            else
            {
                // Try parse virtual key
                if (Enum.TryParse<System.Windows.Forms.Keys>(part, true, out var parsedKey))
                {
                    key = (uint)parsedKey;
                }
                else if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                {
                    key = char.ToUpper(part[0]);
                }
            }
        }

        if (key == 0) // Parse failed, fallback
        {
            modifier = defaultMod;
            key = defaultKey;
        }
    }

    private void ComponentDispatcher_ThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg.message == WM_HOTKEY)
        {
            int id = msg.wParam.ToInt32();
            switch (id)
            {
                case HOTKEY_ID_HOST:
                    SwitchToHost?.Invoke();
                    handled = true;
                    break;
                case HOTKEY_ID_CLIENT:
                    SwitchToClient?.Invoke(0);
                    handled = true;
                    break;
                case HOTKEY_ID_EMERGENCY:
                    EmergencyRelease?.Invoke();
                    handled = true;
                    break;
            }
        }
    }

    public void Unregister()
    {
        if (!_isRegistered) return;

        ComponentDispatcher.ThreadPreprocessMessage -= ComponentDispatcher_ThreadPreprocessMessage;

        UnregisterHotKey(IntPtr.Zero, HOTKEY_ID_HOST);
        UnregisterHotKey(IntPtr.Zero, HOTKEY_ID_CLIENT);
        UnregisterHotKey(IntPtr.Zero, HOTKEY_ID_EMERGENCY);

        _isRegistered = false;
    }

    public void Dispose()
    {
        Unregister();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_CONTROL_KEY = 0x11;
    private const int VK_MENU_KEY = 0x12; // Alt
    private const int VK_SHIFT_KEY = 0x10;
    private const int VK_LWIN_KEY = 0x5B;
    private const int VK_RWIN_KEY = 0x5C;

    public bool IsRegisteredHotkey(int vkCode)
    {
        uint currentMods = 0;
        if ((GetAsyncKeyState(VK_CONTROL_KEY) & 0x8000) != 0) currentMods |= MOD_CONTROL;
        if ((GetAsyncKeyState(VK_MENU_KEY) & 0x8000) != 0) currentMods |= MOD_ALT;
        if ((GetAsyncKeyState(VK_SHIFT_KEY) & 0x8000) != 0) currentMods |= MOD_SHIFT;
        if (((GetAsyncKeyState(VK_LWIN_KEY) & 0x8000) != 0) || ((GetAsyncKeyState(VK_RWIN_KEY) & 0x8000) != 0)) currentMods |= MOD_WIN;

        if (vkCode == VK_HOST && currentMods == MOD_HOST) return true;
        if (vkCode == VK_CLIENT && currentMods == MOD_CLIENT) return true;
        if (vkCode == VK_ESCAPE && currentMods == MOD_EMERGENCY) return true;
        return false;
    }
}
