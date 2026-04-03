using ZapretManager.Models;

namespace ZapretManager.Services;

public static class AdminTaskDispatcher
{
    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var discovery = new ZapretDiscoveryService();
        var serviceManager = new WindowsServiceManager();
        var updateService = new UpdateService();
        var processService = new ZapretProcessService();
        var connectivityService = new ConnectivityTestService();
        var gameModeService = new GameModeService();
        var dnsService = new DnsService();

        switch (args[0])
        {
            case "--start-profile" when args.Length >= 3:
            {
                var installation = discovery.TryLoad(args[1]) ?? throw new InvalidOperationException("Папка zapret не найдена.");
                var profile = installation.Profiles.FirstOrDefault(item =>
                    string.Equals(item.FilePath, args[2], StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Name, args[2], StringComparison.OrdinalIgnoreCase));

                if (profile is null)
                {
                    throw new InvalidOperationException("Выбранный конфиг не найден.");
                }

                await processService.StartAsync(installation, profile);
                return 0;
            }

            case "--stop-profile" when args.Length >= 2:
            {
                var installation = discovery.TryLoad(args[1]) ?? throw new InvalidOperationException("Папка zapret не найдена.");
                await processService.StopAsync(installation);
                return 0;
            }

            case "--install-service" when args.Length >= 3:
            {
                var installation = discovery.TryLoad(args[1]) ?? throw new InvalidOperationException("Папка zapret не найдена.");
                var profile = installation.Profiles.FirstOrDefault(item =>
                    string.Equals(item.FilePath, args[2], StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Name, args[2], StringComparison.OrdinalIgnoreCase));

                if (profile is null)
                {
                    throw new InvalidOperationException("Выбранный конфиг не найден.");
                }

                await serviceManager.InstallAsync(installation, profile);
                return 0;
            }

            case "--remove-service":
                await serviceManager.RemoveAsync();
                return 0;

            case "--apply-update" when args.Length >= 4:
                await updateService.ApplyUpdateAsync(args[1], args[2], args[3]);
                return 0;

            case "--set-game-mode" when args.Length >= 3:
            {
                var installation = discovery.TryLoad(args[1]) ?? throw new InvalidOperationException("Папка zapret не найдена.");
                gameModeService.SetMode(installation, args[2]);
                return 0;
            }

            case "--set-dns-profile" when args.Length >= 2:
            {
                var customPrimary = args.Length >= 3 && !string.Equals(args[2], "__EMPTY__", StringComparison.Ordinal)
                    ? args[2]
                    : null;
                var customSecondary = args.Length >= 4 && !string.Equals(args[3], "__EMPTY__", StringComparison.Ordinal)
                    ? args[3]
                    : null;
                var useDoh = args.Length >= 5 &&
                             bool.TryParse(args[4], out var parsedUseDoh) &&
                             parsedUseDoh;
                var customDohTemplate = args.Length >= 6 && !string.Equals(args[5], "__EMPTY__", StringComparison.Ordinal)
                    ? args[5]
                    : null;
                var resultPath = args.Length >= 7 && !string.IsNullOrWhiteSpace(args[6]) ? args[6] : null;

                try
                {
                    await dnsService.ApplyProfileAsync(args[1], customPrimary, customSecondary, useDoh, customDohTemplate);
                    await WriteAdminTaskResultAsync(resultPath, null);
                    return 0;
                }
                catch (Exception ex)
                {
                    await WriteAdminTaskResultAsync(resultPath, ex.Message);
                    return 1;
                }
            }

            case "--probe-configs" when args.Length >= 4:
            {
                var installation = discovery.TryLoad(args[1]) ?? throw new InvalidOperationException("Папка zapret не найдена.");
                var customTarget = string.Equals(args[2], "__DEFAULT__", StringComparison.Ordinal) ? null : args[2];
                var outputPath = args[3];
                var results = new List<ConfigProbeResult>();

                foreach (var profile in installation.Profiles)
                {
                    results.Add(await connectivityService.ProbeConfigAsync(installation, profile, customTarget));
                }

                var recommended = results
                    .OrderByDescending(item => item.SuccessRate)
                    .ThenByDescending(item => item.SuccessCount)
                    .ThenByDescending(item => item.SupplementarySuccessRate)
                    .ThenByDescending(item => item.SupplementarySuccessCount)
                    .ThenBy(item => item.ConfigName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                var payload = new ProbeBatchResult
                {
                    Results = results,
                    RecommendedConfigName = recommended?.ConfigName
                };

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllTextAsync(
                    outputPath,
                    System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

                return 0;
            }

            case "--probe-configs-progress" when args.Length >= 4:
            {
                var installation = discovery.TryLoad(args[1]) ?? throw new InvalidOperationException("Папка zapret не найдена.");
                var customTarget = string.Equals(args[2], "__DEFAULT__", StringComparison.Ordinal) ? null : args[2];
                var outputPath = args[3];
                var results = new List<ConfigProbeResult>();
                var total = installation.Profiles.Count;

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await WriteProgressAsync(outputPath, results, null, 0, total, isCompleted: false);

                foreach (var profile in installation.Profiles)
                {
                    await WriteProgressAsync(outputPath, results, profile.Name, results.Count, total, isCompleted: false);
                    results.Add(await connectivityService.ProbeConfigAsync(installation, profile, customTarget));
                    await WriteProgressAsync(outputPath, results, profile.Name, results.Count, total, isCompleted: false);
                }

                var recommended = results
                    .OrderByDescending(item => item.SuccessRate)
                    .ThenByDescending(item => item.SuccessCount)
                    .ThenByDescending(item => item.SupplementarySuccessRate)
                    .ThenByDescending(item => item.SupplementarySuccessCount)
                    .ThenBy(item => item.AveragePingMilliseconds ?? long.MaxValue)
                    .ThenBy(item => item.ConfigName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                await WriteProgressAsync(outputPath, results, null, results.Count, total, isCompleted: true, recommended?.ConfigName);
                return 0;
            }

            default:
                return null;
        }
    }

    private static async Task WriteProgressAsync(
        string outputPath,
        List<ConfigProbeResult> results,
        string? currentConfigName,
        int completed,
        int total,
        bool isCompleted,
        string? recommendedConfigName = null)
    {
        var payload = new ProbeProgressState
        {
            Results = [.. results],
            CurrentConfigName = currentConfigName,
            CompletedConfigs = completed,
            TotalConfigs = total,
            IsCompleted = isCompleted,
            RecommendedConfigName = recommendedConfigName
        };

        await File.WriteAllTextAsync(
            outputPath,
            System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task WriteAdminTaskResultAsync(string? outputPath, string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, errorMessage ?? string.Empty);
    }
}
