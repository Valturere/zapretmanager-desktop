using System.Diagnostics;
using System.Windows;

namespace ZapretManager;

public partial class AboutWindow : Window
{
    private readonly string _authorGitHubUrl;
    private readonly string _flowsealRepositoryUrl;
    private readonly string _zapretRepositoryUrl;

    public AboutWindow(
        string version,
        string authorGitHubUrl,
        string flowsealRepositoryUrl,
        string zapretRepositoryUrl,
        bool useLightTheme)
    {
        InitializeComponent();
        _authorGitHubUrl = authorGitHubUrl?.Trim() ?? string.Empty;
        _flowsealRepositoryUrl = flowsealRepositoryUrl?.Trim() ?? string.Empty;
        _zapretRepositoryUrl = zapretRepositoryUrl?.Trim() ?? string.Empty;
        VersionTextBlock.Text = version;

        var hasAuthorLink = !string.IsNullOrWhiteSpace(_authorGitHubUrl);
        AuthorGitHubLabelTextBlock.Visibility = hasAuthorLink ? Visibility.Visible : Visibility.Collapsed;
        AuthorGitHubTextBlock.Visibility = hasAuthorLink ? Visibility.Visible : Visibility.Collapsed;
        AuthorGitHubRun.Text = _authorGitHubUrl;

        FlowsealRepositoryRun.Text = "Flowseal";
        ZapretRepositoryRun.Text = "Zapret";

        ApplyTheme(useLightTheme);
    }

    private void ApplyTheme(bool useLightTheme)
    {
        SetBrushColor("WindowBgBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#9AB7D3" : "#295276");
        SetBrushColor("TextBrush", useLightTheme ? "#183049" : "#FFFFFF");
        SetBrushColor("MutedBrush", useLightTheme ? "#5E7893" : "#9AB2CD");
        SetBrushColor("PanelBrush", useLightTheme ? "#EEF5FB" : "#0C1A28");
        SetBrushColor("PanelBorderBrush", useLightTheme ? "#9CB7CF" : "#274A6B");
        SetBrushColor("ActionBrush", useLightTheme ? "#DCE9F5" : "#173454");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#87A8C8" : "#31597F");
        SetBrushColor("LinkBrush", useLightTheme ? "#2C89A1" : "#7BC8FF");
    }

    private void SetBrushColor(string key, string color)
    {
        var convertedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
        if (Resources[key] is System.Windows.Media.SolidColorBrush brush)
        {
            if (brush.Color != convertedColor)
            {
                brush.Color = convertedColor;
            }
        }
        else
        {
            Resources[key] = new System.Windows.Media.SolidColorBrush(convertedColor);
        }
    }

    private void AuthorGitHubHyperlink_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(_authorGitHubUrl);
    }

    private void FlowsealRepositoryHyperlink_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(_flowsealRepositoryUrl);
    }

    private void ZapretRepositoryHyperlink_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(_zapretRepositoryUrl);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DragMove();
    }

    private static void OpenLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }
}
