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
        SetBrushColor("WindowBgBrush", useLightTheme ? "#FAFCFF" : "#0E1828");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#DCE7F2" : "#22344E");
        SetBrushColor("TitleBrush", useLightTheme ? "#0E1B2B" : "#F4F8FF");
        SetBrushColor("TextBrush", useLightTheme ? "#22364D" : "#D6E3F5");
        SetBrushColor("MutedBrush", useLightTheme ? "#637693" : "#90A3BF");
        SetBrushColor("PanelBrush", useLightTheme ? "#FBFDFF" : "#0A1523");
        SetBrushColor("PanelBorderBrush", useLightTheme ? "#E4EDF7" : "#1D3048");
        SetBrushColor("PrimaryBrush", useLightTheme ? "#2B70F7" : "#2F6BFF");
        SetBrushColor("PrimaryBorderBrush", useLightTheme ? "#5C90FF" : "#6E9CFF");
        SetBrushColor("PrimaryTextBrush", "#F7FBFF");
        SetBrushColor("ActionBrush", useLightTheme ? "#F6F9FD" : "#111C2D");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#D8E2EF" : "#263952");
        SetBrushColor("DangerBrush", useLightTheme ? "#D55454" : "#A94141");
        SetBrushColor("DangerBorderBrush", useLightTheme ? "#E48787" : "#DB7777");
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
