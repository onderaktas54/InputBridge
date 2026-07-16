using System.Runtime.InteropServices;

namespace InputBridge.Linux.Native;

/// <summary>
/// Low-level libc bindings and Linux input-subsystem constants used to talk to
/// <c>/dev/uinput</c> (input injection) and <c>/dev/input/event*</c> (input capture).
///
/// These paths work under both X11 and Wayland because they operate at the kernel
/// evdev/uinput layer, below the display server — which is exactly why we use them
/// instead of the X11-only XTEST API.
/// </summary>
internal static class NativeMethods
{
    // ---- open() flags ----
    public const int O_RDONLY = 0x0000;
    public const int O_WRONLY = 0x0001;
    public const int O_RDWR = 0x0002;
    public const int O_NONBLOCK = 0x0800;

    // ---- evdev event types (linux/input-event-codes.h) ----
    public const ushort EV_SYN = 0x00;
    public const ushort EV_KEY = 0x01;
    public const ushort EV_REL = 0x02;
    public const ushort EV_MSC = 0x04;
    public const ushort EV_REP = 0x14;
    public const ushort SYN_REPORT = 0x00;

    // ---- relative axes ----
    public const ushort REL_X = 0x00;
    public const ushort REL_Y = 0x01;
    public const ushort REL_HWHEEL = 0x06;
    public const ushort REL_WHEEL = 0x08;

    // ---- mouse buttons ----
    public const ushort BTN_LEFT = 0x110;
    public const ushort BTN_RIGHT = 0x111;
    public const ushort BTN_MIDDLE = 0x112;
    public const ushort BTN_SIDE = 0x113;
    public const ushort BTN_EXTRA = 0x114;

    // Size (in bytes) of a single evdev event on 64-bit Linux:
    //   struct input_event { struct timeval time; __u16 type; __u16 code; __s32 value; }
    //   timeval on 64-bit = 2 * 8 bytes, then 2 + 2 + 4 = 8 -> total 24 bytes.
    public const int InputEventSize = 24;

    // ---- ioctl request-code construction (_IOC macros, asm-generic) ----
    private const int IocNrShift = 0;
    private const int IocTypeShift = 8;
    private const int IocSizeShift = 16;
    private const int IocDirShift = 30;
    private const uint IocNone = 0;
    private const uint IocWrite = 1;
    private const uint IocRead = 2;

    private static ulong Ioc(uint dir, uint type, uint nr, uint size)
        => ((ulong)dir << IocDirShift) | ((ulong)size << IocSizeShift)
           | ((ulong)type << IocTypeShift) | ((ulong)nr << IocNrShift);

    private static ulong Iow(char type, uint nr, uint size) => Ioc(IocWrite, type, nr, size);
    private static ulong Ior(char type, uint nr, uint size) => Ioc(IocRead, type, nr, size);
    private static ulong Io(char type, uint nr) => Ioc(IocNone, type, nr, 0);

    // uinput ioctls (linux/uinput.h)
    public static readonly ulong UI_SET_EVBIT = Iow('U', 100, sizeof(int));
    public static readonly ulong UI_SET_KEYBIT = Iow('U', 101, sizeof(int));
    public static readonly ulong UI_SET_RELBIT = Iow('U', 102, sizeof(int));
    public static readonly ulong UI_DEV_SETUP = Iow('U', 3, UinputSetupSize);
    public static readonly ulong UI_DEV_CREATE = Io('U', 1);
    public static readonly ulong UI_DEV_DESTROY = Io('U', 2);

    // struct uinput_setup { struct input_id id; char name[80]; __u32 ff_effects_max; }
    //   input_id = 4 * u16 = 8 ; name = 80 ; ff_effects_max = 4  -> 92 bytes.
    public const uint UinputSetupSize = 92;
    public const int UinputNameOffset = 8;
    public const int UinputNameMax = 80;

    // evdev query ioctls
    public static ulong EVIOCGRAB => Iow('E', 0x90, sizeof(int));
    public static ulong EVIOCGBIT(uint ev, uint len) => Ior('E', 0x20 + ev, len);

    [DllImport("libc", SetLastError = true)]
    public static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern nint read(int fd, byte[] buf, nuint count);

    [DllImport("libc", SetLastError = true)]
    public static extern nint write(int fd, byte[] buf, nuint count);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    public static extern int IoctlInt(int fd, ulong request, int arg);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    public static extern int IoctlBuf(int fd, ulong request, byte[] arg);
}
