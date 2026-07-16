namespace InputBridge.Linux.Native;

/// <summary>Abstraction over local input injection so the client loop stays platform-neutral.</summary>
internal interface IInputInjector : IDisposable
{
    void KeyDown(int vk);
    void KeyUp(int vk);
    void MouseMove(int dx, int dy);
    void MouseButton(int buttonId, bool isDown);
    void Scroll(int wheelDelta);
    void ReleaseAll();
}
