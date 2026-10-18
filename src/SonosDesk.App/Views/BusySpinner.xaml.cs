using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SonosDesk.App.Views;

public partial class BusySpinner : UserControl
{
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(BusySpinner), new PropertyMetadata(2.0, (d, _) => ((BusySpinner)d).UpdateDash()));

    private readonly DoubleAnimation _spin = new(0, 360, new Duration(TimeSpan.FromSeconds(0.9))) { RepeatBehavior = RepeatBehavior.Forever };

    public BusySpinner()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateDash();
        IsVisibleChanged += (_, _) => UpdateAnimation();
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => Rotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    private void UpdateAnimation() =>
        Rotation.BeginAnimation(RotateTransform.AngleProperty, IsVisible ? _spin : null);

    private void UpdateDash()
    {
        var diameter = Math.Min(ActualWidth, ActualHeight);
        if (diameter <= 0 || Thickness <= 0)
        {
            return;
        }

        var circumference = Math.PI * (diameter - Thickness) / Thickness;
        Arc.StrokeDashArray = new DoubleCollection([circumference * 0.25, circumference]);
    }
}
