namespace InputBridge.Linux;

/// <summary>
/// Translates between Windows Virtual-Key codes (what the InputBridge wire protocol
/// carries) and Linux evdev key codes (linux/input-event-codes.h).
///
/// The Windows Host sends VK codes; on Linux we must convert them to evdev codes to
/// inject via uinput. When acting as a Linux Host we do the reverse so Windows/Linux
/// clients receive the VK codes they expect.
/// </summary>
internal static class KeyMap
{
    private static readonly Dictionary<int, int> VkToLinux = new()
    {
        // Letters (VK 'A'..'Z')
        [0x41] = 30, [0x42] = 48, [0x43] = 46, [0x44] = 32, [0x45] = 18, [0x46] = 33,
        [0x47] = 34, [0x48] = 35, [0x49] = 23, [0x4A] = 36, [0x4B] = 37, [0x4C] = 38,
        [0x4D] = 50, [0x4E] = 49, [0x4F] = 24, [0x50] = 25, [0x51] = 16, [0x52] = 19,
        [0x53] = 31, [0x54] = 20, [0x55] = 22, [0x56] = 47, [0x57] = 17, [0x58] = 45,
        [0x59] = 21, [0x5A] = 44,

        // Digits (VK '0'..'9')
        [0x30] = 11, [0x31] = 2, [0x32] = 3, [0x33] = 4, [0x34] = 5,
        [0x35] = 6, [0x36] = 7, [0x37] = 8, [0x38] = 9, [0x39] = 10,

        // Whitespace / editing
        [0x0D] = 28,  // Enter
        [0x1B] = 1,   // Esc
        [0x08] = 14,  // Backspace
        [0x09] = 15,  // Tab
        [0x20] = 57,  // Space
        [0x2E] = 111, // Delete
        [0x2D] = 110, // Insert
        [0x24] = 102, // Home
        [0x23] = 107, // End
        [0x21] = 104, // PageUp
        [0x22] = 109, // PageDown
        [0x14] = 58,  // CapsLock
        [0x90] = 69,  // NumLock
        [0x91] = 70,  // ScrollLock
        [0x2C] = 99,  // PrintScreen
        [0x13] = 119, // Pause

        // Arrows
        [0x25] = 105, [0x26] = 103, [0x27] = 106, [0x28] = 108,

        // Function keys F1..F12
        [0x70] = 59, [0x71] = 60, [0x72] = 61, [0x73] = 62, [0x74] = 63, [0x75] = 64,
        [0x76] = 65, [0x77] = 66, [0x78] = 67, [0x79] = 68, [0x7A] = 87, [0x7B] = 88,

        // OEM punctuation (US layout positions)
        [0xBA] = 39, // ; :
        [0xBB] = 13, // = +
        [0xBC] = 51, // , <
        [0xBD] = 12, // - _
        [0xBE] = 52, // . >
        [0xBF] = 53, // / ?
        [0xC0] = 41, // ` ~
        [0xDB] = 26, // [ {
        [0xDC] = 43, // \ |
        [0xDD] = 27, // ] }
        [0xDE] = 40, // ' "

        // Numpad
        [0x60] = 82, [0x61] = 79, [0x62] = 80, [0x63] = 81, [0x64] = 75, [0x65] = 76,
        [0x66] = 77, [0x67] = 71, [0x68] = 72, [0x69] = 73,
        [0x6A] = 55, [0x6B] = 78, [0x6D] = 74, [0x6E] = 83, [0x6F] = 98,

        // Modifiers — generic (Host may send these) and left/right specific
        [0x10] = 42,  // Shift   -> LeftShift
        [0x11] = 29,  // Control -> LeftCtrl
        [0x12] = 56,  // Alt     -> LeftAlt
        [0xA0] = 42,  // LShift
        [0xA1] = 54,  // RShift
        [0xA2] = 29,  // LCtrl
        [0xA3] = 97,  // RCtrl
        [0xA4] = 56,  // LAlt
        [0xA5] = 100, // RAlt
        [0x5B] = 125, // LWin -> LeftMeta
        [0x5C] = 126, // RWin -> RightMeta
    };

    // Reverse map (evdev -> VK). Built to prefer left/right-specific virtual keys so a
    // Linux Host produces the same codes a Windows Host would.
    private static readonly Dictionary<int, int> LinuxToVk = BuildReverse();

    private static Dictionary<int, int> BuildReverse()
    {
        var reverse = new Dictionary<int, int>();
        foreach (var (vk, code) in VkToLinux)
        {
            // First writer wins; iterate so specific modifiers registered explicitly below.
            reverse.TryAdd(code, vk);
        }

        // Force left/right-specific modifier virtual keys.
        reverse[42] = 0xA0;  // LeftShift
        reverse[54] = 0xA1;  // RightShift
        reverse[29] = 0xA2;  // LeftCtrl
        reverse[97] = 0xA3;  // RightCtrl
        reverse[56] = 0xA4;  // LeftAlt
        reverse[100] = 0xA5; // RightAlt
        reverse[125] = 0x5B; // LeftMeta
        reverse[126] = 0x5C; // RightMeta
        return reverse;
    }

    /// <summary>Windows VK -> Linux evdev key code, or -1 if unmapped.</summary>
    public static int VkToEvdev(int vk) => VkToLinux.TryGetValue(vk, out int code) ? code : -1;

    /// <summary>Linux evdev key code -> Windows VK, or -1 if unmapped.</summary>
    public static int EvdevToVk(int code) => LinuxToVk.TryGetValue(code, out int vk) ? vk : -1;

    /// <summary>All evdev key codes we can inject — used to enable uinput key bits.</summary>
    public static IEnumerable<int> AllEvdevKeyCodes => VkToLinux.Values;
}
