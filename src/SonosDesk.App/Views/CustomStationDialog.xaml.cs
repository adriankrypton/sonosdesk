using System.Windows;
using System.Windows.Controls;
using SonosDesk.Core.Localization;

namespace SonosDesk.App.Views;

public partial class CustomStationDialog : Window
{
    public CustomStationDialog(IEnumerable<string> folders)
    {
        InitializeComponent();
        foreach (var folder in folders)
        {
            FolderBox.Items.Add(folder);
        }

        Validate();
    }

    public string StationName => NameBox.Text.Trim();

    public string StreamUrl => UrlBox.Text.Trim();

    public string? Folder => string.IsNullOrWhiteSpace(FolderBox.Text) ? null : FolderBox.Text.Trim();

    private void OnInputChanged(object sender, TextChangedEventArgs e) => Validate();

    private void Validate()
    {
        var validUrl = Uri.TryCreate(StreamUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        AddButton.IsEnabled = StationName.Length > 0 && validUrl;
        ValidationText.Text = StationName.Length == 0 ? L.T("dlg.station.need_name")
            : !validUrl ? L.T("dlg.station.need_url")
            : uri!.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ? L.T("dlg.station.hls_warning")
            : L.T("dlg.station.checked_on_add");
    }

    private void OnAdd(object sender, RoutedEventArgs e) => DialogResult = true;
}
