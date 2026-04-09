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
            ? "Все цели доступны"
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
        SetBrushColor("WindowBgBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("WindowBorderBrush", useLightTheme ? "#9AB7D3" : "#295276");
        SetBrushColor("TextBrush", useLightTheme ? "#183049" : "#FFFFFF");
        SetBrushColor("MutedBrush", useLightTheme ? "#5E7893" : "#9AB2CD");
        SetBrushColor("PanelBrush", useLightTheme ? "#EEF5FB" : "#0C1A28");
        SetBrushColor("PanelBorderBrush", useLightTheme ? "#9CB7CF" : "#274A6B");
        SetBrushColor("ActionBrush", useLightTheme ? "#DCE9F5" : "#173454");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#87A8C8" : "#31597F");
        SetBrushColor("GridRowBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("GridAltRowBrush", useLightTheme ? "#EEF5FB" : "#0D1C2C");

        SetBrushColor("SummarySuccessBrush", useLightTheme ? "#D9EDE3" : "#27423D");
        SetBrushColor("SummarySuccessBorderBrush", useLightTheme ? "#A8C4B7" : "#6C9184");
        SetBrushColor("SummarySuccessIconBrush", useLightTheme ? "#2E6350" : "#7FE0B4");
        SetBrushColor("SummaryPartialBrush", useLightTheme ? "#EEE4D3" : "#4A422E");
        SetBrushColor("SummaryPartialBorderBrush", useLightTheme ? "#C7B18A" : "#A89566");
        SetBrushColor("SummaryPartialIconBrush", useLightTheme ? "#755B2F" : "#E3C168");
        SetBrushColor("SummaryFailureBrush", useLightTheme ? "#ECDEDF" : "#4F3537");
        SetBrushColor("SummaryFailureBorderBrush", useLightTheme ? "#C29A9A" : "#B88484");
        SetBrushColor("SummaryFailureIconBrush", useLightTheme ? "#A75A5A" : "#E6B3B1");

        SetBrushColor("ProbeBadgeSuccessBackgroundBrush", useLightTheme ? "#D9EDE3" : "#27423D");
        SetBrushColor("ProbeBadgeSuccessBorderBrush", useLightTheme ? "#A8C4B7" : "#6C9184");
        SetBrushColor("ProbeBadgeSuccessForegroundBrush", useLightTheme ? "#2F8E63" : "#7FE0B4");
        SetBrushColor("ProbeBadgePartialBackgroundBrush", useLightTheme ? "#EEE4D3" : "#4A422E");
        SetBrushColor("ProbeBadgePartialBorderBrush", useLightTheme ? "#C7B18A" : "#A89566");
        SetBrushColor("ProbeBadgePartialForegroundBrush", useLightTheme ? "#9A6E1D" : "#E3C168");
        SetBrushColor("ProbeBadgeFailureBackgroundBrush", useLightTheme ? "#ECDEDF" : "#4F3537");
        SetBrushColor("ProbeBadgeFailureBorderBrush", useLightTheme ? "#C29A9A" : "#B88484");
        SetBrushColor("ProbeBadgeFailureForegroundBrush", useLightTheme ? "#A75A5A" : "#E6B3B1");
        SetBrushColor("ProbeBadgeNeutralBackgroundBrush", useLightTheme ? "#EEF5FB" : "#0C1A28");
        SetBrushColor("ProbeBadgeNeutralBorderBrush", useLightTheme ? "#9CB7CF" : "#274A6B");
        SetBrushColor("ProbeBadgeNeutralForegroundBrush", useLightTheme ? "#5E7893" : "#9AB2CD");

        SetBrushColor("SuccessTextBrush", useLightTheme ? "#2F8E63" : "#7FE0B4");
        SetBrushColor("PartialTextBrush", useLightTheme ? "#9A6E1D" : "#E3C168");
        SetBrushColor("FailureTextBrush", useLightTheme ? "#A75A5A" : "#E6B3B1");
        SetBrushColor("NeutralTextBrush", useLightTheme ? "#6D849C" : "#9AB2CD");
        SetBrushColor("PingTextBrush", useLightTheme ? "#2C89A1" : "#96D6E8");
        SetBrushColor("ScrollTrackBrush", useLightTheme ? "#E4EEF7" : "#102235");
        SetBrushColor("ScrollThumbBrush", useLightTheme ? "#8EA9C2" : "#4A6A86");
        SetBrushColor("ScrollThumbHoverBrush", useLightTheme ? "#7897B5" : "#5B7C98");

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
