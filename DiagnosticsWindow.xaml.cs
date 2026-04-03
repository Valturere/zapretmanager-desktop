using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ZapretManager.Models;
using ZapretManager.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace ZapretManager;

public partial class DiagnosticsWindow : Window
{
    private sealed record DiagnosticsRow(string Title, string Message, string BadgeText, MediaBrush BadgeBackground, MediaBrush BadgeBorder, MediaBrush BadgeForeground);

    private readonly DiagnosticsService _diagnosticsService;
    private readonly ZapretInstallation? _installation;
    private DiagnosticsReport? _lastReport;
    private bool _useLightTheme;
    public DiagnosticsWindow(
        DiagnosticsService diagnosticsService,
        ZapretInstallation? installation,
        bool useLightTheme)
    {
        InitializeComponent();
        _diagnosticsService = diagnosticsService;
        _installation = installation;
        ApplyTheme(useLightTheme);
    }

    public async Task ShowAndRunAsync()
    {
        try
        {
            if (!IsVisible)
            {
                Show();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            }
            
            Activate();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            HandleRefreshFailure(ex);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            StatusTextBlock.Text = "Проверяем систему...";
            ResultsSectionGrid.Visibility = Visibility.Collapsed;
            ActionsPanel.Visibility = Visibility.Collapsed;
            SetButtonsEnabled(false);
            _lastReport = await _diagnosticsService.RunAsync(_installation);

            ApplyReportToUi();

            FixTimestampsButton.Visibility = _lastReport.NeedsTcpTimestampFix ? Visibility.Visible : Visibility.Collapsed;
            RemoveWinDivertButton.Visibility = _lastReport.HasStaleWinDivert ? Visibility.Visible : Visibility.Collapsed;
            RemoveConflictsButton.Visibility = _lastReport.ConflictingServices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ResultsSectionGrid.Visibility = Visibility.Visible;
            ActionsPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            HandleRefreshFailure(ex);
        }
        finally
        {
            SetButtonsEnabled(true);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void FixTimestampsButton_Click(object sender, RoutedEventArgs e)
    {
        if (await _diagnosticsService.EnableTcpTimestampsAsync())
        {
            DialogService.ShowInfo("TCP timestamps включены.");
            await RefreshAsync();
            return;
        }

        DialogService.ShowError("Не удалось включить TCP timestamps.");
    }

    private async void RemoveWinDivertButton_Click(object sender, RoutedEventArgs e)
    {
        if (await _diagnosticsService.RemoveStaleWinDivertAsync())
        {
            DialogService.ShowInfo("Попытка удаления WinDivert завершена.");
            await RefreshAsync();
            return;
        }

        DialogService.ShowError("Не удалось удалить WinDivert.");
    }

    private async void RemoveConflictsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null || _lastReport.ConflictingServices.Count == 0)
        {
            return;
        }

        var removed = await _diagnosticsService.RemoveConflictingServicesAsync(_lastReport.ConflictingServices);
        if (removed.Count > 0)
        {
            DialogService.ShowInfo($"Удалены службы: {string.Join(", ", removed)}.");
            await RefreshAsync();
            return;
        }

        DialogService.ShowError("Не удалось удалить конфликтующие службы.");
    }

