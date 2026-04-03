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
        SetBrushColor("WindowBgBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#9AB7D3" : "#295276");
        SetBrushColor("TextBrush", useLightTheme ? "#183049" : "#FFFFFF");
        SetBrushColor("MutedBrush", useLightTheme ? "#5A7591" : "#A7BBD1");
        SetBrushColor("InputBrush", useLightTheme ? "#FFFFFF" : "#0A1725");
        SetBrushColor("InputBorderBrush", useLightTheme ? "#87A8C8" : "#2D5379");
        SetBrushColor("PrimaryBrush", useLightTheme ? "#43BF86" : "#47C78B");
        SetBrushColor("PrimaryBorderBrush", useLightTheme ? "#6FD6A4" : "#7BE2B2");
        SetBrushColor("PrimaryTextBrush", "#0A2416");
        SetBrushColor("ActionBrush", useLightTheme ? "#DCE9F5" : "#173454");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#87A8C8" : "#31597F");
        SetBrushColor("DisabledBrush", useLightTheme ? "#DCE9F5" : "#13283F");
        SetBrushColor("DisabledTextBrush", useLightTheme ? "#4F6B88" : "#8EA6C2");
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
