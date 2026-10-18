using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using SonosDesk.App.Services;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.App.ViewModels;

public abstract class ViewModelBase(UserNotifier notifier, ILogger logger) : ObservableObject
{
    protected UserNotifier Notifier { get; } = notifier;

    protected ILogger Logger { get; } = logger;

    protected async Task<bool> RunAsync(Func<CancellationToken, Task> operation, string? successMessage = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await operation(timeout.Token);
            if (successMessage is not null)
            {
                Notifier.Show(successMessage, NotificationSeverity.Success);
            }

            return true;
        }
        catch (SonosException ex)
        {
            Notifier.Show(ex.Message, ex.Kind == SonosErrorKind.NotSupported ? NotificationSeverity.Warning : NotificationSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            Notifier.ShowError(L.T("vm.timeout"));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error in UI operation");
            Notifier.ShowError(L.T("vm.unexpected_error"));
        }

        return false;
    }
}
