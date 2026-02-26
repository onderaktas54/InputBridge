using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using InputBridge.Core.Protocol;

namespace InputBridge.Host.Hooks;

public sealed class KeyboardHook : IDisposable
{
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;
    
    private bool _isRemoteMode = false;
    private uint _nextSeq = 1;

    public event Action<InputPacket>? KeyEvent;

    public Func<int, bool>? IsRegisteredHotkey { get; set; }

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void SetRemoteMode(bool isRemoteMode)
    {
        _isRemoteMode = isRemoteMode;
        if (!isRemoteMode)
        {
            ReleaseModifierKeys();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
    
    private const byte KEYEVENTF_KEYUP = 0x02;
    private const byte VK_SHIFT_KEY = 0x10;
    private const byte VK_LWIN_KEY = 0x5B;

    private void ReleaseModifierKeys()
    {
        // Release modifier keys that might be stuck
        keybd_event((byte)VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
        keybd_event((byte)VK_MENU, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_SHIFT_KEY, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_LWIN_KEY, 0, KEYEVENTF_KEYUP, 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();
            if (message == WM_KEYDOWN || message == WM_KEYUP || message == WM_SYSKEYDOWN || message == WM_SYSKEYUP)
            {
                var vkCode = Marshal.ReadInt32(lParam);

                var packet = new InputPacket
                {
                    Version = 1,
                    Type = (message == WM_KEYDOWN || message == WM_SYSKEYDOWN) ? InputType.KeyDown : InputType.KeyUp,
                    Data1 = vkCode,
                    Data2 = 0,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    SequenceNumber = unchecked(_nextSeq++)
                };

                KeyEvent?.Invoke(packet);

                if (_isRemoteMode)
                {
                    if (IsRegisteredHotkey != null && IsRegisteredHotkey(vkCode))
                    {
                        // It's a mode switch hotkey, let it pass to the OS HotkeyManager
                        return CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    // Block the input from reaching the OS
                    return (IntPtr)1; 
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        
        if (curModule?.ModuleName != null)
        {
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);

            if (_hookId == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Uninstall();
    }
}
