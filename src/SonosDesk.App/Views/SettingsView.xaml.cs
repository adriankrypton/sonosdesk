using System.Windows;
using System.Windows.Controls;
using SonosDesk.App.ViewModels;

namespace SonosDesk.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnSecretChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.ClientSecretInput = SecretBox.Password;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SettingsViewModel oldVm)
        {
            oldVm.ClientSecretStored -= OnClientSecretStored;
        }

        if (e.NewValue is SettingsViewModel newVm)
        {
            newVm.ClientSecretStored += OnClientSecretStored;
        }
    }

    private void OnClientSecretStored(object? sender, EventArgs e) => SecretBox.Clear();

    private void OnLinkNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        if (e.Uri.Scheme == Uri.UriSchemeHttps)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // No browser registered; nothing else to do.
            }
        }

        e.Handled = true;
    }
}
