using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ZapretManager.Models;
using ZapretManager.Services;
using MediaColor = System.Windows.Media.Color;

namespace ZapretManager;

public partial class TcpFreezeWindow : Window
{
    private static readonly Regex ConfigHeaderRegex = new("^===\\s+(?<name>.+)\\s+===$", RegexOptions.Compiled);
    private static readonly Regex SuiteCountRegex = new("suite TCP 16-20:\\s+(?<count>\\d+)\\s+целей", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ConfigSummaryRegex = new("^Итог\\s+(?<name>.+):\\s+OK\\s+(?<ok>\\d+),\\s+BLOCKED\\s+(?<blocked>\\d+),\\s+FAIL\\s+(?<fail>\\d+),\\s+UNSUP\\s+(?<unsup>\\d+)\\.$", RegexOptions.Compiled);
    private static readonly Regex RecommendedRegex = new("Наиболее устойчивый конфиг по TCP 16-20:\\s+(?<name>.+)$", RegexOptions.Compiled);
    private static readonly Regex TargetLineRegex = new("\\[(?<id>[^\\]]+)\\]\\s+(?<host>\\S+)\\s+->\\s+(?<details>.+)$", RegexOptions.Compiled);

    private readonly Func<string?, IProgress<string>, CancellationToken, Task<TcpFreezeRunReport>> _runAsync;
    private readonly Func<TcpFreezeWindowContext> _contextFactory;
    private readonly ObservableCollection<TcpFreezeConfigTableRow> _rows = [];
    private readonly Dictionary<string, TcpFreezeConfigResult> _resultMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly StringBuilder _logBuilder = new();

    private CancellationTokenSource? _runCancellation;
    private TcpFreezeWindowContext _context = new() { Configs = [] };
    private TcpFreezeConfigTableRow? _selectedSummaryRow;
    private TcpFreezeDetailsWindow? _detailsWindow;
    private readonly bool _useLightTheme;
    private int _totalConfigsForRun;
    private int _completedConfigs;
    private int _totalTargetsPerConfig;
    private int _processedTargetsForCurrentConfig;
    private string? _currentConfigName;

    public TcpFreezeWindow(
        Func<string?, IProgress<string>, CancellationToken, Task<TcpFreezeRunReport>> runAsync,
        Func<TcpFreezeWindowContext> contextFactory,
        bool useLightTheme)
    {
        InitializeComponent();
        _runAsync = runAsync;
        _contextFactory = contextFactory;
        _useLightTheme = useLightTheme;
        ApplyTheme(useLightTheme);
        ConfigGrid.ItemsSource = _rows;
        RefreshContext();
        SetRunningState(isRunning: false);
        UpdateSelectedSummary();
    }

    public TcpFreezeConfigTableRow? SelectedSummaryRow
    {
        get => _selectedSummaryRow;
        set
        {
            if (ReferenceEquals(_selectedSummaryRow, value))
            {
                return;
            }

            _selectedSummaryRow = value;
            UpdateSelectedSummary();
        }
    }

    public async Task ShowAndActivateAsync()
    {
        if (!IsVisible)
        {
            Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        }

        RefreshContext();
        Activate();
    }

    private async void RunSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSummaryRow is null)
        {
            return;
        }

