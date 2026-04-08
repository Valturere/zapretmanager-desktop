using System.Diagnostics;
using ZapretManager.Models;

namespace ZapretManager.Services;

internal static class ZapretBatchLauncher
{
    public static readonly TimeSpan DefaultSilentStartTimeout = TimeSpan.FromSeconds(25);

    public static async Task<Process> StartAndAttachWinwsAsync(
        ZapretInstallation installation,
        ConfigProfile profile,
        TimeSpan detectionTimeout,
        bool suppressUpdateCheck = true)
    {
        var knownProcessIds = EnumerateMatchingWinwsProcesses(installation)
            .Select(process => process.Id)
            .ToHashSet();

        using var starterProcess = Process.Start(CreateBatchStartInfo(installation, profile, suppressUpdateCheck))
            ?? throw new InvalidOperationException($"Не удалось запустить профиль {profile.FileName}.");

        var startedAtUtc = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAtUtc < detectionTimeout)
        {
            var matches = EnumerateMatchingWinwsProcesses(installation)
                .OrderByDescending(GetSafeStartTimeUtc)
                .ToList();

            Process? candidate = null;
            foreach (var process in matches)
            {
                if (knownProcessIds.Contains(process.Id))
                {
                    process.Dispose();
                    continue;
                }

                candidate = process;
                break;
            }

            foreach (var process in matches)
            {
                if (!ReferenceEquals(process, candidate))
                {
                    process.Dispose();
                }
            }

            if (candidate is not null)
            {
                return candidate;
            }

            if (starterProcess.HasExited && starterProcess.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Профиль {profile.FileName} завершился до запуска winws.exe. Код выхода: {starterProcess.ExitCode}.");
            }

            await Task.Delay(250);
        }

        throw new InvalidOperationException(
            $"Профиль {profile.FileName} не успел запустить winws.exe. На некоторых ПК запуск может занимать больше времени. Повторите попытку.");
    }

    private static ProcessStartInfo CreateBatchStartInfo(ZapretInstallation installation, ConfigProfile profile, bool suppressUpdateCheck)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = installation.RootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(profile.FilePath);

        if (suppressUpdateCheck)
        {
            startInfo.Environment["NO_UPDATE_CHECK"] = "1";
        }

        return startInfo;
    }

    private static IEnumerable<Process> EnumerateMatchingWinwsProcesses(ZapretInstallation installation)
    {
        foreach (var process in Process.GetProcessesByName("winws"))
        {
            if (BelongsToInstallation(process, installation))
            {
                yield return process;
            }
            else
            {
                process.Dispose();
            }
        }
    }

    private static bool BelongsToInstallation(Process process, ZapretInstallation installation)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(path) &&
                   path.StartsWith(installation.RootPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            process.Dispose();
            return false;
        }
    }

    private static DateTime GetSafeStartTimeUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
}
