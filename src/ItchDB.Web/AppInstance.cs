namespace ItchDB.Web;

/// <summary>One ItchDB process per user session, so a scheduled launch can see that the app is already open.</summary>
public sealed class AppInstance : IDisposable
{
    private readonly Mutex _mutex;

    private AppInstance(Mutex mutex) => _mutex = mutex;

    public static AppInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: false, @"Local\ItchDB.SingleInstance");
        try
        {
            if (mutex.WaitOne(TimeSpan.Zero)) return new AppInstance(mutex);
        }
        catch (AbandonedMutexException)
        {
            return new AppInstance(mutex);
        }
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        _mutex.Dispose();
    }
}
