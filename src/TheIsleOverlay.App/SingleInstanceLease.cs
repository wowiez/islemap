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

    public static bool TryAcquireReplacingExisting(
        string name,
        out SingleInstanceLease? lease,
        TimeSpan? timeout = null)
    {
        if (TryAcquire(name, out lease)) return true;

        using var current = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (process.Id == current.Id) continue;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                }
            }
        }

        var expiresAt = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(4));
        do
        {
            Thread.Sleep(80);
            if (TryAcquire(name, out lease)) return true;
        }
        while (DateTime.UtcNow < expiresAt);

        lease = null;
        return false;
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
