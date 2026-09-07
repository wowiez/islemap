namespace TheIsleOverlay.App;

public sealed class SingleInstanceLease : IDisposable
{
    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceLease(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static bool TryAcquire(string name, out SingleInstanceLease? lease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            lease = null;
            return false;
        }

        lease = new SingleInstanceLease(mutex);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // The process is already shutting down and no longer owns the mutex.
        }

        _mutex.Dispose();
    }
}
