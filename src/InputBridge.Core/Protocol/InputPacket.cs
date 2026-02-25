using System;
using System.Runtime.InteropServices;

namespace InputBridge.Core.Protocol;

public enum InputType : byte
{
    KeyDown = 0,
    KeyUp = 1,
    MouseMove = 2,
    MouseButtonDown = 3,
    MouseButtonUp = 4,
    MouseScroll = 5,
    Heartbeat = 10,
    HandshakeRequest = 20,
    HandshakeResponse = 21,
    SwitchNotify = 30
}

[Flags]
public enum ModifierFlags : ushort
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
    Win = 8,
    LeftButton = 16,
    RightButton = 32,
    MiddleButton = 64
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InputPacket
{
    public byte Version;
    public InputType Type;
    public ModifierFlags Flags;
    public int Data1;
    public int Data2;
    public long Timestamp;
    public uint SequenceNumber;
}
