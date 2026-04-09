using System.Text.RegularExpressions;

namespace ZapretManager.Models;

public static class ProbeBadgeHelper
{
    public static string BuildBadgeText(ConfigProbeResult? probeResult)
    {
        if (probeResult is null)
        {
            return string.Empty;
        }

        if (HasOnlyDnsIssues(probeResult))
        {
            return "DNS";
        }

        return probeResult.Outcome switch
        {
            ProbeOutcomeKind.Success => "✓",
            ProbeOutcomeKind.Partial => "!",
            _ => "✕"
        };
    }

    public static string BuildBadgeText(ConnectivityTargetResult result)
    {
        if (result.IsDiagnosticOnly)
        {
            return "—";
        }

        if (HasOnlyDnsIssues(result))
        {
            return "DNS";
        }

        return result.Outcome switch
        {
            ProbeOutcomeKind.Success => "✓",
            ProbeOutcomeKind.Partial => "!",
            _ => "✕"
        };
    }

    public static bool HasOnlyDnsIssues(ConfigProbeResult probeResult)
    {
        var problematicTargets = probeResult.TargetResults
            .Where(result => !result.IsDiagnosticOnly && BuildBadgeText(result) is not "✓" and not "—")
            .ToArray();

        return problematicTargets.Length > 0 &&
               problematicTargets.All(HasOnlyDnsIssues);
    }

    public static bool HasOnlyDnsIssues(ConnectivityTargetResult result)
    {
        if (result.IsDiagnosticOnly)
        {
            return false;
        }

        var statuses = ParseProtocolStatuses(result);
        if (statuses.Count == 0)
        {
            return false;
        }

        var problematicStatuses = statuses.Values
            .Where(value => !string.Equals(value, "OK", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return problematicStatuses.Length > 0 &&
               problematicStatuses.All(value => string.Equals(value, "DNS", StringComparison.OrdinalIgnoreCase));
    }

    public static Dictionary<string, string> ParseProtocolStatuses(ConnectivityTargetResult result)
    {
        return ParseProtocolStatuses(result.HttpStatus, result.IsDiagnosticOnly);
    }

    public static Dictionary<string, string> ParseProtocolStatuses(string httpStatus, bool isDiagnosticOnly = false)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(httpStatus))
        {
            return map;
        }

        foreach (Match match in Regex.Matches(httpStatus, @"(?<label>HTTP|TLS1\.2|TLS1\.3|PING):(?<value>[A-Z0-9\.]+)", RegexOptions.IgnoreCase))
        {
            map[match.Groups["label"].Value.ToUpperInvariant()] = match.Groups["value"].Value.ToUpperInvariant();
        }

        if (map.Count > 0)
        {
            return map;
        }

        if (string.Equals(httpStatus, "TLS OK", StringComparison.OrdinalIgnoreCase))
        {
            map["TLS1.2"] = "OK";
            map["TLS1.3"] = "OK";
            return map;
        }

        if (string.Equals(httpStatus, "TCP OK", StringComparison.OrdinalIgnoreCase))
        {
            map["HTTP"] = "OK";
            return map;
        }

        if (string.Equals(httpStatus, "PING OK", StringComparison.OrdinalIgnoreCase))
        {
            map["PING"] = "OK";
            return map;
        }

        if (!isDiagnosticOnly)
        {
            map["HTTP"] = "ERROR";
        }

        return map;
    }
}
