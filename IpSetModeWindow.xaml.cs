using System.Windows;
using System.Windows.Input;

namespace ZapretManager;

public partial class IpSetModeWindow : Window
{
    private sealed record IpSetModeItem(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    public string SelectedModeValue { get; private set; }
    public bool WasApplied { get; private set; }

    public IpSetModeWindow(string currentMode, bool useLightTheme)
    {
        InitializeComponent();
        ApplyTheme(useLightTheme);

        var items = new[]
        {
            new IpSetModeItem("loaded", "По списку"),
            new IpSetModeItem("none", "Выключен"),
            new IpSetModeItem("any", "Все IP")
        };

        ModeComboBox.ItemsSource = items;
        ModeComboBox.SelectedValue = currentMode;
        SelectedModeValue = currentMode;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedModeValue = ModeComboBox.SelectedValue as string ?? "loaded";
        WasApplied = true;
        Close();
    }

    private void ApplyTheme(bool useLightTheme)
    {
        SetBrushColor("WindowBgBrush", useLightTheme ? "#FAFCFF" : "#0E1828");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#DCE7F2" : "#22344E");
        SetBrushColor("TextBrush", useLightTheme ? "#0E1B2B" : "#F4F8FF");
        SetBrushColor("MutedBrush", useLightTheme ? "#637693" : "#90A3BF");
        SetBrushColor("InputBrush", useLightTheme ? "#FFFFFF" : "#0C1626");
        SetBrushColor("InputBorderBrush", useLightTheme ? "#D8E3F0" : "#24364F");
        SetBrushColor("PrimaryBrush", useLightTheme ? "#2B70F7" : "#2F6BFF");
        SetBrushColor("PrimaryBorderBrush", useLightTheme ? "#5C90FF" : "#6E9CFF");
        SetBrushColor("PrimaryTextBrush", "#F7FBFF");
        SetBrushColor("ActionBrush", useLightTheme ? "#F6F9FD" : "#111C2D");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#D8E2EF" : "#263952");
        SetBrushColor("DisabledBrush", useLightTheme ? "#EEF3F8" : "#101827");
        SetBrushColor("DisabledTextBrush", useLightTheme ? "#8DA0B8" : "#61748D");
    }

    private void SetBrushColor(string resourceKey, string hexColor)
    {
        if (Resources[resourceKey] is not System.Windows.Media.SolidColorBrush brush)
        {
            return;
        }

        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
        if (brush.IsFrozen)
        {
            Resources[resourceKey] = new System.Windows.Media.SolidColorBrush(color);
            return;
        }

        brush.Color = color;
    }
}
