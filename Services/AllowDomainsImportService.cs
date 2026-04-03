using System.Net.Http;
using System.Text.RegularExpressions;

namespace ZapretManager.Services;

public sealed class AllowDomainsImportService
{
    private const string RawBaseUrl = "https://raw.githubusercontent.com/itdoginfo/allow-domains/main/";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static readonly IReadOnlyList<AllowDomainsPreset> Presets =
    [
        new("youtube", "YouTube", "Services/youtube.lst"),
        new("discord", "Discord", "Services/discord.lst"),
        new("telegram", "Telegram", "Services/telegram.lst"),
        new("cloudflare", "Cloudflare", "Services/cloudflare.lst"),
        new("meta", "Meta", "Services/meta.lst"),
        new("tiktok", "TikTok", "Services/tiktok.lst"),
        new("twitter", "Twitter / X", "Services/twitter.lst"),
        new("roblox", "Roblox", "Services/roblox.lst"),
        new("google_ai", "Google AI", "Services/google_ai.lst"),
        new("google_meet", "Google Meet", "Services/google_meet.lst"),
        new("google_play", "Google Play", "Services/google_play.lst"),
        new("cloudfront", "CloudFront", "Services/cloudfront.lst"),
        new("digitalocean", "DigitalOcean", "Services/digitalocean.lst"),
        new("hdrezka", "Hdrezka", "Services/hdrezka.lst"),
        new("hetzner", "Hetzner", "Services/hetzner.lst"),
        new("ovh", "OVH", "Services/ovh.lst")
    ];

    public IReadOnlyList<AllowDomainsPreset> GetPresets() => Presets;

    public async Task<IReadOnlyList<string>> DownloadDomainsAsync(AllowDomainsPreset preset, CancellationToken cancellationToken = default)
    {
        string content;
        try
        {
            content = await HttpClient.GetStringAsync(RawBaseUrl + preset.RelativePath, cancellationToken);
        }
        catch (Exception ex) when (NetworkErrorTranslator.IsNetworkException(ex))
        {
            throw NetworkErrorTranslator.CreateGitHubException(ex, $"Не удалось загрузить список доменов {preset.Label}");
        }

        var domains = content
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.Trim())
            .Where(IsSupportedDomain)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (domains.Length == 0)
        {
            throw new InvalidOperationException("В выбранном списке не найдено ни одного подходящего домена.");
        }

        return domains;
    }

    private static bool IsSupportedDomain(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }

        if (trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.Contains('/') ||
            trimmed.Contains('\\') ||
            trimmed.Contains(' ') ||
            trimmed.Contains('\t') ||
            trimmed.Contains(':'))
        {
            return false;
        }

        return Regex.IsMatch(trimmed,
            @"^(?=.{1,253}$)(?!-)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$",
            RegexOptions.CultureInvariant);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretManager");
        return client;
    }
}

public sealed record AllowDomainsPreset(string Key, string Label, string RelativePath)
{
    public override string ToString() => Label;
}
