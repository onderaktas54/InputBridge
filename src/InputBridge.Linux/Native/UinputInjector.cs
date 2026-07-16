using System.Collections.Concurrent;
using System.Text;

namespace InputBridge.Linux.Native;

/// <summary>
/// Injects keyboard and mouse events on Linux by creating a virtual input device
/// through <c>/dev/uinput</c>. This is the Linux counterpart of the Windows
/// <c>SendInput</c>-based simulators, and it works under both X11 and Wayland.
///
/// Requires write access to <c>/dev/uinput</c> (run as root, or add a udev rule).
/// </summary>
internal sealed class UinputInjector : IInputInjector
{
    private readonly int _fd;
    private readonly ConcurrentDictionary<int, bool> _pressedKeys = new();
    private readonly ConcurrentDictionary<int, bool> _pressedButtons = new();
    private bool _disposed;

    private UinputInjector(int fd) => _fd = fd;

    public static UinputInjector Create()
    {
        int fd = NativeMethods.open("/dev/uinput", NativeMethods.O_WRONLY | NativeMethods.O_NONBLOCK);
        if (fd < 0)
        {
            throw new InvalidOperationException(
                "Could not open /dev/uinput. Run as root (sudo) or add a udev rule granting access.");
        }

        try
        {
            Configure(fd);
            return new UinputInjector(fd);
        }
        catch
        {
            NativeMethods.close(fd);
            throw;
        }
    }

    private static void Ioctl(int fd, ulong request, int arg)
    {
        if (NativeMethods.IoctlInt(fd, request, arg) < 0)
        {
            throw new InvalidOperationException($"ioctl 0x{request:X} failed on /dev/uinput.");
        }
    }

    private static void Configure(int fd)
    {
        Ioctl(fd, NativeMethods.UI_SET_EVBIT, NativeMethods.EV_SYN);
        Ioctl(fd, NativeMethods.UI_SET_EVBIT, NativeMethods.EV_KEY);
        Ioctl(fd, NativeMethods.UI_SET_EVBIT, NativeMethods.EV_REL);

        // Enable every key code we know how to map.
        foreach (int code in KeyMap.AllEvdevKeyCodes)
        {
            Ioctl(fd, NativeMethods.UI_SET_KEYBIT, code);
        }

        // Enable mouse buttons.
        foreach (ushort btn in new[]
                 {
                     NativeMethods.BTN_LEFT, NativeMethods.BTN_RIGHT, NativeMethods.BTN_MIDDLE,
                     NativeMethods.BTN_SIDE, NativeMethods.BTN_EXTRA,
                 })
        {
            Ioctl(fd, NativeMethods.UI_SET_KEYBIT, btn);
        }

        // Enable relative axes for movement and scrolling.
        foreach (ushort axis in new[]
                 {
                     NativeMethods.REL_X, NativeMethods.REL_Y,
                     NativeMethods.REL_WHEEL, NativeMethods.REL_HWHEEL,
                 })
        {
            Ioctl(fd, NativeMethods.UI_SET_RELBIT, axis);
        }

        // struct uinput_setup: input_id{bus,vendor,product,version} + name[80] + ff_effects_max
        var setup = new byte[NativeMethods.UinputSetupSize];
        WriteU16(setup, 0, 0x03);   // BUS_USB
        WriteU16(setup, 2, 0x1234); // vendor
        WriteU16(setup, 4, 0x5678); // product
        WriteU16(setup, 6, 0x0001); // version
        byte[] name = Encoding.ASCII.GetBytes("InputBridge Virtual Device");
        Array.Copy(name, 0, setup, NativeMethods.UinputNameOffset,
            Math.Min(name.Length, NativeMethods.UinputNameMax - 1));

        if (NativeMethods.IoctlBuf(fd, NativeMethods.UI_DEV_SETUP, setup) < 0)
        {
            throw new InvalidOperationException("UI_DEV_SETUP failed on /dev/uinput.");
        }

        if (NativeMethods.IoctlInt(fd, NativeMethods.UI_DEV_CREATE, 0) < 0)
        {
            throw new InvalidOperationException("UI_DEV_CREATE failed on /dev/uinput.");
        }

        // Give the display server a moment to notice the new device.
        Thread.Sleep(200);
    }

    public void KeyDown(int vk) => KeyEvent(vk, isDown: true);

    public void KeyUp(int vk) => KeyEvent(vk, isDown: false);

    private void KeyEvent(int vk, bool isDown)
    {
        int code = KeyMap.VkToEvdev(vk);
        if (code < 0) return; // Unmapped key — skip rather than inject garbage.

        if (isDown) _pressedKeys[code] = true;
        else _pressedKeys.TryRemove(code, out _);

        Emit(NativeMethods.EV_KEY, (ushort)code, isDown ? 1 : 0);
        Sync();
    }

    public void MouseMove(int dx, int dy)
    {
        if (dx != 0) Emit(NativeMethods.EV_REL, NativeMethods.REL_X, dx);
        if (dy != 0) Emit(NativeMethods.EV_REL, NativeMethods.REL_Y, dy);
        Sync();
    }

    public void MouseButton(int buttonId, bool isDown)
    {
        int code = buttonId switch
        {
            0 => NativeMethods.BTN_LEFT,
            1 => NativeMethods.BTN_RIGHT,
            2 => NativeMethods.BTN_MIDDLE,
            3 => NativeMethods.BTN_SIDE,
            4 => NativeMethods.BTN_EXTRA,
            _ => -1,
        };
        if (code < 0) return;

        if (isDown) _pressedButtons[buttonId] = true;
        else _pressedButtons.TryRemove(buttonId, out _);

        Emit(NativeMethods.EV_KEY, (ushort)code, isDown ? 1 : 0);
        Sync();
    }

    public void Scroll(int wheelDelta)
    {
        // Windows sends wheel deltas in multiples of 120 (WHEEL_DELTA); evdev uses
        // discrete notch counts. Convert, keeping the sign and at least one notch.
        int notches = wheelDelta / 120;
        if (notches == 0) notches = Math.Sign(wheelDelta);
        if (notches == 0) return;

        Emit(NativeMethods.EV_REL, NativeMethods.REL_WHEEL, notches);
        Sync();
    }

    public void ReleaseAll()
    {
        foreach (int code in _pressedKeys.Keys.ToArray())
        {
            _pressedKeys.TryRemove(code, out _);
            Emit(NativeMethods.EV_KEY, (ushort)code, 0);
        }

        foreach (int buttonId in _pressedButtons.Keys.ToArray())
        {
            MouseButton(buttonId, isDown: false);
        }

        Sync();
    }

    private void Emit(ushort type, ushort code, int value)
    {
        if (_disposed) return;
        byte[] ev = new byte[NativeMethods.InputEventSize];
        // time (16 bytes) left zero — kernel fills it in.
        WriteU16(ev, 16, type);
        WriteU16(ev, 18, code);
        WriteI32(ev, 20, value);
        NativeMethods.write(_fd, ev, (nuint)ev.Length);
    }

    private void Sync() => Emit(NativeMethods.EV_SYN, NativeMethods.SYN_REPORT, 0);

    private static void WriteU16(byte[] buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteI32(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { ReleaseAll(); } catch { /* best effort */ }
        NativeMethods.IoctlInt(_fd, NativeMethods.UI_DEV_DESTROY, 0);
        NativeMethods.close(_fd);
    }
}
