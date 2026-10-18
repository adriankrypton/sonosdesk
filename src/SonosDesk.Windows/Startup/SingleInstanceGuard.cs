using System;
using System.Threading;

namespace SonosDesk.Windows.Startup;

public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\SonosDesk.SingleInstance";
    private const string ActivateEventName = @"Local\SonosDesk.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly RegisteredWaitHandle _registration;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activate,
            (_, _) => ActivationRequested?.Invoke(this, EventArgs.Empty),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public event EventHandler? ActivationRequested;

    public static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex, activate);
        }

        activate.Set();
        activate.Dispose();
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _activate.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread (disposed from another thread during shutdown).
        }

        _mutex.Dispose();
    }
}
