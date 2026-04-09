using System.Windows;
namespace ZapretManager.Controls;

public partial class ProbeStatusBadge : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty BadgeTextProperty =
        DependencyProperty.Register(
            nameof(BadgeText),
            typeof(string),
            typeof(ProbeStatusBadge),
            new PropertyMetadata(string.Empty));

    public string BadgeText
    {
        get => (string)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public ProbeStatusBadge()
    {
        InitializeComponent();
    }
}
