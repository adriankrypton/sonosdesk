namespace SonosDesk.App.ViewModels;

internal sealed class VolumeDebouncer(Func<int, CancellationToken, Task> apply)
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan HoldAfterChange = TimeSpan.FromSeconds(2.5);

    private CancellationTokenSource? _pending;
    private DateTimeOffset _lastLocalChange = DateTimeOffset.MinValue;

    public bool IsHolding => DateTimeOffset.UtcNow - _lastLocalChange < HoldAfterChange;

    public async Task SubmitAsync(int volume, Action<Exception> onError)
    {
        _lastLocalChange = DateTimeOffset.UtcNow;
        _pending?.Cancel();
        _pending?.Dispose();
        var cts = new CancellationTokenSource();
        _pending = cts;

        try
        {
            await Task.Delay(Delay, cts.Token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            await apply(volume, timeout.Token);
            _lastLocalChange = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer value.
        }
        catch (Exception ex)
        {
            onError(ex);
        }
    }
}
