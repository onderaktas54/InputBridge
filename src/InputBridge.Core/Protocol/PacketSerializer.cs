using System;
using System.Runtime.InteropServices;

namespace InputBridge.Core.Protocol;

public static class PacketSerializer
{
    public static byte[] Serialize(in InputPacket packet)
    {
        int size = Marshal.SizeOf<InputPacket>();
        byte[] arr = new byte[size];
        MemoryMarshal.Write(arr, in packet);
        return arr;
    }

    public static InputPacket Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < Marshal.SizeOf<InputPacket>())
        {
            throw new ArgumentException("Data is too short to be an InputPacket", nameof(data));
        }
        return MemoryMarshal.Read<InputPacket>(data);
    }
}
