using InputBridge.Host.Hooks;

namespace InputBridge.Host.Tests;

public sealed class HotkeyManagerTests
{
    [Theory]
    [InlineData("Ctrl+Win+1", 0x000A, 0x31)]
    [InlineData("Ctrl+Alt+2", 0x0003, 0x32)]
    [InlineData("Shift+A", 0x0004, 0x41)]
    public void ParseHotkey_ShouldMapSingleCharacterToKeyboardVirtualKey(
        string input,
        uint expectedModifiers,
        uint expectedKey)
    {
        HotkeyManager.ParseHotkey(input, out uint modifiers, out uint key, 0, 0);

        Assert.Equal(expectedModifiers, modifiers);
        Assert.Equal(expectedKey, key);
    }
}
