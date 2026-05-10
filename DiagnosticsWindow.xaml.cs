using System.Windows;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using ZapretManager.Models;
using ZapretManager.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace ZapretManager;

public partial class DiagnosticsWindow : Window
{
    private sealed record DiagnosticsRow(string Title, string Message, string TooltipText, string BadgeText, MediaBrush BadgeBackground, MediaBrush BadgeBorder, MediaBrush BadgeForeground);

    private readonly DiagnosticsService _diagnosticsService;
    private readonly ZapretInstallation? _installation;
    private readonly ObservableCollection<DiagnosticsRow> _rows = [];
    private DiagnosticsReport? _lastReport;
    private bool _useLightTheme;
    private int _progressCompleted;
    private int _progressTotal;
    public DiagnosticsWindow(
        DiagnosticsService diagnosticsService,
        ZapretInstallation? installation,
        bool useLightTheme)
    {
        InitializeComponent();
        _diagnosticsService = diagnosticsService;
        _installation = installation;
        ResultsGrid.ItemsSource = _rows;
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
            _lastReport = null;
            _rows.Clear();
            _progressCompleted = 0;
            _progressTotal = GetExpectedCheckCount();
            SummaryTextBlock.Text = "Диагностика выполняется. Результаты будут появляться по мере проверки.";
            StatusTextBlock.Text = "Подготавливаем диагностику...";
            ProgressCountTextBlock.Text = $"0 / {_progressTotal}";
            UpdateProgressBar();
            ResultsSectionGrid.Visibility = Visibility.Visible;
            ActionsPanel.Visibility = Visibility.Collapsed;
            FixTimestampsButton.Visibility = Visibility.Collapsed;
            RemoveWinDivertButton.Visibility = Visibility.Collapsed;
            RemoveConflictsButton.Visibility = Visibility.Collapsed;
            SetButtonsEnabled(false);

            var progress = new Progress<DiagnosticsProgressUpdate>(ApplyProgressUpdate);
            _lastReport = await _diagnosticsService.RunAsync(_installation, progress);

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
        var primaryBorderColor = useLightTheme ? "#5C90FF" : "#6E9CFF";
        SetBrushColor("WindowBgBrush", useLightTheme ? "#FAFCFF" : "#0E1828");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#DCE7F2" : "#22344E");
        SetBrushColor("TextBrush", useLightTheme ? "#0E1B2B" : "#F4F8FF");
        SetBrushColor("MutedBrush", useLightTheme ? "#637693" : "#90A3BF");
        SetBrushColor("PanelBrush", useLightTheme ? "#FBFDFF" : "#0A1523");
        SetBrushColor("PanelBorderBrush", useLightTheme ? "#E4EDF7" : "#1D3048");
        SetBrushColor("ActionBrush", useLightTheme ? "#F6F9FD" : "#111C2D");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#D8E2EF" : "#263952");
        SetBrushColor("PrimaryBrush", useLightTheme ? "#2B70F7" : "#2F6BFF");
        SetBrushColor("PrimaryBorderBrush", primaryBorderColor);
        SetBrushColor("PrimaryTextBrush", "#F7FBFF");
        SetBrushColor("GridRowBrush", useLightTheme ? "#FFFFFF" : "#0E1828");
        SetBrushColor("GridAltRowBrush", useLightTheme ? "#F8FBFF" : "#0B1322");
        SetBrushColor("ScrollTrackBrush", useLightTheme ? "#EEF3F8" : "#101A2A");
        SetBrushColor("ScrollThumbBrush", useLightTheme ? "#B1C0D2" : "#3D526D");
        SetBrushColor("ScrollThumbHoverBrush", useLightTheme ? "#94A8C0" : "#587392");
        SetBrushColor("ProgressTrackBrush", useLightTheme ? "#D9E5F4" : "#16263B");
        SetBrushColor("ProgressFillBrush", useLightTheme ? "#2B70F7" : "#2F6BFF");

        if (_lastReport is not null)
        {
            ApplyReportToUi();
        }

        UpdateProgressBar();
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
        _progressCompleted = _lastReport.Items.Count;
        _progressTotal = Math.Max(_progressTotal, _lastReport.Items.Count);
        ProgressCountTextBlock.Text = $"{_progressCompleted} / {_progressTotal}";
        UpdateProgressBar();
    }

    private void ApplyProgressUpdate(DiagnosticsProgressUpdate update)
    {
        StatusTextBlock.Text = update.StatusText;
        _progressCompleted = update.CompletedChecks;
        _progressTotal = update.TotalChecks;
        ProgressCountTextBlock.Text = $"{_progressCompleted} / {_progressTotal}";
        UpdateProgressBar();

        if (update.Item is null)
        {
            return;
        }

        _rows.Add(CreateRow(update.Item));
        SummaryTextBlock.Text = $"Диагностика выполняется. Уже проверено: {update.CompletedChecks} из {update.TotalChecks}.";
    }

    private int GetExpectedCheckCount() => _installation is not null ? 14 : 13;

