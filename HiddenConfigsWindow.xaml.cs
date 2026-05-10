using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ZapretManager.Models;

namespace ZapretManager;

public partial class HiddenConfigsWindow : Window
{
    public ObservableCollection<HiddenConfigItem> Items { get; }

    public HiddenConfigsWindow(IEnumerable<HiddenConfigItem> items, bool useLightTheme)
    {
        InitializeComponent();
        Items = new ObservableCollection<HiddenConfigItem>(items);
        DataContext = this;
        ApplyTheme(useLightTheme);
        HiddenConfigsListBox.SelectedIndex = -1;
        RestoreSelectedButton.IsEnabled = false;
    }

    public HiddenConfigsAction SelectedAction { get; private set; } = HiddenConfigsAction.None;
    public IReadOnlyList<string> SelectedFilePaths { get; private set; } = [];

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RestoreSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPaths = HiddenConfigsListBox.SelectedItems
            .OfType<HiddenConfigItem>()
            .Select(item => item.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (selectedPaths.Length == 0)
        {
            return;
        }

        SelectedAction = HiddenConfigsAction.RestoreSelected;
        SelectedFilePaths = selectedPaths;
        Close();
    }

    private void RestoreAllButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = HiddenConfigsAction.RestoreAll;
        Close();
    }

    private void HiddenConfigsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RestoreSelectedButton.IsEnabled = HiddenConfigsListBox.SelectedItems.Count > 0;
    }

    private void ApplyTheme(bool useLightTheme)
    {
        SetBrushColor("WindowBgBrush", useLightTheme ? "#FAFCFF" : "#0E1828");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#DCE7F2" : "#22344E");
        SetBrushColor("TextBrush", useLightTheme ? "#0E1B2B" : "#F4F8FF");
        SetBrushColor("MutedBrush", useLightTheme ? "#637693" : "#90A3BF");
        SetBrushColor("PanelBrush", useLightTheme ? "#FBFDFF" : "#0A1523");
        SetBrushColor("PanelBorderBrush", useLightTheme ? "#E4EDF7" : "#1D3048");
        SetBrushColor("PrimaryBrush", useLightTheme ? "#2B70F7" : "#2F6BFF");
        SetBrushColor("PrimaryBorderBrush", useLightTheme ? "#5C90FF" : "#6E9CFF");
        SetBrushColor("PrimaryTextBrush", "#F7FBFF");
        SetBrushColor("ActionBrush", useLightTheme ? "#F6F9FD" : "#111C2D");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#D8E2EF" : "#263952");
        SetBrushColor("SelectionBrush", useLightTheme ? "#E8F0FF" : "#142848");
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

public enum HiddenConfigsAction
{
    None,
    RestoreSelected,
    RestoreAll
}
