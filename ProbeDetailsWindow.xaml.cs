using System.Windows;
using System.Windows.Media;
using ZapretManager.Models;
using ZapretManager.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace ZapretManager;

public partial class ProbeDetailsWindow : Window
{
    private sealed record ProtocolCellViewModel(
        string Text,
        MediaBrush Foreground,
        string Tooltip);

    private sealed record ProbeDetailRow(
        string TargetName,
        ProtocolCellViewModel Http,
        ProtocolCellViewModel Tls12,
        ProtocolCellViewModel Tls13,
        string PingText,
        MediaBrush PingBrush,
        string PingToolTip,
        string DetailText,
        string RowToolTip);

    private readonly string _configName;
    private readonly ConfigProbeResult _probeResult;

    public ProbeDetailsWindow(string configName, ConfigProbeResult probeResult, bool useLightTheme)
    {
        InitializeComponent();
        _configName = configName;
        _probeResult = probeResult;
        ApplyTheme(useLightTheme);
        RefreshView();
    }

    private void RefreshView()
    {
        TitleTextBlock.Text = _configName;
        SummaryTextBlock.Text = _probeResult.Summary == "✓"
            ? "Все домены доступны"
            : _probeResult.Summary.Length > 1
                ? _probeResult.Summary[1..].TrimStart()
                : _probeResult.Summary;
        StatsTextBlock.Text =
            $"Полностью доступны: {_probeResult.SuccessCount}/{_probeResult.TotalCount}  •  Частично: {_probeResult.PartialCount}  •  Средний отклик: {(_probeResult.AveragePingMilliseconds?.ToString() ?? "—")} мс";

        ApplySummaryBadge(_probeResult.Outcome);

        DataContext = new
        {
            Rows = _probeResult.TargetResults
                .OrderBy(result => GetSummarySortOrder(result.TargetName))
                .ThenBy(result => ConnectivityTestService.GetDetailSortOrder(result.TargetName))
                .ThenBy(result => result.TargetName, StringComparer.OrdinalIgnoreCase)
                .Select(BuildRow)
                .ToArray()
        };
    }

    private ProbeDetailRow BuildRow(ConnectivityTargetResult result)
    {
        var statuses = ProbeBadgeHelper.ParseProtocolStatuses(result);
        var compactStatusText = result.IsDiagnosticOnly
            ? "—"
            : ProbeBadgeHelper.BuildBadgeText(result);
        var rowToolTip = string.IsNullOrWhiteSpace(result.Details)
            ? result.HttpStatus
            : $"{result.HttpStatus}\n{result.Details}";

        return new ProbeDetailRow(
            TargetName: result.TargetName,
            Http: BuildProtocolCell(statuses, "HTTP"),
            Tls12: BuildProtocolCell(statuses, "TLS1.2"),
            Tls13: BuildProtocolCell(statuses, "TLS1.3"),
            PingText: BuildPingText(result),
            PingBrush: GetPingBrush(result),
            PingToolTip: result.PingMilliseconds.HasValue ? $"Средний отклик: {result.PingMilliseconds.Value} мс" : "Отклик не получен",
            DetailText: compactStatusText,
            RowToolTip: rowToolTip);
    }

    private ProtocolCellViewModel BuildProtocolCell(IReadOnlyDictionary<string, string> statuses, string key)
    {
        if (!statuses.TryGetValue(key, out var value))
        {
            return new ProtocolCellViewModel("—", (MediaBrush)FindResource("NeutralTextBrush"), "Проверка не выполнялась");
        }

        return value switch
        {
            "OK" => new ProtocolCellViewModel("OK", (MediaBrush)FindResource("SuccessTextBrush"), "Проверка пройдена"),
            "UNSUP" => new ProtocolCellViewModel("UNSUP", (MediaBrush)FindResource("PartialTextBrush"), "Протокол недоступен или не поддерживается"),
            "DNS" => new ProtocolCellViewModel("DNS", (MediaBrush)FindResource("PartialTextBrush"), "Обычный системный DNS не прошёл или потребовался DoH fallback"),
            "SSL" => new ProtocolCellViewModel("SSL", (MediaBrush)FindResource("FailureTextBrush"), "Проверка упёрлась в SSL/TLS"),
            "ERROR" => new ProtocolCellViewModel("ERROR", (MediaBrush)FindResource("FailureTextBrush"), "Проверка не пройдена"),
            _ => new ProtocolCellViewModel(value, (MediaBrush)FindResource("NeutralTextBrush"), value)
        };
    }

