using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ZapretManager.Services;

public sealed class ProgramRemovalService
{
    private const string StartupTaskName = "ZapretManager";
    private const string LegacyRunValueName = "ZapretManager";

    public async Task LaunchPreparedRemovalAsync(
        string currentExecutablePath,
        int currentProcessId,
        string serviceName,
        IReadOnlyCollection<string>? extraCleanupPaths = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentExecutablePath) || !File.Exists(currentExecutablePath))
        {
            throw new InvalidOperationException("Не удалось определить текущий файл ZapretManager для удаления.");
        }

        var targetExecutablePath = Path.GetFullPath(currentExecutablePath);
        var targetDirectory = Path.GetDirectoryName(targetExecutablePath);
        if (string.IsNullOrWhiteSpace(targetDirectory) ||
            string.Equals(targetDirectory, Path.GetPathRoot(targetDirectory), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Не удалось определить папку ZapretManager для удаления.");
        }

        var localDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZapretManager");
        var programDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ZapretManager");
        var scriptPath = Path.Combine(Path.GetTempPath(), $"remove-zapretmanager-{Guid.NewGuid():N}.ps1");
        var extraCleanupPathsFile = Path.Combine(Path.GetTempPath(), $"remove-zapretmanager-paths-{Guid.NewGuid():N}.txt");

        await File.WriteAllTextAsync(
            scriptPath,
            BuildRemovalScriptContent(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);

        var normalizedExtraPaths = (extraCleanupPaths ?? [])
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await File.WriteAllLinesAsync(
            extraCleanupPathsFile,
            normalizedExtraPaths,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);

        var requiresElevation =
            !CanWriteToDirectory(Path.GetDirectoryName(targetDirectory)!) ||
            !CanWriteToDirectory(Path.GetDirectoryName(programDataDirectory)!);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments =
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File {QuoteArgument(scriptPath)} " +
                $"-CurrentProcessId {currentProcessId} " +
                $"-TargetExecutablePath {QuoteArgument(targetExecutablePath)} " +
                $"-TargetDirectory {QuoteArgument(targetDirectory)} " +
                $"-LocalDataDirectory {QuoteArgument(localDataDirectory)} " +
                $"-ProgramDataDirectory {QuoteArgument(programDataDirectory)} " +
                $"-ServiceName {QuoteArgument(serviceName)} " +
                $"-StartupTaskName {QuoteArgument(StartupTaskName)} " +
                $"-LegacyRunValueName {QuoteArgument(LegacyRunValueName)} " +
                $"-ExtraCleanupPathsFile {QuoteArgument(extraCleanupPathsFile)}",
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = true
        };

        if (requiresElevation)
        {
            startInfo.Verb = "runas";
        }

        try
        {
            _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить удаление программы.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Удаление программы отменено.", ex);
        }
    }

    private static bool CanWriteToDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            var testPath = Path.Combine(directoryPath, $".zapretmanager-delete-test-{Guid.NewGuid():N}.tmp");
            using (File.Create(testPath, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string QuoteArgument(string argument)
    {
        return $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string BuildRemovalScriptContent()
    {
        return """
param(
    [int]$CurrentProcessId,
    [string]$TargetExecutablePath,
    [string]$TargetDirectory,
    [string]$LocalDataDirectory,
    [string]$ProgramDataDirectory,
    [string]$ServiceName = '',
    [string]$StartupTaskName = '',
    [string]$LegacyRunValueName = '',
    [string]$ExtraCleanupPathsFile = ''
)

$ErrorActionPreference = 'Stop'

function Invoke-Safe {
    param([scriptblock]$Action)

    try {
        & $Action
    }
    catch {
    }
}

function Wait-ForProcessExit {
    param([int]$ProcessId)

    for ($i = 0; $i -lt 160; $i++) {
        try {
            Get-Process -Id $ProcessId -ErrorAction Stop | Out-Null
            Start-Sleep -Milliseconds 500
        }
        catch {
            return
        }
    }
}

function Remove-PathWithRetry {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return
    }

    for ($i = 0; $i -lt 60; $i++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (Test-Path -LiteralPath $Path) {
        throw "Не удалось удалить: $Path"
    }
}

function Try-RemovePathWithRetry {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return
    }

    try {
        Remove-PathWithRetry -Path $Path
    }
    catch {
    }
}

function Remove-FileWithRetry {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return
    }

    for ($i = 0; $i -lt 60; $i++) {
        try {
            Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
            return
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (Test-Path -LiteralPath $Path) {
        throw "Не удалось удалить файл: $Path"
    }
}

function Try-RemoveDirectoryIfEmpty {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return
    }

    try {
        if ((Get-ChildItem -LiteralPath $Path -Force | Measure-Object).Count -eq 0) {
            Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
        }
    }
    catch {
    }
}

function Try-RemoveExtraPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    try {
        $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    }
    catch {
        return
    }

    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        return
    }

    try {
        $item = Get-Item -LiteralPath $resolvedPath -Force -ErrorAction Stop
        if ($item.PSIsContainer) {
            Try-RemovePathWithRetry -Path $resolvedPath
        }
        else {
            Remove-FileWithRetry -Path $resolvedPath
        }
    }
    catch {
    }
}

Wait-ForProcessExit -ProcessId $CurrentProcessId
Start-Sleep -Milliseconds 350

if (-not [string]::IsNullOrWhiteSpace($ServiceName)) {
    Invoke-Safe { sc.exe stop $ServiceName | Out-Null }

    for ($i = 0; $i -lt 40; $i++) {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -eq $service -or $service.Status -eq 'Stopped') {
            break
        }

        Start-Sleep -Milliseconds 500
    }

    Invoke-Safe { sc.exe delete $ServiceName | Out-Null }

    for ($i = 0; $i -lt 40; $i++) {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -eq $service) {
            break
        }

        Start-Sleep -Milliseconds 500
    }
}

if (-not [string]::IsNullOrWhiteSpace($StartupTaskName)) {
    Invoke-Safe {
        $task = Get-ScheduledTask -TaskName $StartupTaskName -ErrorAction SilentlyContinue
        if ($null -ne $task) {
            Unregister-ScheduledTask -TaskName $StartupTaskName -Confirm:$false | Out-Null
        }
    }
}

if (-not [string]::IsNullOrWhiteSpace($LegacyRunValueName)) {
    Invoke-Safe {
        Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $LegacyRunValueName -Force -ErrorAction SilentlyContinue
    }
}

$remainingProcesses = Get-CimInstance Win32_Process -Filter "name = 'ZapretManager.exe'" -ErrorAction SilentlyContinue |
    Where-Object {
        $_.ProcessId -ne $PID -and
        $_.ProcessId -ne $CurrentProcessId -and
        -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
        [string]::Equals($_.ExecutablePath, $TargetExecutablePath, [System.StringComparison]::OrdinalIgnoreCase)
    }

foreach ($processInfo in $remainingProcesses) {
    Invoke-Safe { Stop-Process -Id $processInfo.ProcessId -Force -ErrorAction Stop }
}

Remove-FileWithRetry -Path $TargetExecutablePath
Remove-FileWithRetry -Path ($TargetExecutablePath + '.old')
Remove-FileWithRetry -Path ($TargetExecutablePath + '.bak')
Try-RemoveDirectoryIfEmpty -Path $TargetDirectory
if (-not [string]::IsNullOrWhiteSpace($ExtraCleanupPathsFile) -and (Test-Path -LiteralPath $ExtraCleanupPathsFile)) {
    try {
        $extraPaths = Get-Content -LiteralPath $ExtraCleanupPathsFile -ErrorAction Stop
        foreach ($extraPath in $extraPaths) {
            Try-RemoveExtraPath -Path $extraPath
        }
    }
    catch {
    }

    Invoke-Safe { Remove-Item -LiteralPath $ExtraCleanupPathsFile -Force -ErrorAction SilentlyContinue }
}
Try-RemovePathWithRetry -Path $ProgramDataDirectory
Try-RemovePathWithRetry -Path $LocalDataDirectory

$cleanupCommand = 'ping 127.0.0.1 -n 2 > nul & del /f /q "' + $PSCommandPath + '" >nul 2>nul'
Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $cleanupCommand -WindowStyle Hidden
""";
    }
}
