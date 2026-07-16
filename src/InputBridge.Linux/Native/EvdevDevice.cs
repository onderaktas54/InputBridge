namespace InputBridge.Linux.Native;

/// <summary>A single <c>/dev/input/event*</c> device we read raw events from (Host capture).</summary>
internal sealed class EvdevDevice : IDisposable
{
    public readonly record struct Event(ushort Type, ushort Code, int Value);

    private readonly int _fd;
    private readonly Thread _thread;
    private volatile bool _running = true;
    private bool _grabbed;

    public string Path { get; }
    public bool HasKeys { get; }
    public bool HasRel { get; }

    public event Action<Event>? OnEvent;

    private EvdevDevice(int fd, string path, bool hasKeys, bool hasRel)
    {
        _fd = fd;
        Path = path;
        HasKeys = hasKeys;
        HasRel = hasRel;
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = $"evdev:{path}" };
    }

    public void Start() => _thread.Start();

    /// <summary>Exclusive-grab the device so events stop reaching local apps while forwarding.</summary>
    public bool TryGrab()
    {
        if (_grabbed) return true;
        if (NativeMethods.IoctlInt(_fd, NativeMethods.EVIOCGRAB, 1) != 0) return false;
        _grabbed = true;
        return true;
    }

    public void Ungrab()
    {
        if (!_grabbed) return;
        NativeMethods.IoctlInt(_fd, NativeMethods.EVIOCGRAB, 0);
        _grabbed = false;
    }

    private void ReadLoop()
    {
        byte[] buf = new byte[NativeMethods.InputEventSize];
        while (_running)
        {
            nint n = NativeMethods.read(_fd, buf, (nuint)buf.Length);
            if (n != NativeMethods.InputEventSize)
            {
                if (!_running) break;
                Thread.Sleep(2); // transient (e.g. EAGAIN) — retry
                continue;
            }

            ushort type = (ushort)(buf[16] | (buf[17] << 8));
            ushort code = (ushort)(buf[18] | (buf[19] << 8));
            int value = buf[20] | (buf[21] << 8) | (buf[22] << 16) | (buf[23] << 24);
            OnEvent?.Invoke(new Event(type, code, value));
        }
    }

    /// <summary>Open every keyboard/mouse evdev node available to the current user.</summary>
    public static List<EvdevDevice> EnumerateKeyboardsAndMice()
    {
        var devices = new List<EvdevDevice>();
        string dir = "/dev/input";
        if (!Directory.Exists(dir)) return devices;

        foreach (string path in Directory.GetFiles(dir, "event*"))
        {
            int fd = NativeMethods.open(path, NativeMethods.O_RDONLY);
            if (fd < 0) continue;

            (bool hasKeys, bool hasRel, bool hasRep) = QueryCapabilities(fd);

            // Mouse = relative axes; keyboard = keys with autorepeat and no relative axes.
            bool isMouse = hasRel;
            bool isKeyboard = hasKeys && hasRep && !hasRel;

            if (isMouse || isKeyboard)
            {
                devices.Add(new EvdevDevice(fd, path, isKeyboard, isMouse));
            }
            else
            {
                NativeMethods.close(fd);
            }
        }

        return devices;
    }

    private static (bool keys, bool rel, bool rep) QueryCapabilities(int fd)
    {
        // EVIOCGBIT(0) returns the bitmask of supported event types.
        byte[] evbits = new byte[4];
        NativeMethods.IoctlBuf(fd, NativeMethods.EVIOCGBIT(0, (uint)evbits.Length), evbits);
        uint mask = (uint)(evbits[0] | (evbits[1] << 8) | (evbits[2] << 16) | (evbits[3] << 24));

        bool keys = (mask & (1u << NativeMethods.EV_KEY)) != 0;
        bool rel = (mask & (1u << NativeMethods.EV_REL)) != 0;
        bool rep = (mask & (1u << NativeMethods.EV_REP)) != 0;
        return (keys, rel, rep);
    }

    public void Dispose()
    {
        _running = false;
        Ungrab();
        NativeMethods.close(_fd); // unblocks the pending read()
        if (_thread.IsAlive && Thread.CurrentThread != _thread)
        {
            _thread.Join(500);
        }
    }
}