    private void ApplyTheme(bool useLightTheme)
    {
        _useLightTheme = useLightTheme;
        var primaryBorderColor = useLightTheme ? "#6FD6A4" : "#7BE2B2";
        SetBrushColor("WindowBgBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#9AB7D3" : "#295276");
        SetBrushColor("TextBrush", useLightTheme ? "#183049" : "#FFFFFF");
        SetBrushColor("MutedBrush", useLightTheme ? "#4F6B88" : "#9AB2CD");
        SetBrushColor("PanelBrush", useLightTheme ? "#EEF5FB" : "#0C1A28");
        SetBrushColor("PanelBorderBrush", useLightTheme ? "#9CB7CF" : "#274A6B");
        SetBrushColor("ActionBrush", useLightTheme ? "#DCE9F5" : "#173454");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#87A8C8" : "#31597F");
        SetBrushColor("PrimaryBrush", useLightTheme ? "#43BF86" : "#47C78B");
        SetBrushColor("PrimaryBorderBrush", primaryBorderColor);
        SetBrushColor("PrimaryTextBrush", "#0A2416");
        SetBrushColor("GridRowBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("GridAltRowBrush", useLightTheme ? "#EEF5FB" : "#0D1C2C");
        SetBrushColor("ScrollTrackBrush", useLightTheme ? "#E4EEF7" : "#102235");
        SetBrushColor("ScrollThumbBrush", useLightTheme ? "#B7C9DB" : "#35506A");
        SetBrushColor("ScrollThumbHoverBrush", useLightTheme ? "#9FB8D0" : "#466481");

        if (_lastReport is not null)
        {
            ApplyReportToUi();
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        FixTimestampsButton.IsEnabled = enabled;
        RemoveWinDivertButton.IsEnabled = enabled;
        RemoveConflictsButton.IsEnabled = enabled;
    }

    private static string GetBadgeText(DiagnosticsSeverity severity) => severity switch
    {
        DiagnosticsSeverity.Success => "✓",
        DiagnosticsSeverity.Warning => "!",
        _ => "✕"
    };

    private MediaBrush GetBadgeBackground(DiagnosticsSeverity severity) => new SolidColorBrush((MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(
        _useLightTheme
            ? severity switch
            {
                DiagnosticsSeverity.Success => "#D9EDE3",
                DiagnosticsSeverity.Warning => "#EEE4D3",
                _ => "#EEDDE3"
            }
            : severity switch
            {
                DiagnosticsSeverity.Success => "#27423D",
                DiagnosticsSeverity.Warning => "#4A422E",
                _ => "#4C3941"
            }));

    private MediaBrush GetBadgeBorder(DiagnosticsSeverity severity) => new SolidColorBrush((MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(
        _useLightTheme
            ? severity switch
            {
                DiagnosticsSeverity.Success => "#A8C4B7",
                DiagnosticsSeverity.Warning => "#C7B18A",
                _ => "#C8A8B2"
            }
            : severity switch
            {
                DiagnosticsSeverity.Success => "#6C9184",
                DiagnosticsSeverity.Warning => "#A89566",
                _ => "#B28A97"
            }));

    private MediaBrush GetBadgeForeground(DiagnosticsSeverity severity) => new SolidColorBrush((MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(
        _useLightTheme
            ? severity switch
            {
                DiagnosticsSeverity.Success => "#2E6350",
                DiagnosticsSeverity.Warning => "#755B2F",
                _ => "#764754"
            }
            : "#FFFFFF"));

    private void SetBrushColor(string key, string color)
    {
        Resources[key] = new SolidColorBrush((MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private void ApplyReportToUi()
    {
        if (_lastReport is null)
        {
            return;
        }

        SummaryTextBlock.Text = _lastReport.HasAnyErrors
            ? "Обнаружены ошибки и предупреждения, которые могут мешать работе zapret."
            : _lastReport.HasAnyWarnings
                ? "Критичных ошибок не найдено, но есть моменты, на которые стоит обратить внимание."
                : "Критичных проблем не найдено.";

        StatusTextBlock.Text = $"Проверок: {_lastReport.Items.Count}  •  Ошибок: {_lastReport.Items.Count(item => item.Severity == DiagnosticsSeverity.Error)}  •  Предупреждений: {_lastReport.Items.Count(item => item.Severity == DiagnosticsSeverity.Warning)}";

        ResultsGrid.ItemsSource = _lastReport.Items
            .Select(item => new DiagnosticsRow(
                item.Title,
                item.Message,
                GetBadgeText(item.Severity),
                GetBadgeBackground(item.Severity),
                GetBadgeBorder(item.Severity),
                GetBadgeForeground(item.Severity)))
            .ToArray();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HandleRefreshFailure(Exception ex)
    {
        _lastReport = null;
        SummaryTextBlock.Text = "Диагностику не удалось завершить.";
        StatusTextBlock.Text = $"Ошибка: {ex.Message}";
        ResultsGrid.ItemsSource = Array.Empty<DiagnosticsRow>();
        FixTimestampsButton.Visibility = Visibility.Collapsed;
        RemoveWinDivertButton.Visibility = Visibility.Collapsed;
        RemoveConflictsButton.Visibility = Visibility.Collapsed;
        ResultsSectionGrid.Visibility = Visibility.Collapsed;
        ActionsPanel.Visibility = Visibility.Visible;
        SetButtonsEnabled(true);
    }
}
