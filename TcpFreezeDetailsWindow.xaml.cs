using System.Windows;
using System.Windows.Media;
using ZapretManager.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace ZapretManager;

public partial class TcpFreezeDetailsWindow : Window
{
    private sealed record ProtocolCellViewModel(string Text, MediaBrush Foreground, string Tooltip);

    private sealed record DetailRow(
        string TargetName,
        ProtocolCellViewModel Http,
        ProtocolCellViewModel Tls12,
        ProtocolCellViewModel Tls13,
        string SummaryBadgeText,
        string SummaryTooltip,
        string RowToolTip);

    private readonly TcpFreezeConfigResult _result;

    public TcpFreezeDetailsWindow(TcpFreezeConfigResult result, bool useLightTheme)
    {
        InitializeComponent();
        _result = result;
        ApplyTheme(useLightTheme);
        RefreshView();
    }

    private void RefreshView()
    {
        TitleTextBlock.Text = _result.ConfigName;
        SummaryBadge.BadgeText = GetSummaryBadgeText(_result);
        SummaryTextBlock.Text = _result.BlockedCount > 0
            ? $"Есть подозрения на 16-20 KB freeze: {_result.BlockedCount}"
            : _result.FailCount > 0
                ? "Есть ошибки соединения"
                : "Подозрений на 16-20 KB freeze не найдено";

        StatsTextBlock.Text =
            $"OK: {_result.OkCount}  •  BLOCKED: {_result.BlockedCount}  •  FAIL: {_result.FailCount}  •  UNSUP: {_result.UnsupportedCount}";

        if (_result.BlockedTargets.Count > 0)
        {
            SubtitleTextBlock.Text = $"Проблемные цели: {string.Join(", ", _result.BlockedTargets.Take(4))}";
            SubtitleTextBlock.Visibility = Visibility.Visible;
        }
        else
        {
            SubtitleTextBlock.Visibility = Visibility.Collapsed;
        }

        ResultsGrid.ItemsSource = _result.TargetResults
            .Select(BuildRow)
            .ToArray();
    }

    private DetailRow BuildRow(TcpFreezeTargetResult result)
    {
        var statuses = result.Checks.ToDictionary(item => item.Label, StringComparer.OrdinalIgnoreCase);
        var rowToolTip = string.Join(Environment.NewLine, result.Checks.Select(check =>
            $"{check.Label}: code={check.Code}, up={check.UpBytes}, down={check.DownBytes}, time={check.TimeSeconds:0.###}s"));

        return new DetailRow(
            TargetName: $"{result.Country} {result.Provider} [{result.TargetId}] {result.Host}",
            Http: BuildProtocolCell(statuses, "HTTP"),
            Tls12: BuildProtocolCell(statuses, "TLS1.2"),
            Tls13: BuildProtocolCell(statuses, "TLS1.3"),
            SummaryBadgeText: BuildSummaryBadgeText(result.Checks),
            SummaryTooltip: string.Join("  •  ", result.Checks.Select(check => $"{check.Label}:{FormatProtocolText(check)}")),
            RowToolTip: rowToolTip);
    }

    private ProtocolCellViewModel BuildProtocolCell(IReadOnlyDictionary<string, TcpFreezeProtocolResult> statuses, string key)
    {
        if (!statuses.TryGetValue(key, out var value))
        {
            return new ProtocolCellViewModel("—", (MediaBrush)FindResource("NeutralTextBrush"), "Проверка не выполнялась");
        }

        return value.Status switch
        {
            TcpFreezeProtocolStatus.Ok => new ProtocolCellViewModel($"OK[{value.Code}]", (MediaBrush)FindResource("SuccessTextBrush"), BuildProtocolTooltip(value)),
            TcpFreezeProtocolStatus.LikelyBlocked => new ProtocolCellViewModel($"BLOCK[{value.Code}]", (MediaBrush)FindResource("BlockedTextBrush"), BuildProtocolTooltip(value)),
            TcpFreezeProtocolStatus.Unsupported => new ProtocolCellViewModel("UNSUP", (MediaBrush)FindResource("NeutralTextBrush"), BuildProtocolTooltip(value)),
            _ => new ProtocolCellViewModel($"FAIL[{value.Code}]", (MediaBrush)FindResource("FailTextBrush"), BuildProtocolTooltip(value))
        };
    }

    private static string BuildSummaryBadgeText(IReadOnlyList<TcpFreezeProtocolResult> checks)
    {
        if (checks.Any(check => check.Status == TcpFreezeProtocolStatus.LikelyBlocked))
        {
            return "✕";
        }

        if (checks.Any(check => check.Status == TcpFreezeProtocolStatus.Fail))
        {
            return "!";
        }

        if (checks.All(check => check.Status == TcpFreezeProtocolStatus.Unsupported))
        {
            return "—";
        }

        return "✓";
    }

    private static string FormatProtocolText(TcpFreezeProtocolResult result)
    {
        return result.Status switch
        {
            TcpFreezeProtocolStatus.Ok => $"OK[{result.Code}]",
            TcpFreezeProtocolStatus.LikelyBlocked => $"BLOCK[{result.Code}]",
            TcpFreezeProtocolStatus.Unsupported => "UNSUP",
            _ => $"FAIL[{result.Code}]"
        };
    }

    private static string BuildProtocolTooltip(TcpFreezeProtocolResult result)
    {
        return $"code={result.Code}, up={result.UpBytes}, down={result.DownBytes}, time={result.TimeSeconds:0.###}s";
    }

    private static string GetSummaryBadgeText(TcpFreezeConfigResult result)
    {
        if (result.FailCount == 0 && result.BlockedCount == 0)
        {
            return "✓";
        }

        if (result.OkCount == 0)
        {
            return "✕";
        }

        return "!";
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
        SetBrushColor("GridRowBrush", useLightTheme ? "#FFFFFF" : "#0E1828");
        SetBrushColor("GridAltRowBrush", useLightTheme ? "#F8FBFF" : "#0B1322");

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

        SetBrushColor("SuccessTextBrush", useLightTheme ? "#247B53" : "#77E0AF");
        SetBrushColor("BlockedTextBrush", useLightTheme ? "#B24B58" : "#F0A4AD");
        SetBrushColor("FailTextBrush", useLightTheme ? "#A56E14" : "#F3C76C");
        SetBrushColor("NeutralTextBrush", useLightTheme ? "#667A95" : "#9AB0CB");
        SetBrushColor("ScrollTrackBrush", useLightTheme ? "#EEF3F8" : "#101A2A");
        SetBrushColor("ScrollThumbBrush", useLightTheme ? "#B1C0D2" : "#3D526D");
        SetBrushColor("ScrollThumbHoverBrush", useLightTheme ? "#94A8C0" : "#587392");
    }

    private void SetBrushColor(string key, string color)
    {
        Resources[key] = new SolidColorBrush((MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
