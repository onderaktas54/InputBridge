using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;

namespace InputBridge.Client.Simulation;

public sealed class MouseSimulator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private readonly ConcurrentDictionary<int, bool> _pressedButtons = new();

    public void ReleaseAllButtons()
    {
        foreach (var key in _pressedButtons.Keys.ToArray())
        {
            SimulateMouseButton(key, isDown: false);
        }
    }

    public void ResetPosition()
    {
        // For client side, we usually don't need to force position for disconnect cleanup unless requested
        // Or we could move to center if we had screen metrics. For now empty layout or minimal cleanup.
    }

    public void SimulateMouseMove(int deltaX, int deltaY)
    {
        var input = new INPUT
        {
            Type = INPUT_MOUSE,
            U = new InputUnion
            {
                Mi = new MOUSEINPUT
                {
                    Dx = deltaX,
                    Dy = deltaY,
                    Flags = MOUSEEVENTF_MOVE,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void SimulateMouseButton(int buttonId, bool isDown)
    {
        if (isDown)
            _pressedButtons[buttonId] = true;
        else
            _pressedButtons.TryRemove(buttonId, out _);

        uint flags = 0;
        uint mouseData = 0;

        switch (buttonId)
        {
            case 0: // Left
                flags = isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
                break;
            case 1: // Right
                flags = isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
                break;
            case 2: // Middle
                flags = isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
                break;
            case 3: // XButton1
                flags = isDown ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP;
                mouseData = 1; // XBUTTON1
                break;
            case 4: // XButton2
                flags = isDown ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP;
                mouseData = 2; // XBUTTON2
                break;
        }

        if (flags == 0) return;

        var input = new INPUT
        {
            Type = INPUT_MOUSE,
            U = new InputUnion
            {
                Mi = new MOUSEINPUT
                {
                    MouseData = mouseData,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void SimulateScroll(int wheelDelta)
    {
        var input = new INPUT
        {
            Type = INPUT_MOUSE,
            U = new InputUnion
            {
                Mi = new MOUSEINPUT
                {
                    MouseData = (uint)wheelDelta,
                    Flags = MOUSEEVENTF_WHEEL,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}
