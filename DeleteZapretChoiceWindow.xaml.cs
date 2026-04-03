using System.Windows;
using System.Windows.Input;

namespace ZapretManager;

public partial class DeleteZapretChoiceWindow : Window
{
    public DeleteZapretChoice Choice { get; private set; } = DeleteZapretChoice.Cancel;

    public DeleteZapretChoiceWindow(string rootPath, bool useLightTheme)
    {
        InitializeComponent();
        PathTextBlock.Text = rootPath;
        ApplyTheme(useLightTheme);
    }

    private void DeleteKeepListsButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = DeleteZapretChoice.DeleteKeepLists;
        DialogResult = true;
        Close();
    }

    private void DeleteEverythingButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = DeleteZapretChoice.DeleteEverything;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = DeleteZapretChoice.Cancel;
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = DeleteZapretChoice.Cancel;
        DialogResult = false;
        Close();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }

    private void ApplyTheme(bool useLightTheme)
    {
        SetBrushColor("WindowBgBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#9AB7D3" : "#295276");
        SetBrushColor("TitleBrush", useLightTheme ? "#183049" : "#FFFFFF");
        SetBrushColor("TextBrush", useLightTheme ? "#4F6B88" : "#D5E4F5");
        SetBrushColor("MutedBrush", useLightTheme ? "#68839E" : "#89A6C2");
        SetBrushColor("PanelBrush", useLightTheme ? "#EEF5FB" : "#0C1A28");
        SetBrushColor("PanelBorderBrush", useLightTheme ? "#BED1E4" : "#274A6B");
        SetBrushColor("PrimaryBrush", useLightTheme ? "#43BF86" : "#47C78B");
        SetBrushColor("PrimaryBorderBrush", useLightTheme ? "#6FD6A4" : "#7BE2B2");
        SetBrushColor("PrimaryTextBrush", "#0A2416");
        SetBrushColor("ActionBrush", useLightTheme ? "#DCE9F5" : "#173454");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#87A8C8" : "#31597F");
        SetBrushColor("DangerBrush", useLightTheme ? "#D56B6B" : "#C55B5B");
        SetBrushColor("DangerBorderBrush", useLightTheme ? "#E38F8F" : "#E17C7C");
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

public enum DeleteZapretChoice
{
    Cancel,
    DeleteKeepLists,
    DeleteEverything
}
