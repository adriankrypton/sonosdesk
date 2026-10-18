using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Settings;

namespace SonosDesk.Core.Services;

public sealed class AutomationScheduler : IAsyncDisposable
{
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(20);

    internal static readonly TimeSpan MaxCatchUp = TimeSpan.FromMinutes(5);

    private readonly ISonosConfigurationService _configuration;
    private readonly IQuickActionExecutor _executor;
    private readonly ISonosStateService _state;
    private readonly TimeProvider _time;
    private readonly ILogger<AutomationScheduler> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DateTime _lastCheckLocal;

    public AutomationScheduler(
        ISonosConfigurationService configuration,
        IQuickActionExecutor executor,
        ISonosStateService state,
        TimeProvider time,
        ILogger<AutomationScheduler> logger)
    {
        _configuration = configuration;
        _executor = executor;
        _state = state;
        _time = time;
        _logger = logger;
    }

    public event EventHandler<QuickActionResult>? RuleExecuted;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _lastCheckLocal = _time.GetLocalNow().DateTime;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null || _loop is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _cts.Dispose();
    }

    public static bool IsDue(AutomationRule rule, DateTime previous, DateTime now)
    {
        if (!rule.Enabled || now <= previous)
        {
            return false;
        }

        if (now - previous > MaxCatchUp)
        {
            previous = now - MaxCatchUp;
        }

        for (var date = previous.Date; date <= now.Date; date = date.AddDays(1))
        {
            var occurrence = date + rule.Time.ToTimeSpan();
            if (occurrence > previous && occurrence <= now && (rule.Days.Count == 0 || rule.Days.Contains(occurrence.DayOfWeek)))
            {
                return true;
            }
        }

        return false;
    }

    internal async Task CheckAsync(CancellationToken ct)
    {
        var now = _time.GetLocalNow().DateTime;
        var previous = _lastCheckLocal;
        _lastCheckLocal = now;

        var settings = _configuration.Current;
        foreach (var rule in settings.Automations.Where(r => IsDue(r, previous, now)))
        {
            var action = settings.QuickActions.FirstOrDefault(a => a.Id == rule.QuickActionId);
            if (action is null)
            {
                _logger.LogWarning("Automation {Rule} references a missing quick action", rule.Name);
                continue;
            }

            _logger.LogInformation("Running automation {Rule}", rule.Name);
            var result = await _executor.ExecuteAsync(action, _state.ActiveRoom, ct).ConfigureAwait(false);
            RuleExecuted?.Invoke(this, result);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(CheckInterval, _time);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await CheckAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automation check failed");
            }
        }
    }
}
