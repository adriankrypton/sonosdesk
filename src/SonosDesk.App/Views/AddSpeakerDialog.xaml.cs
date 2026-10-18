using System.Windows;
using System.Windows.Controls;
using SonosDesk.Core.Localization;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.App.Views;

public partial class AddSpeakerDialog : Window
{
    public AddSpeakerDialog()
    {
        InitializeComponent();
    }

    public string? Address { get; private set; }

    private void OnAddressChanged(object sender, TextChangedEventArgs e)
    {
        var text = AddressBox.Text.Trim();
        var valid = DeviceUrlValidator.TryBuildManualLocation(text) is not null;
        AddButton.IsEnabled = valid;
        ValidationText.Text = text.Length == 0
            ? L.T("dlg.speaker.example")
            : valid
                ? L.T("dlg.speaker.valid")
                : L.T("dlg.speaker.invalid");
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        Address = AddressBox.Text.Trim();
        DialogResult = true;
    }
}
