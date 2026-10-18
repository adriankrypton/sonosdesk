using System.Windows;

namespace SonosDesk.App.Views;

public partial class TextPromptDialog : Window
{
    public TextPromptDialog(string title, string hint, IEnumerable<string> suggestions, string? initial = null)
    {
        InitializeComponent();
        Title = title;
        HeaderText.Text = title;
        HintText.Text = hint;
        foreach (var suggestion in suggestions)
        {
            InputBox.Items.Add(suggestion);
        }

        InputBox.Text = initial ?? string.Empty;
    }

    public string Value => InputBox.Text.Trim();

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
