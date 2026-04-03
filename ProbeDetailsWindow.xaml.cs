using System.Windows;
using System.Windows.Media;
using System.Text.RegularExpressions;
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
        MediaBrush DetailBrush,
        MediaBrush DetailBadgeBackground,
        MediaBrush DetailBadgeBorder,
        MediaBrush DetailBadgeForeground,
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
        var statuses = ParseStatuses(result);
        var detailText = string.IsNullOrWhiteSpace(result.Details)
            ? "—"
            : result.Details;
        var compactStatusText = result.IsDiagnosticOnly
            ? "—"
            : BuildCompactStatusText(result);
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
            DetailBrush: string.Equals(compactStatusText, "—", StringComparison.Ordinal) ? (MediaBrush)FindResource("MutedBrush") : (MediaBrush)FindResource("TextBrush"),
            DetailBadgeBackground: GetDetailBadgeBackground(result),
            DetailBadgeBorder: GetDetailBadgeBorder(result),
            DetailBadgeForeground: GetDetailBadgeForeground(result),
            RowToolTip: rowToolTip);
    }

    private static Dictionary<string, string> ParseStatuses(ConnectivityTargetResult result)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(result.HttpStatus, @"(?<label>HTTP|TLS1\.2|TLS1\.3|PING):(?<value>[A-Z0-9\.]+)", RegexOptions.IgnoreCase))
        {
            map[match.Groups["label"].Value.ToUpperInvariant()] = match.Groups["value"].Value.ToUpperInvariant();
        }

        if (map.Count > 0)
        {
            return map;
        }

        if (string.Equals(result.HttpStatus, "TLS OK", StringComparison.OrdinalIgnoreCase))
        {
            map["TLS1.2"] = "OK";
            map["TLS1.3"] = "OK";
            return map;
        }

        if (string.Equals(result.HttpStatus, "TCP OK", StringComparison.OrdinalIgnoreCase))
        {
            map["HTTP"] = "OK";
            return map;
        }

        if (string.Equals(result.HttpStatus, "PING OK", StringComparison.OrdinalIgnoreCase))
        {
            map["PING"] = "OK";
            return map;
        }

        if (!string.IsNullOrWhiteSpace(result.HttpStatus) && !result.IsDiagnosticOnly)
        {
            map["HTTP"] = "ERROR";
        }

        return map;
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

    private static string BuildCompactStatusText(ConnectivityTargetResult result)
    {
        return result.Outcome switch
        {
            ProbeOutcomeKind.Success => "✓",
            ProbeOutcomeKind.Partial => "!",
            _ => "✕"
        };
    }

    private MediaBrush GetDetailBadgeBackground(ConnectivityTargetResult result)
    {
        if (result.IsDiagnosticOnly)
        {
            return (MediaBrush)FindResource("PanelBrush");
        }

        return result.Outcome switch
        {
            ProbeOutcomeKind.Success => (MediaBrush)FindResource("SummarySuccessBrush"),
            ProbeOutcomeKind.Partial => (MediaBrush)FindResource("SummaryPartialBrush"),
            _ => (MediaBrush)FindResource("SummaryFailureBrush")
        };
    }

    private MediaBrush GetDetailBadgeBorder(ConnectivityTargetResult result)
    {
        if (result.IsDiagnosticOnly)
        {
            return (MediaBrush)FindResource("PanelBorderBrush");
        }

        return result.Outcome switch
        {
            ProbeOutcomeKind.Success => (MediaBrush)FindResource("SummarySuccessBorderBrush"),
            ProbeOutcomeKind.Partial => (MediaBrush)FindResource("SummaryPartialBorderBrush"),
            _ => (MediaBrush)FindResource("SummaryFailureBorderBrush")
        };
    }

    private MediaBrush GetDetailBadgeForeground(ConnectivityTargetResult result)
    {
        if (result.IsDiagnosticOnly)
        {
            return (MediaBrush)FindResource("MutedBrush");
        }

        return result.Outcome switch
        {
            ProbeOutcomeKind.Success => (MediaBrush)FindResource("SummarySuccessIconBrush"),
            ProbeOutcomeKind.Partial => (MediaBrush)FindResource("SummaryPartialIconBrush"),
            _ => (MediaBrush)FindResource("SummaryFailureIconBrush")
        };
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
        SetBrushColor("SummarySuccessIconBrush", useLightTheme ? "#2E6350" : "#FFFFFF");
        SetBrushColor("SummaryPartialBrush", useLightTheme ? "#EEE4D3" : "#4A422E");
        SetBrushColor("SummaryPartialBorderBrush", useLightTheme ? "#C7B18A" : "#A89566");
        SetBrushColor("SummaryPartialIconBrush", useLightTheme ? "#755B2F" : "#FFFFFF");
        SetBrushColor("SummaryFailureBrush", useLightTheme ? "#EEDDE3" : "#4C3941");
        SetBrushColor("SummaryFailureBorderBrush", useLightTheme ? "#C8A8B2" : "#B28A97");
        SetBrushColor("SummaryFailureIconBrush", useLightTheme ? "#764754" : "#FFFFFF");

        SetBrushColor("SuccessTextBrush", useLightTheme ? "#2F8E63" : "#7FE0B4");
        SetBrushColor("PartialTextBrush", useLightTheme ? "#9A6E1D" : "#E3C168");
        SetBrushColor("FailureTextBrush", useLightTheme ? "#A44C64" : "#E6A6B8");
        SetBrushColor("NeutralTextBrush", useLightTheme ? "#6D849C" : "#9AB2CD");
        SetBrushColor("PingTextBrush", useLightTheme ? "#2C89A1" : "#96D6E8");
        SetBrushColor("ScrollTrackBrush", useLightTheme ? "#E4EEF7" : "#102235");
        SetBrushColor("ScrollThumbBrush", useLightTheme ? "#B7C9DB" : "#35506A");
        SetBrushColor("ScrollThumbHoverBrush", useLightTheme ? "#9FB8D0" : "#466481");

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
