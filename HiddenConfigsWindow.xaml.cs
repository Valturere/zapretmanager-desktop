using System.Collections.ObjectModel;
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
    public string? SelectedFilePath { get; private set; }

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
        if (HiddenConfigsListBox.SelectedItem is not HiddenConfigItem item)
        {
            return;
        }

        SelectedAction = HiddenConfigsAction.RestoreSelected;
        SelectedFilePath = item.FilePath;
        Close();
    }

    private void RestoreAllButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = HiddenConfigsAction.RestoreAll;
        Close();
    }

    private void HiddenConfigsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RestoreSelectedButton.IsEnabled = HiddenConfigsListBox.SelectedItem is HiddenConfigItem;
    }

    private void ApplyTheme(bool useLightTheme)
    {
        SetBrushColor("WindowBgBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#9AB7D3" : "#295276");
        SetBrushColor("TextBrush", useLightTheme ? "#183049" : "#FFFFFF");
        SetBrushColor("MutedBrush", useLightTheme ? "#5A7591" : "#A7BBD1");
        SetBrushColor("PanelBrush", useLightTheme ? "#EEF5FB" : "#0C1A28");
        SetBrushColor("PanelBorderBrush", useLightTheme ? "#9CB7CF" : "#2C5478");
        SetBrushColor("PrimaryBrush", useLightTheme ? "#43BF86" : "#47C78B");
        SetBrushColor("PrimaryBorderBrush", useLightTheme ? "#6FD6A4" : "#7BE2B2");
        SetBrushColor("PrimaryTextBrush", "#0A2416");
        SetBrushColor("ActionBrush", useLightTheme ? "#DCE9F5" : "#173454");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#87A8C8" : "#31597F");
        SetBrushColor("SelectionBrush", useLightTheme ? "#BCD0E3" : "#35506A");
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