    private static string BuildPingText(ConnectivityTargetResult result)
    {
        if (result.PingMilliseconds.HasValue)
        {
            return $"{result.PingMilliseconds.Value} ms";
        }

        return result.IsDiagnosticOnly && !result.Success ? "Timeout" : "—";
    }

    private MediaBrush GetPingBrush(ConnectivityTargetResult result)
    {
        if (result.PingMilliseconds.HasValue)
        {
            return (MediaBrush)FindResource("PingTextBrush");
        }

        return result.IsDiagnosticOnly && !result.Success
            ? (MediaBrush)FindResource("FailureTextBrush")
            : (MediaBrush)FindResource("NeutralTextBrush");
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

        SetBrushColor("SummarySuccessBrush", useLightTheme ? "#E6F6EF" : "#15382C");
        SetBrushColor("SummarySuccessBorderBrush", useLightTheme ? "#9AD4B9" : "#2A8761");
        SetBrushColor("SummarySuccessIconBrush", useLightTheme ? "#247B53" : "#77E0AF");
        SetBrushColor("SummaryPartialBrush", useLightTheme ? "#FFF2DA" : "#43361C");
        SetBrushColor("SummaryPartialBorderBrush", useLightTheme ? "#E3BE79" : "#A88035");
        SetBrushColor("SummaryPartialIconBrush", useLightTheme ? "#A56E14" : "#F3C76C");
        SetBrushColor("SummaryFailureBrush", useLightTheme ? "#FCEAEA" : "#3E2023");
        SetBrushColor("SummaryFailureBorderBrush", useLightTheme ? "#E4A2A7" : "#A54A57");
        SetBrushColor("SummaryFailureIconBrush", useLightTheme ? "#B24B58" : "#F0A4AD");

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
        SetBrushColor("PartialTextBrush", useLightTheme ? "#A56E14" : "#F3C76C");
        SetBrushColor("FailureTextBrush", useLightTheme ? "#B24B58" : "#F0A4AD");
        SetBrushColor("NeutralTextBrush", useLightTheme ? "#667A95" : "#9AB0CB");
        SetBrushColor("PingTextBrush", useLightTheme ? "#2B70F7" : "#6E9CFF");
        SetBrushColor("ScrollTrackBrush", useLightTheme ? "#EEF3F8" : "#101A2A");
        SetBrushColor("ScrollThumbBrush", useLightTheme ? "#B1C0D2" : "#3D526D");
        SetBrushColor("ScrollThumbHoverBrush", useLightTheme ? "#94A8C0" : "#587392");

        if (TitleTextBlock is not null)
        {
            RefreshView();
        }
    }

    private void ApplySummaryBadge(ProbeOutcomeKind outcome)
    {
        SummarySuccessPath.Visibility = outcome == ProbeOutcomeKind.Success ? Visibility.Visible : Visibility.Collapsed;
        SummaryPartialGrid.Visibility = outcome == ProbeOutcomeKind.Partial ? Visibility.Visible : Visibility.Collapsed;
        SummaryFailurePath.Visibility = outcome == ProbeOutcomeKind.Failure ? Visibility.Visible : Visibility.Collapsed;

        switch (outcome)
        {
            case ProbeOutcomeKind.Success:
                SummaryBadgeBorder.Background = (MediaBrush)FindResource("SummarySuccessBrush");
                SummaryBadgeBorder.BorderBrush = (MediaBrush)FindResource("SummarySuccessBorderBrush");
                break;
            case ProbeOutcomeKind.Partial:
                SummaryBadgeBorder.Background = (MediaBrush)FindResource("SummaryPartialBrush");
                SummaryBadgeBorder.BorderBrush = (MediaBrush)FindResource("SummaryPartialBorderBrush");
                break;
            default:
                SummaryBadgeBorder.Background = (MediaBrush)FindResource("SummaryFailureBrush");
                SummaryBadgeBorder.BorderBrush = (MediaBrush)FindResource("SummaryFailureBorderBrush");
                break;
        }
    }

    private void SetBrushColor(string key, string color)
    {
        Resources[key] = new SolidColorBrush((MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private static int GetSummarySortOrder(string targetName)
    {
        return ConnectivityTestService.ToSummaryDisplayName(targetName) switch
        {
            "Discord" => 0,
            "YouTube" => 1,
            "Google" => 2,
            "Cloudflare" => 3,
            "Instagram" => 4,
            "TikTok" => 5,
            "X / Twitter" => 6,
            "Twitch" => 7,
            _ => 20
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

}
