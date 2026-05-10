using System.Diagnostics;
using System;
using System.Windows;

namespace ZapretManager;

public partial class AboutWindow : Window
{
    private readonly string _authorProfileUrl;
    private readonly string _authorRepositoryUrl;
    private readonly string _flowsealProfileUrl;
    private readonly string _flowsealRepositoryUrl;
    private readonly string _zapretProfileUrl;
    private readonly string _zapretRepositoryUrl;
    private readonly string _issuesUrl;

    public AboutWindow(
        string version,
        string authorProfileUrl,
        string authorRepositoryUrl,
        string flowsealProfileUrl,
        string flowsealRepositoryUrl,
        string zapretProfileUrl,
        string zapretRepositoryUrl,
        string issuesUrl,
        bool useLightTheme)
    {
        InitializeComponent();
        _authorProfileUrl = authorProfileUrl?.Trim() ?? string.Empty;
        _authorRepositoryUrl = authorRepositoryUrl?.Trim() ?? string.Empty;
        _flowsealProfileUrl = flowsealProfileUrl?.Trim() ?? string.Empty;
        _flowsealRepositoryUrl = flowsealRepositoryUrl?.Trim() ?? string.Empty;
        _zapretProfileUrl = zapretProfileUrl?.Trim() ?? string.Empty;
        _zapretRepositoryUrl = zapretRepositoryUrl?.Trim() ?? string.Empty;
        _issuesUrl = issuesUrl?.Trim() ?? string.Empty;
        VersionTextBlock.Text = $"v{version}";

        var hasAuthorLink = !string.IsNullOrWhiteSpace(_authorProfileUrl) || !string.IsNullOrWhiteSpace(_authorRepositoryUrl);
        AuthorGitHubLabelTextBlock.Visibility = hasAuthorLink ? Visibility.Visible : Visibility.Collapsed;
        AuthorGitHubTextBlock.Visibility = hasAuthorLink ? Visibility.Visible : Visibility.Collapsed;
        AuthorProfileRun.Text = GetLastPathSegment(_authorProfileUrl);
        AuthorRepositoryRun.Text = GetLastPathSegment(_authorRepositoryUrl);
        FlowsealProfileRun.Text = GetLastPathSegment(_flowsealProfileUrl);
        FlowsealRepositoryRun.Text = GetLastPathSegment(_flowsealRepositoryUrl);
        ZapretProfileRun.Text = GetLastPathSegment(_zapretProfileUrl);
        ZapretRepositoryRun.Text = GetLastPathSegment(_zapretRepositoryUrl);

        ApplyTheme(useLightTheme);
    }

    private void ApplyTheme(bool useLightTheme)
    {
        SetBrushColor("WindowBgBrush", useLightTheme ? "#FAFCFF" : "#0E1828");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#DCE7F2" : "#22344E");
        SetBrushColor("TextBrush", useLightTheme ? "#0E1B2B" : "#F4F8FF");
        SetBrushColor("MutedBrush", useLightTheme ? "#637693" : "#90A3BF");
        SetBrushColor("PanelBrush", useLightTheme ? "#FBFDFF" : "#0A1523");
        SetBrushColor("PanelBorderBrush", useLightTheme ? "#E4EDF7" : "#1D3048");
        SetBrushColor("ActionBrush", useLightTheme ? "#F6F9FD" : "#111C2D");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#D8E2EF" : "#263952");
        SetBrushColor("LinkBrush", useLightTheme ? "#2B70F7" : "#6E9CFF");
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

    private void AuthorProfileHyperlink_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(_authorProfileUrl);
    }

    private void AuthorRepositoryHyperlink_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(_authorRepositoryUrl);
    }

    private void FlowsealProfileHyperlink_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(_flowsealProfileUrl);
    }

    private void FlowsealRepositoryHyperlink_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(_flowsealRepositoryUrl);
    }

    private void ZapretProfileHyperlink_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(_zapretProfileUrl);
    }

    private void ZapretRepositoryHyperlink_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(_zapretRepositoryUrl);
    }

    private void IssuesHyperlink_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(_issuesUrl);
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

    private static string GetLastPathSegment(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length > 0)
                {
                    return segments[^1];
                }
            }
        }
        catch
        {
        }

        return "GitHub";
    }
}
