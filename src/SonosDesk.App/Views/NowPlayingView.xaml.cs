using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SonosDesk.App.ViewModels;

namespace SonosDesk.App.Views;

public partial class NowPlayingView : UserControl
{
    private const string SectionFormat = "SonosDesk.HomeSection";
    private bool _orderApplied;

    public NowPlayingView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplySavedOrder();
        DataContextChanged += (_, _) => ApplySavedOrder();
    }

    private void ApplySavedOrder()
    {
        if (_orderApplied || DataContext is not MainViewModel vm)
        {
            return;
        }

        _orderApplied = true;
        var order = vm.HomeSectionOrder.ToList();
        var sections = Sections.Children.OfType<FrameworkElement>().ToList();
        var sorted = sections
            .OrderBy(s => order.IndexOf(s.Tag as string ?? string.Empty) is var i && i >= 0 ? i : order.Count + sections.IndexOf(s))
            .ToList();
        Sections.Children.Clear();
        foreach (var section in sorted)
        {
            Sections.Children.Add(section);
        }
    }

    private void OnOpenMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } element)
        {
            menu.PlacementTarget = element;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindSection(sender as DependencyObject) is not { } section)
        {
            return;
        }

        section.Opacity = 0.5;
        try
        {
            DragDrop.DoDragDrop(section, new DataObject(SectionFormat, section.Tag as string ?? string.Empty), DragDropEffects.Move);
        }
        finally
        {
            section.Opacity = 1;
        }

        e.Handled = true;
    }

    private void OnSectionsDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(SectionFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSectionsDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(SectionFormat) is not string tag
            || Sections.Children.OfType<FrameworkElement>().FirstOrDefault(s => (s.Tag as string) == tag) is not { } dragged)
        {
            return;
        }

        // Insert before the first visible card whose vertical center is below the drop point.
        var y = e.GetPosition(Sections).Y;
        var others = Sections.Children.OfType<FrameworkElement>().Where(s => s != dragged).ToList();
        var index = others.Count;
        for (var i = 0; i < others.Count; i++)
        {
            var card = others[i];
            if (card.Visibility != Visibility.Visible)
            {
                continue;
            }

            var top = card.TranslatePoint(new Point(0, 0), Sections).Y;
            if (y < top + (card.ActualHeight / 2))
            {
                index = i;
                break;
            }
        }

        Sections.Children.Remove(dragged);
        Sections.Children.Insert(Math.Min(index, Sections.Children.Count), dragged);
        if (DataContext is MainViewModel vm)
        {
            vm.SaveHomeSectionOrder(Sections.Children.OfType<FrameworkElement>().Select(s => s.Tag as string ?? string.Empty).ToList());
        }

        e.Handled = true;
    }

    private FrameworkElement? FindSection(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement fe && fe.Parent == Sections)
            {
                return fe;
            }

            element = System.Windows.Media.VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element);
        }

        return null;
    }
}