    private void UpdateProgressBar()
    {
        if (ProgressTrackBorder is null || ProgressFillBorder is null)
        {
            return;
        }

        var trackWidth = ProgressTrackBorder.ActualWidth;
        if (trackWidth <= 0)
        {
            return;
        }

        if (_progressTotal <= 0)
        {
            ProgressFillBorder.Width = 0;
            return;
        }

        var ratio = Math.Clamp((double)_progressCompleted / _progressTotal, 0d, 1d);
        ProgressFillBorder.Width = trackWidth * ratio;
    }

    private void ProgressTrackBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateProgressBar();
    }

    private DiagnosticsRow CreateRow(DiagnosticsCheckItem item)
    {
        return new DiagnosticsRow(
            item.Title,
            item.Message,
            BuildTooltipText(item),
            GetBadgeText(item.Severity),
            GetBadgeBackground(item.Severity),
            GetBadgeBorder(item.Severity),
            GetBadgeForeground(item.Severity));
    }

    private static string BuildTooltipText(DiagnosticsCheckItem item)
    {
        var description = item.Title switch
        {
            "Base Filtering Engine" => "Проверяет, запущена ли системная служба фильтрации пакетов Windows. Она нужна части сетевых драйверов и служб, которые работают с трафиком.",
            "Системный прокси" => "Проверяет, не включён ли в Windows системный прокси. Он может перенаправлять трафик и мешать нормальной работе приложений.",
            "Adguard" => "Проверяет, не запущен ли Adguard. Такие фильтрующие программы могут конфликтовать с Discord и сетевой обработкой zapret.",
            "Killer" => "Проверяет, нет ли в системе служб Killer, которые умеют вмешиваться в сетевой трафик и иногда ломают работу приложений.",
            "Intel Connectivity" => "Проверяет, не установлены ли конфликтующие сетевые службы Intel Connectivity / Network. Они могут влиять на обработку трафика.",
            "Check Point" => "Проверяет, нет ли компонентов Check Point. Корпоративные сетевые агенты такого типа иногда конфликтуют с обходом и фильтрацией трафика.",
            "SmartByte" => "Проверяет, нет ли SmartByte. Эта утилита умеет вмешиваться в сетевой трафик и иногда вызывает нестабильную работу приложений.",
            "WinDivert драйвер" => "Проверяет, найден ли файл драйвера WinDivert в папке сборки. Без него часть сценариев работы winws невозможна.",
            "VPN" => "Проверяет, нет ли активного VPN или его компонентов. VPN может менять маршрутизацию и искажать результаты диагностики и проверки конфигов.",
            "Secure DNS" => "Проверяет, есть ли в системе или браузере признаки включённого Secure DNS / DoH. Это влияет на доступность доменов и результаты проверки.",
            "hosts" => "Проверяет системный файл hosts на конфликтующие записи, которые могут вручную перенаправлять запросы к сайтам.",
            "TCP timestamps" => "Проверяет системную настройку TCP timestamps. Для некоторых сборок и сценариев Flowseal рекомендует держать её включённой.",
            "WinDivert" => "Проверяет, не осталась ли в системе подвисшая служба WinDivert без работающего winws. Такие хвосты могут мешать новой установке.",
            "Конфликтующие bypass-службы" => "Проверяет, нет ли других bypass-служб, которые могут одновременно перехватывать трафик и конфликтовать с zapret.",
            _ => item.Title
        };

        return string.IsNullOrWhiteSpace(item.Details) || string.Equals(item.Details, item.Message, StringComparison.Ordinal)
            ? description
            : item.Title == "VPN"
                ? description
                : $"{description}\n\nПодробности: {item.Details}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HandleRefreshFailure(Exception ex)
    {
        var displayMessage = BuildDiagnosticsFailureMessage(ex);
        _lastReport = null;
        SummaryTextBlock.Text = "Диагностику не удалось завершить.";
        StatusTextBlock.Text = "Ошибка: не удалось получить результаты диагностики.";
        ProgressCountTextBlock.Text = $"{_progressCompleted} / {_progressTotal}";
        _rows.Clear();
        FixTimestampsButton.Visibility = Visibility.Collapsed;
        RemoveWinDivertButton.Visibility = Visibility.Collapsed;
        RemoveConflictsButton.Visibility = Visibility.Collapsed;
        ResultsSectionGrid.Visibility = Visibility.Visible;
        ActionsPanel.Visibility = Visibility.Visible;
        SetButtonsEnabled(true);
        UpdateProgressBar();
        DialogService.ShowError(displayMessage, owner: this);
    }

    private static string BuildDiagnosticsFailureMessage(Exception exception)
    {
        var details = DialogService.GetDisplayMessage(exception, "Не удалось выполнить диагностику системы.");
        if (details.Contains("Что можно попробовать:", StringComparison.OrdinalIgnoreCase))
        {
            return details;
        }

        return
            "Не удалось выполнить диагностику системы." +
            $"{Environment.NewLine}{Environment.NewLine}Повторите попытку. Если ошибка сохраняется, запустите программу от администратора." +
            $"{Environment.NewLine}{Environment.NewLine}Техническая причина:{Environment.NewLine}{details}";
    }
}
