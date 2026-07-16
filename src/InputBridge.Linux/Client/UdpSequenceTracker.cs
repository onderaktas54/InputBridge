namespace InputBridge.Linux.Client;

internal sealed class UdpSequenceTracker
{
    private uint _last;

    public void Reset() => _last = 0;

    public bool ShouldAccept(uint sequence)
    {
        if (sequence == 0 || sequence <= _last) return false;
        _last = sequence;
        return true;
    }
}
