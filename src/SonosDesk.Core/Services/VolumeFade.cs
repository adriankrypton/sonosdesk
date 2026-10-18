namespace SonosDesk.Core.Services;

public static class VolumeFade
{
    public static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(150);

    public static IReadOnlyList<int> Steps(int from, int to, TimeSpan duration)
    {
        var delta = to - from;
        if (delta == 0)
        {
            return [];
        }

        var count = duration <= TimeSpan.Zero
            ? 1
            : (int)Math.Clamp(Math.Ceiling(duration / StepInterval), 1, Math.Abs(delta));
        var steps = new int[count];
        for (var i = 1; i <= count; i++)
        {
            steps[i - 1] = from + (int)Math.Round(delta * (double)i / count, MidpointRounding.AwayFromZero);
        }

        return steps;
    }

    public static async Task RunAsync(
        int from,
        int to,
        TimeSpan duration,
        Func<int, CancellationToken, Task> setVolume,
        TimeProvider time,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setVolume);
        ArgumentNullException.ThrowIfNull(time);
        var steps = Steps(from, to, duration);
        if (steps.Count == 0)
        {
            return;
        }

        var pause = steps.Count > 1 ? duration / steps.Count : TimeSpan.Zero;
        for (var i = 0; i < steps.Count; i++)
        {
            if (i > 0 && pause > TimeSpan.Zero)
            {
                await Task.Delay(pause, time, cancellationToken).ConfigureAwait(false);
            }

            await setVolume(steps[i], cancellationToken).ConfigureAwait(false);
        }
    }
}
