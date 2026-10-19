using System.Windows;
using System.Windows.Controls;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;

namespace SonosDesk.App.Views;

public partial class AddReceiverDialog : Window
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<ReceiverIdentity>>> _search;
    private readonly CancellationTokenSource _closing = new();

    public AddReceiverDialog(Func<CancellationToken, Task<IReadOnlyList<ReceiverIdentity>>> search)
    {
        _search = search;
        InitializeComponent();
        Closed += (_, _) => _closing.Cancel();
    }

    public string? Address { get; private set; }

    private void OnAddressChanged(object sender, TextChangedEventArgs e)
    {
        var text = AddressBox.Text.Trim();
        var valid = SettingsNormalizer.IsLocalIPv4(text);
        AddButton.IsEnabled = valid;
        ValidationText.Text = text.Length == 0
            ? L.T("dlg.receiver.example")
            : valid ? L.T("dlg.speaker.valid") : L.T("dlg.receiver.invalid");
    }

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        SearchButton.IsEnabled = false;
        SearchingPanel.Visibility = Visibility.Visible;
        try
        {
            var found = await _search(_closing.Token);
            FoundList.ItemsSource = found;
            FoundList.Visibility = found.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ValidationText.Text = found.Count > 0 ? L.T("dlg.receiver.pick") : L.T("dlg.receiver.none_found");
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (SonosException ex)
        {
            ValidationText.Text = ex.Message;
        }
        finally
        {
            if (!_closing.IsCancellationRequested)
            {
                SearchButton.IsEnabled = true;
                SearchingPanel.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnFoundSelected(object sender, SelectionChangedEventArgs e)
    {
        if (FoundList.SelectedItem is ReceiverIdentity identity)
        {
            AddressBox.Text = identity.Host;
        }
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        Address = AddressBox.Text.Trim();
        DialogResult = true;
    }
}
