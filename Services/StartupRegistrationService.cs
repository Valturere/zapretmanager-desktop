using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace ZapretManager.Services;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyRunValueName = "ZapretManager";
    private const string TaskName = "ZapretManager";
    private const string StartupArguments = "--start-hidden";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var runValue = key?.GetValue(LegacyRunValueName) as string;
        if (!string.IsNullOrWhiteSpace(runValue))
        {
            return true;
        }

        var taskResult = RunPowerShell(BuildQueryScheduledTaskScript(), throwOnError: false);
        return taskResult.ExitCode == 0;
    }

    public void SetEnabled(bool enabled)
    {
        RemoveLegacyRunEntry();

        if (!enabled)
        {
            RemoveScheduledTask();
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Не удалось определить путь к текущему exe для автозапуска.");
        }

        RegisterScheduledTask(executablePath);
    }

    public string BuildSetEnabledErrorMessage(Exception exception, bool enable)
    {
        var details = DialogService.GetDisplayMessage(exception, enable
            ? "Не удалось настроить автозапуск ZapretManager."
            : "Не удалось изменить настройку автозапуска ZapretManager.");

        if (details.Contains("Что можно попробовать:", StringComparison.OrdinalIgnoreCase))
        {
            return details;
        }

        var actionText = enable
            ? "Не удалось включить автозапуск ZapretManager при входе в Windows."
            : "Не удалось выключить автозапуск ZapretManager.";

        return
            $"{actionText}{Environment.NewLine}{Environment.NewLine}" +
            "Проверьте Планировщик заданий Windows и повторите попытку." +
            $"{Environment.NewLine}{Environment.NewLine}Техническая причина:{Environment.NewLine}{details}";
    }

    private static void RemoveLegacyRunEntry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
                        ?? throw new InvalidOperationException("Не удалось открыть раздел автозапуска в реестре.");
        key.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);
    }

    private static void RegisterScheduledTask(string executablePath)
    {
        var workingDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException("Не удалось определить рабочую папку exe для автозапуска.");
        }

        var taskName = EscapePowerShellLiteral(TaskName);
        var escapedExecutablePath = EscapePowerShellLiteral(executablePath);
        var escapedWorkingDirectory = EscapePowerShellLiteral(workingDirectory);
        var escapedArguments = EscapePowerShellLiteral(StartupArguments);
        var escapedDescription = EscapePowerShellLiteral("Автозапуск ZapretManager при входе в Windows.");

        var script = $$"""
$ErrorActionPreference = 'Stop'
$taskName = '{{taskName}}'
$userId = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute '{{escapedExecutablePath}}' -Argument '{{escapedArguments}}' -WorkingDirectory '{{escapedWorkingDirectory}}'
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
$trigger.Delay = 'PT15S'
$principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances Ignore
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description '{{escapedDescription}}' -Force | Out-Null
""";

        RunPowerShell(script);
    }

    private static void RemoveScheduledTask()
    {
        RunPowerShell(BuildRemoveScheduledTaskScript(), throwOnError: false);
    }

    private static string BuildQueryScheduledTaskScript()
    {
        var taskName = EscapePowerShellLiteral(TaskName);
        return $$"""
$task = Get-ScheduledTask -TaskName '{{taskName}}' -ErrorAction SilentlyContinue
if ($null -eq $task -or $task.State -eq 'Disabled') {
    exit 1
}
""";
    }

    private static string BuildRemoveScheduledTaskScript()
    {
        var taskName = EscapePowerShellLiteral(TaskName);
        return $$"""
$task = Get-ScheduledTask -TaskName '{{taskName}}' -ErrorAction SilentlyContinue
if ($null -ne $task) {
    Unregister-ScheduledTask -TaskName '{{taskName}}' -Confirm:$false
}
""";
    }

    private static ProcessResult RunPowerShell(string script, bool throwOnError = true)
    {
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return RunProcess(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
            throwOnError);
    }

    private static ProcessResult RunProcess(string fileName, string arguments, bool throwOnError = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
                           ?? throw new InvalidOperationException($"Не удалось запустить {fileName}.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var combined = string.Join(
            Environment.NewLine,
            new[] { output.Trim(), error.Trim() }.Where(text => !string.IsNullOrWhiteSpace(text)));

        if (throwOnError && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(combined)
                    ? $"Не удалось выполнить команду {fileName}."
                    : combined);
        }

        return new ProcessResult(process.ExitCode, combined);
    }

    private static string EscapePowerShellLiteral(string value)
    {
        return value.Replace("'", "''");
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
