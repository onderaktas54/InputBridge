using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;

namespace InputBridge.Client.Simulation;

public sealed class KeyboardSimulator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT Ki;
        [FieldOffset(0)] public MOUSEINPUT Mi;
        [FieldOffset(0)] public HARDWAREINPUT Hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Msg;
        public ushort ParamL;
        public ushort ParamH;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    private readonly ConcurrentDictionary<int, bool> _pressedKeys = new();

    public void SimulateKeyDown(int virtualKeyCode)
    {
        _pressedKeys[virtualKeyCode] = true;
        SendKeyboardInput((ushort)virtualKeyCode, isKeyDown: true);
    }

    public void SimulateKeyUp(int virtualKeyCode)
    {
        _pressedKeys.TryRemove(virtualKeyCode, out _);
        SendKeyboardInput((ushort)virtualKeyCode, isKeyDown: false);
    }

    public void ReleaseAllKeys()
    {
        foreach (var key in _pressedKeys.Keys.ToArray())
        {
            SimulateKeyUp(key);
        }
    }

    private void SendKeyboardInput(ushort virtualKeyCode, bool isKeyDown)
    {
        ushort scanCode = (ushort)MapVirtualKey(virtualKeyCode, MAPVK_VK_TO_VSC);

        uint flags = 0; // KeyDown
        if (!isKeyDown)
        {
            flags |= KEYEVENTF_KEYUP;
        }

        // We can send both VK and ScanCode for maximum compatibility with games.
        // If we strictly use ScanCode, we set KEYEVENTF_SCANCODE indicator.
        // For general usage, keeping VK and ScanCode together is often fine.

        var input = new INPUT
        {
            Type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                Ki = new KEYBDINPUT
                {
                    Vk = virtualKeyCode,
                    Scan = scanCode,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}
