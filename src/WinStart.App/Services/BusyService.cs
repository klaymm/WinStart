using System.Windows;

namespace WinStart.App.Services;

public interface IBusyService
{
    bool IsBusy { get; }
    event EventHandler? Changed;
    IDisposable Begin();
}

public sealed class BusyService : IBusyService
{
    private int _count;

    public bool IsBusy => Volatile.Read(ref _count) > 0;

    public event EventHandler? Changed;

    public IDisposable Begin()
    {
        Interlocked.Increment(ref _count);
        Raise();
        return new Scope(this);
    }

    private void End()
    {
        Interlocked.Decrement(ref _count);
        Raise();
    }

    private void Raise()
    {
        var app = Application.Current;
        if (app is null) { Changed?.Invoke(this, EventArgs.Empty); return; }
        app.Dispatcher.Invoke(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private sealed class Scope(BusyService owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.End();
        }
    }
}
