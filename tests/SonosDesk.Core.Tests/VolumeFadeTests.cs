using Microsoft.Extensions.Time.Testing;
using SonosDesk.Core.Services;

namespace SonosDesk.Core.Tests;

public class VolumeFadeTests
{
    [Fact]
    public void Without_duration_the_target_is_set_at_once() =>
        Assert.Equal([35], VolumeFade.Steps(10, 35, TimeSpan.Zero));

    [Fact]
    public void Equal_volumes_need_no_command() => Assert.Empty(VolumeFade.Steps(20, 20, TimeSpan.FromSeconds(2)));

    [Fact]
    public void Steps_are_limited_by_duration_and_end_on_the_target()
    {
        var steps = VolumeFade.Steps(0, 60, TimeSpan.FromMilliseconds(600));

        Assert.Equal([15, 30, 45, 60], steps);
    }

    [Fact]
    public void Small_changes_use_one_step_per_volume_point()
    {
        Assert.Equal([19, 18, 17], VolumeFade.Steps(20, 17, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Run_waits_between_steps()
    {
        var time = new FakeTimeProvider();
        var sent = new List<int>();
        var run = VolumeFade.RunAsync(10, 13, TimeSpan.FromMilliseconds(900), (v, _) => { sent.Add(v); return Task.CompletedTask; }, time);

        Assert.Equal([11], sent);
        time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal([11, 12], sent);
        time.Advance(TimeSpan.FromMilliseconds(300));
        await run;
        Assert.Equal([11, 12, 13], sent);
    }

    [Fact]
    public async Task Run_can_be_cancelled()
    {
        var time = new FakeTimeProvider();
        using var cts = new CancellationTokenSource();
        var sent = new List<int>();
        var run = VolumeFade.RunAsync(0, 50, TimeSpan.FromSeconds(3), (v, _) => { sent.Add(v); return Task.CompletedTask; }, time, cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Single(sent);
    }
}