        await RunAsync(SelectedSummaryRow.FilePath);
    }

    private async void RunAllButton_Click(object sender, RoutedEventArgs e) => await RunAsync(null);

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null)
        {
            _runCancellation.Cancel();
            return;
        }

        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => ActionButton_Click(sender, e);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_runCancellation is not null)
        {
            _runCancellation.Cancel();
            e.Cancel = true;
            return;
        }

        try
        {
            _detailsWindow?.Close();
        }
        catch
        {
        }

        base.OnClosing(e);
    }

    private async Task RunAsync(string? selectedConfigPath)
    {
        if (_runCancellation is not null)
        {
            return;
        }

        RefreshContext();
        ClearResults();
        _runCancellation = new CancellationTokenSource();
        SetRunningState(isRunning: true);

        var progress = new Progress<string>(HandleProgressLine);
        var scopeLabel = string.IsNullOrWhiteSpace(selectedConfigPath)
            ? "всех видимых конфигов"
            : $"конфига {SelectedSummaryRow?.ConfigName ?? "без имени"}";

        _totalConfigsForRun = string.IsNullOrWhiteSpace(selectedConfigPath) ? _context.Configs.Count : 1;
        AddLogLine(string.Empty);
        AddLogLine($"=== Запуск проверки для {scopeLabel} ===");
        StatusTextBlock.Text = "Проверка выполняется...";
        PhaseTextBlock.Text = $"Подготавливаем запуск для {scopeLabel}.";
        UpdateProgressDisplay();

        try
        {
            var report = await _runAsync(selectedConfigPath, progress, _runCancellation.Token);
            ApplyReport(report);
            AddLogLine(string.Empty);
            AddLogLine("Проверка завершена.");
            StatusTextBlock.Text = "Проверка завершена.";
            PhaseTextBlock.Text = "Тест завершён. Можно выбрать конфиг в таблице и открыть детали.";
        }
        catch (OperationCanceledException)
        {
            AddLogLine(string.Empty);
            AddLogLine("Проверка отменена.");
            StatusTextBlock.Text = "Проверка отменена.";
            PhaseTextBlock.Text = "Выполнение остановлено пользователем.";
        }
        catch (Exception ex)
        {
            var message = DialogService.GetDisplayMessage(ex);
            AddLogLine(string.Empty);
            AddLogLine("Ошибка: " + message);
            StatusTextBlock.Text = "Проверка завершилась с ошибкой.";
            PhaseTextBlock.Text = message;
        }
        finally
        {
            _runCancellation.Dispose();
            _runCancellation = null;
            SetRunningState(isRunning: false, idleStatus: StatusTextBlock.Text);
            UpdateSelectedSummary();
        }
    }

    private void RefreshContext()
    {
        _context = _contextFactory.Invoke();
        SummaryTextBlock.Text = $"Доступно конфигов: {_context.Configs.Count}. Выбор и запуск можно делать прямо в этой таблице.";
        FooterTextBlock.Text = _runCancellation is null
            ? "Подробный лог оставлен для диагностики. Основная работа теперь идёт через таблицу конфигов."
            : "Во время теста остальные действия в окне временно недоступны.";

        var currentSelectionPath = SelectedSummaryRow?.FilePath ?? _context.InitiallySelectedFilePath;
        _rows.Clear();
        foreach (var descriptor in _context.Configs)
        {
            _rows.Add(BuildRow(descriptor, _resultMap.GetValueOrDefault(descriptor.FilePath)));
        }

        SelectedSummaryRow = _rows.FirstOrDefault(row =>
                                 string.Equals(row.FilePath, currentSelectionPath, StringComparison.OrdinalIgnoreCase))
                             ?? _rows.FirstOrDefault();
        ConfigGrid.SelectedItem = SelectedSummaryRow;
    }

    private void ClearResults()
    {
        _resultMap.Clear();
        _logBuilder.Clear();
        LogTextBox.Clear();
        _completedConfigs = 0;
        _totalTargetsPerConfig = 0;
        _processedTargetsForCurrentConfig = 0;
        _currentConfigName = null;
        TargetsMetricTextBlock.Text = "-";
        ProgressMetricTextBlock.Text = "0 / 0";
        ProgressCountTextBlock.Text = "0 / 0";
        RecommendedConfigTextBlock.Text = "Появится после завершения";
        CurrentTargetTextBlock.Text = "Цель ещё не проверяется";

        RefreshContext();
    }

    private void ApplyReport(TcpFreezeRunReport report)
    {
        _resultMap.Clear();
        foreach (var configResult in report.ConfigResults)
        {
            _resultMap[configResult.FilePath] = configResult;
        }

        RefreshContext();

        var recommended = !string.IsNullOrWhiteSpace(report.RecommendedConfigPath)
            ? _rows.FirstOrDefault(row => string.Equals(row.FilePath, report.RecommendedConfigPath, StringComparison.OrdinalIgnoreCase))
            : null;
        if (recommended is not null)
        {
            RecommendedConfigTextBlock.Text = recommended.ConfigName;
        }
    }

    private void HandleProgressLine(string line)
    {
        AddLogLine(line);
        ParseProgressLine(line);
    }

    private void ParseProgressLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (line.StartsWith("Останавливаем", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Возвращаем", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Параметры:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Выбрано для теста:", StringComparison.OrdinalIgnoreCase))
        {
            PhaseTextBlock.Text = line;
            return;
        }

        var suiteMatch = SuiteCountRegex.Match(line);
        if (suiteMatch.Success && int.TryParse(suiteMatch.Groups["count"].Value, out var suiteCount))
        {
            _totalTargetsPerConfig = suiteCount;
            TargetsMetricTextBlock.Text = $"{suiteCount} x 3";
            UpdateProgressDisplay();
            return;
        }

        var configHeaderMatch = ConfigHeaderRegex.Match(line);
        if (configHeaderMatch.Success && !string.Equals(configHeaderMatch.Groups["name"].Value, "Сводка", StringComparison.OrdinalIgnoreCase))
        {
            _currentConfigName = configHeaderMatch.Groups["name"].Value.Trim();
            _processedTargetsForCurrentConfig = 0;
            PhaseTextBlock.Text = $"Проверяем {_currentConfigName}.";
            var row = _rows.FirstOrDefault(item => string.Equals(item.ConfigName, _currentConfigName, StringComparison.OrdinalIgnoreCase));
            if (row is not null)
            {
                SelectedSummaryRow = row;
                ConfigGrid.SelectedItem = row;
                ConfigGrid.ScrollIntoView(row);
            }

            UpdateProgressDisplay();
            return;
        }

        var targetLineMatch = TargetLineRegex.Match(line);
        if (targetLineMatch.Success)
        {
            _processedTargetsForCurrentConfig++;
            CurrentTargetTextBlock.Text = targetLineMatch.Groups["host"].Value.Trim();
            if (_totalTargetsPerConfig > 0 && !string.IsNullOrWhiteSpace(_currentConfigName))
            {
                PhaseTextBlock.Text = $"Проверяем {_currentConfigName}: цель {_processedTargetsForCurrentConfig} из {_totalTargetsPerConfig}.";
            }

            UpdateProgressDisplay();
            return;
        }

        var configSummaryMatch = ConfigSummaryRegex.Match(line);
        if (configSummaryMatch.Success)
        {
            _completedConfigs = Math.Min(_completedConfigs + 1, _totalConfigsForRun);
            _processedTargetsForCurrentConfig = 0;
            UpdateRowSummary(
                configSummaryMatch.Groups["name"].Value.Trim(),
                int.Parse(configSummaryMatch.Groups["ok"].Value),
                int.Parse(configSummaryMatch.Groups["blocked"].Value),
                int.Parse(configSummaryMatch.Groups["fail"].Value),
                int.Parse(configSummaryMatch.Groups["unsup"].Value));
            UpdateProgressDisplay();
            return;
        }

        var recommendedMatch = RecommendedRegex.Match(line);
        if (recommendedMatch.Success)
        {
            RecommendedConfigTextBlock.Text = recommendedMatch.Groups["name"].Value.Trim();
        }
    }

    private void UpdateRowSummary(string configName, int okCount, int blockedCount, int failCount, int unsupportedCount)
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];
            if (!string.Equals(row.ConfigName, configName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _rows[index] = row with
            {
                OkCount = okCount,
                BlockedCount = blockedCount,
                FailCount = failCount,
                UnsupportedCount = unsupportedCount,
                SummaryText = BuildSummaryText(okCount, blockedCount, failCount, unsupportedCount, null),
                HasResult = true
            };

            if (SelectedSummaryRow is not null &&
                string.Equals(SelectedSummaryRow.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                SelectedSummaryRow = _rows[index];
                ConfigGrid.SelectedItem = SelectedSummaryRow;
            }

            break;
        }
    }

    private TcpFreezeConfigTableRow BuildRow(TcpFreezeConfigDescriptor descriptor, TcpFreezeConfigResult? result)
    {
        return new TcpFreezeConfigTableRow
        {
            ConfigName = descriptor.ConfigName,
            FileName = descriptor.FileName,
            FilePath = descriptor.FilePath,
            OkCount = result?.OkCount,
            BlockedCount = result?.BlockedCount,
            FailCount = result?.FailCount,
            UnsupportedCount = result?.UnsupportedCount,
            SummaryText = BuildSummaryText(
                result?.OkCount,
                result?.BlockedCount,
                result?.FailCount,
                result?.UnsupportedCount,
                result?.BlockedTargets),
            HasResult = result is not null
        };
    }

    private static string BuildSummaryText(
        int? okCount,
        int? blockedCount,
        int? failCount,
        int? unsupportedCount,
        IReadOnlyList<string>? blockedTargets)
    {
        if (!okCount.HasValue || !blockedCount.HasValue || !failCount.HasValue || !unsupportedCount.HasValue)
        {
            return "Результатов пока нет";
        }

        if (blockedCount.Value > 0 && blockedTargets is { Count: > 0 })
        {
            return $"Подозрение на freeze: {string.Join(", ", blockedTargets.Take(2))}";
        }

        if (failCount.Value > 0)
        {
            return "Есть ошибки соединения";
        }

        if (unsupportedCount.Value > 0)
        {
            return "Есть неподдерживаемые проверки";
        }

        return "Подозрений на freeze не найдено";
    }

    private void UpdateSelectedSummary()
    {
        if (SelectedSummaryRow is null)
        {
            SelectedConfigNameTextBlock.Text = "Конфиг не выбран";
            SelectedSummaryTextBlock.Text = "Выберите строку в таблице.";
            SelectedBlockedTextBlock.Text = string.Empty;
            SelectedBlockedTextBlock.Visibility = Visibility.Collapsed;
            RunSelectedButton.IsEnabled = false;
            return;
        }

        SelectedConfigNameTextBlock.Text = SelectedSummaryRow.ConfigName;
        if (!_resultMap.TryGetValue(SelectedSummaryRow.FilePath, out var result))
        {
            SelectedSummaryTextBlock.Text = "У этого конфига ещё нет результатов TCP 16-20.";
            SelectedBlockedTextBlock.Text = string.Empty;
            SelectedBlockedTextBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            SelectedSummaryTextBlock.Text =
                $"OK {result.OkCount} • BLOCKED {result.BlockedCount} • FAIL {result.FailCount} • UNSUP {result.UnsupportedCount}";
            if (result.BlockedTargets.Count == 0)
            {
                SelectedBlockedTextBlock.Text = "Подозрения на 16-20 KB freeze не зафиксированы.";
                SelectedBlockedTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                SelectedBlockedTextBlock.Text = $"Подозрение на freeze: {string.Join(", ", result.BlockedTargets.Take(4))}";
                SelectedBlockedTextBlock.Visibility = Visibility.Visible;
            }
        }

        RunSelectedButton.IsEnabled = _runCancellation is null;
    }

    private void ConfigGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.DataGrid { SelectedItem: TcpFreezeConfigTableRow row })
        {
            return;
        }

        SelectedSummaryRow = row;
    }

    private void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: TcpFreezeConfigTableRow row })
        {
            return;
        }

        if (!_resultMap.TryGetValue(row.FilePath, out var result))
        {
            return;
        }

        if (_detailsWindow is not null)
        {
            try
            {
                _detailsWindow.Close();
            }
            catch
            {
            }

            _detailsWindow = null;
        }

        var window = new TcpFreezeDetailsWindow(result, _useLightTheme)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_detailsWindow, window))
            {
                _detailsWindow = null;
            }
        };

        _detailsWindow = window;
        window.Show();
        window.Activate();
    }

    private void UpdateProgressDisplay()
    {
        if (_totalConfigsForRun <= 0)
        {
            ProgressMetricTextBlock.Text = "0 / 0";
            ProgressCountTextBlock.Text = "0 / 0";
            return;
        }

        ProgressMetricTextBlock.Text = $"{Math.Min(_completedConfigs, _totalConfigsForRun)} / {_totalConfigsForRun}";

        if (_totalTargetsPerConfig <= 0)
        {
            ProgressCountTextBlock.Text = $"{Math.Min(_completedConfigs, _totalConfigsForRun)} / {_totalConfigsForRun}";
            return;
        }

        var completedSteps = (_completedConfigs * _totalTargetsPerConfig) + _processedTargetsForCurrentConfig;
        var totalSteps = _totalConfigsForRun * _totalTargetsPerConfig;
        completedSteps = Math.Min(completedSteps, totalSteps);
        ProgressCountTextBlock.Text = $"{completedSteps} / {totalSteps}";
    }

    private void AddLogLine(string line)
    {
        if (_logBuilder.Length > 0)
        {
            _logBuilder.AppendLine();
        }

        _logBuilder.Append(line);
        LogTextBox.Text = _logBuilder.ToString();
        LogTextBox.CaretIndex = LogTextBox.Text.Length;
        LogTextBox.ScrollToEnd();
    }

    private void SetRunningState(bool isRunning, string? idleStatus = null)
    {
        RunAllButton.IsEnabled = !isRunning;
        RunSelectedButton.IsEnabled = !isRunning && SelectedSummaryRow is not null;
        ActionButton.Content = isRunning ? "Отмена" : "Закрыть";
        StatusTextBlock.Text = isRunning ? "Проверка выполняется..." : idleStatus ?? "Инструмент готов к запуску.";
    }

    private void ApplyTheme(bool useLightTheme)
    {
        var accentBorder = useLightTheme ? "#5C90FF" : "#6E9CFF";
        SetBrushColor("WindowBgBrush", useLightTheme ? "#FAFCFF" : "#0E1828");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#DCE7F2" : "#22344E");
        SetBrushColor("MainTextBrush", useLightTheme ? "#0E1B2B" : "#F4F8FF");
        SetBrushColor("TextMutedBrush", useLightTheme ? "#637693" : "#90A3BF");
        SetBrushColor("CardBrush", useLightTheme ? "#FFFFFF" : "#0E1828");
        SetBrushColor("CardBrush2", useLightTheme ? "#FBFDFF" : "#0A1523");
        SetBrushColor("CardEdgeBrush", useLightTheme ? "#E4EDF7" : "#1D3048");
        SetBrushColor("InputBrush", useLightTheme ? "#FFFFFF" : "#0C1626");
        SetBrushColor("InputBorderBrush", useLightTheme ? "#D8E3F0" : "#24364F");
        SetBrushColor("ActionBrush", useLightTheme ? "#F6F9FD" : "#111C2D");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#D8E2EF" : "#263952");
        SetBrushColor("ActionHoverBrush", useLightTheme ? "#EDF3FA" : "#172536");
        SetBrushColor("SelectionBrush", useLightTheme ? "#E8F0FF" : "#142848");
        SetBrushColor("AccentBrush", useLightTheme ? "#2B70F7" : "#2F6BFF");
        SetBrushColor("AccentHoverBrush", accentBorder);
        SetBrushColor("AccentTextBrush", "#F7FBFF");
        SetBrushColor("DisabledBrush", useLightTheme ? "#EEF3F8" : "#101827");
        SetBrushColor("DisabledBorderBrush", useLightTheme ? "#D6E0EC" : "#1A2A3E");
        SetBrushColor("DisabledTextBrush", useLightTheme ? "#8DA0B8" : "#61748D");
        SetBrushColor("ScrollTrackBrush", useLightTheme ? "#EEF3F8" : "#101A2A");
        SetBrushColor("ScrollThumbBrush", useLightTheme ? "#B1C0D2" : "#3D526D");
        SetBrushColor("ScrollThumbHoverBrush", useLightTheme ? "#94A8C0" : "#587392");

        SetBrushColor("ProbeBadgeSuccessBackgroundBrush", useLightTheme ? "#E6F6EF" : "#15382C");
        SetBrushColor("ProbeBadgeSuccessBorderBrush", useLightTheme ? "#9AD4B9" : "#2A8761");
        SetBrushColor("ProbeBadgeSuccessForegroundBrush", useLightTheme ? "#247B53" : "#77E0AF");
        SetBrushColor("ProbeBadgePartialBackgroundBrush", useLightTheme ? "#FFF2DA" : "#43361C");
        SetBrushColor("ProbeBadgePartialBorderBrush", useLightTheme ? "#E3BE79" : "#A88035");
        SetBrushColor("ProbeBadgePartialForegroundBrush", useLightTheme ? "#A56E14" : "#F3C76C");
        SetBrushColor("ProbeBadgeFailureBackgroundBrush", useLightTheme ? "#FCEAEA" : "#3E2023");
        SetBrushColor("ProbeBadgeFailureBorderBrush", useLightTheme ? "#E4A2A7" : "#A54A57");
        SetBrushColor("ProbeBadgeFailureForegroundBrush", useLightTheme ? "#B24B58" : "#F0A4AD");
        SetBrushColor("ProbeBadgeNeutralBackgroundBrush", useLightTheme ? "#F2F6FB" : "#101B2D");
        SetBrushColor("ProbeBadgeNeutralBorderBrush", useLightTheme ? "#C7D6E6" : "#314A66");
        SetBrushColor("ProbeBadgeNeutralForegroundBrush", useLightTheme ? "#667A95" : "#9AB0CB");
    }

    private void SetBrushColor(string key, string color)
    {
        Resources[key] = new SolidColorBrush((MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }
}
