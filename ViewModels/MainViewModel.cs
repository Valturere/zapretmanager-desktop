using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ZapretManager;
using ZapretManager.Infrastructure;
using ZapretManager.Models;
using ZapretManager.Services;

namespace ZapretManager.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string AuthorGitHubProfileUrl = "https://github.com/Valturere";
    private const string AuthorGitHubRepositoryUrl = "https://github.com/Valturere/zapretmanager-desktop";
    private const string FlowsealProfileUrl = "https://github.com/Flowseal";
    private const string FlowsealRepositoryUrl = "https://github.com/Flowseal/zapret-discord-youtube";
    private const string ZapretProfileUrl = "https://github.com/bol-van";
    private const string ZapretRepositoryUrl = "https://github.com/bol-van/zapret";
    private const string IssuesUrl = "https://github.com/Valturere/zapretmanager-desktop/issues";

    private readonly ZapretDiscoveryService _discoveryService = new();
    private readonly ZapretProcessService _processService = new();
    private readonly WindowsServiceManager _serviceManager = new();
    private readonly ConnectivityTestService _connectivityTestService = new();
    private readonly UpdateService _updateService = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly GameModeService _gameModeService = new();
    private readonly IpSetService _ipSetService = new();
    private readonly RepositoryMaintenanceService _repositoryMaintenanceService = new();
    private readonly DiscordCacheService _discordCacheService = new();
    private readonly StartupRegistrationService _startupRegistrationService = new();
    private readonly PreservedUserDataService _preservedUserDataService = new();
    private readonly DnsService _dnsService = new();
    private readonly DnsDiagnosisService _dnsDiagnosisService = new();
    private readonly DiagnosticsService _diagnosticsService = new();
    private readonly ManagerUpdateService _managerUpdateService = new();
    private readonly AppSettings _settings;
    private readonly Dictionary<string, ConfigProbeResult> _probeResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TargetGroupDefinition> _builtInTargetGroups = CreateBuiltInTargetGroups()
        .ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
    private readonly string _managerVersion = GetManagerVersion();

    private ZapretInstallation? _installation;
    private bool _isRebuildingRows;
    private ConfigProfile? _selectedConfig;
    private ConfigTableRow? _selectedConfigRow;
    private GameModeOption? _selectedGameMode;
    private CancellationTokenSource? _probeCancellation;
    private string _installationPath = "Папка zapret ещё не выбрана";
    private string _windowTitle = "ZapretManager";
    private string _versionText = "Версия: неизвестно";
    private string _runtimeStatus = "Проверяем состояние winws.exe...";
    private string _serviceStatus = "Проверяем состояние службы...";
    private string _updateStatus = "Обновления: не проверялись";
    private string _gameModeStatus = "Игровой режим: не определён";
    private string _lastActionText = "Действие: ожидание";
    private string _busyEtaText = string.Empty;
    private string _manualTarget = string.Empty;
    private string _recommendedConfigText = "Рекомендуемый конфиг: появится после проверки";
    private string _selectedSummaryText = "Подробности появятся после проверки.";
    private string _defaultTargetsHint = "Пресеты: YouTube, Discord, Cloudflare или все цели из targets.txt.";
    private string _selectedTargetsDisplayText = "Все цели из targets.txt";
    private string _inlineNotificationText = string.Empty;
    private bool _isInlineNotificationVisible;
    private bool _isInlineNotificationError;
    private bool _isBusy;
    private bool _isProbeRunning;
    private bool _hasUpdate;
    private bool _hasPreviousVersion;
    private bool _autoCheckUpdatesEnabled;
    private bool _startWithWindowsEnabled;
    private bool _closeToTrayEnabled;
    private bool _minimizeToTrayEnabled;
    private bool _useLightThemeEnabled;
    private string? _updateDownloadUrl;
    private string? _updateLatestVersion;
    private string _probeButtonText = "Проверить";
    private double _busyProgressValue;
    private bool _busyProgressIsIndeterminate = true;
    private CancellationTokenSource? _notificationCancellation;
    private bool _restoreSuspendedServiceAfterStandalone;
    private string? _suspendedServiceRestoreRootPath;
    private bool _isRestoringSuspendedService;
    private CancellationTokenSource? _suspendedServiceRestoreWatchCancellation;
    private readonly Stopwatch _probeStopwatch = new();
    private readonly DispatcherTimer _probeProgressTimer;
    private int _probeProgressTotalProfiles;
    private int _probeProgressCompletedProfiles;
    private bool _probeProgressIncludesRestoreStep;
    private bool _probeCurrentProfileActive;
    private DateTime _probeCurrentProfileStartedAtUtc;
    private TimeSpan _probeInitialProfileEstimate;
    private TimeSpan? _probeLastDisplayedRemaining;
    private DateTime _probeLastEtaUpdatedAtUtc;
    private ProbeDetailsWindow? _probeDetailsWindow;
    private DiagnosticsWindow? _diagnosticsWindow;
    private readonly HashSet<Window> _openAuxiliaryWindows = [];
    private const string ManagerMovedMessage = "Файл ZapretManager был перемещён после запуска. Закройте программу через трей или Диспетчер задач и откройте её заново из новой папки.";

    public MainViewModel()
    {
        _probeProgressTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _probeProgressTimer.Tick += (_, _) => RefreshProbeProgressDisplay();

        _settings = _settingsService.Load();
        _settings.CustomTargetGroups ??= [];
        _settings.SelectedTargetGroupKeys ??= [];
        _settings.HiddenConfigPaths ??= [];
        if (!_settings.StartWithWindowsPreferenceInitialized)
        {
            _settings.StartWithWindowsEnabled = false;
            _settings.StartWithWindowsPreferenceInitialized = true;
            _settingsService.Save(_settings);
        }

        _settings.PreferredDnsProfileKey = string.IsNullOrWhiteSpace(_settings.PreferredDnsProfileKey)
            ? DnsService.SystemProfileKey
            : _settings.PreferredDnsProfileKey;
        _autoCheckUpdatesEnabled = _settings.AutoCheckUpdatesEnabled;
        _closeToTrayEnabled = _settings.CloseToTrayEnabled;
        _minimizeToTrayEnabled = _settings.MinimizeToTrayEnabled;
        _useLightThemeEnabled = _settings.UseLightTheme;
        SynchronizeStartupRegistration();

        Configs = new ObservableCollection<ConfigProfile>();
        ConfigRows = new ObservableCollection<ConfigTableRow>();
        GameModeOptions = new ObservableCollection<GameModeOption>
        {
            new("disabled", "Выключен"),
            new("all", "TCP + UDP (обычно)"),
            new("tcp", "Только TCP"),
            new("udp", "Только UDP")
        };

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && !IsProbeRunning);
        QuickSearchCommand = new AsyncRelayCommand(QuickSearchAsync, () => !IsBusy && !IsProbeRunning);
        BrowseCommand = new AsyncRelayCommand(BrowseFolderAsync, () => !IsBusy && !IsProbeRunning);
        DownloadZapretCommand = new AsyncRelayCommand(DownloadZapretAsync, () => !IsBusy && !IsProbeRunning);
        DeleteZapretCommand = new AsyncRelayCommand(DeleteZapretAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => _installation is not null && !IsBusy);
        OpenTargetsFileCommand = new AsyncRelayCommand(OpenTargetsEditorAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenIncludedDomainsEditorCommand = new AsyncRelayCommand(OpenIncludedDomainsEditorAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenExcludedDomainsEditorCommand = new AsyncRelayCommand(OpenExcludedDomainsEditorAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenHostsEditorCommand = new AsyncRelayCommand(OpenHostsEditorAsync, () => !IsBusy && !IsProbeRunning);
        OpenUserSubnetsEditorCommand = new AsyncRelayCommand(OpenUserSubnetsEditorAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenHiddenConfigsCommand = new AsyncRelayCommand(OpenHiddenConfigsWindowAsync, () => _installation is not null && !IsBusy);
        OpenIpSetModeCommand = new AsyncRelayCommand(OpenIpSetModeWindowAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenDnsSettingsCommand = new AsyncRelayCommand(OpenDnsSettingsAsync, () => !IsBusy && !IsProbeRunning);
        OpenDiagnosticsCommand = new AsyncRelayCommand(OpenDiagnosticsAsync, () => !IsBusy && !IsProbeRunning);
        OpenAboutCommand = new AsyncRelayCommand(OpenAboutWindowAsync, () => !IsBusy);
        UpdateIpSetListCommand = new AsyncRelayCommand(UpdateIpSetListAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        UpdateHostsFileCommand = new AsyncRelayCommand(UpdateHostsFileAsync, () => !IsBusy && !IsProbeRunning);
        ClearDiscordCacheCommand = new AsyncRelayCommand(ClearDiscordCacheAsync, () => !IsBusy && !IsProbeRunning);
        StartCommand = new AsyncRelayCommand(StartSelectedAsync, () => _installation is not null && SelectedConfig is not null && !IsBusy);
        StopCommand = new AsyncRelayCommand(StopAsync, CanStopCurrentRuntime);
        HideSelectedConfigCommand = new RelayCommand(HideSelectedConfig, () => _installation is not null && SelectedConfig is not null && !IsBusy && !IsProbeRunning);
        AutoInstallCommand = new AsyncRelayCommand(RunAutomaticInstallAsync, () => !IsBusy && !IsProbeRunning);
        InstallServiceCommand = new AsyncRelayCommand(InstallServiceAsync, () => _installation is not null && SelectedConfig is not null && !IsBusy);
        RemoveServiceCommand = new AsyncRelayCommand(RemoveServiceAsync, () => _installation is not null && !IsBusy);
        RunTestsCommand = new RelayCommand(ToggleProbe, () => _installation is not null && ConfigRows.Count > 0 && (!IsBusy || IsProbeRunning));
        CheckUpdatesCommand = new AsyncRelayCommand(() => CheckUpdatesAsync(true), () => _installation is not null && !IsBusy);
        ApplyUpdateCommand = new AsyncRelayCommand(() => ApplyUpdateAsync(), () => _installation is not null && HasUpdate && !IsBusy);
        RestorePreviousVersionCommand = new AsyncRelayCommand(RestorePreviousVersionAsync, () => _installation is not null && HasPreviousVersion && !IsBusy && !IsProbeRunning);
        CheckManagerUpdateCommand = new AsyncRelayCommand(CheckManagerUpdateAsync, () => !IsBusy && !IsProbeRunning);
        ApplyGameModeCommand = new AsyncRelayCommand(ApplyGameModeAsync, () => _installation is not null && SelectedGameMode is not null && !IsBusy);
        UseDefaultTargetsCommand = new RelayCommand(UseDefaultTargets, () => !IsBusy);
        UseYouTubePresetCommand = new RelayCommand(() => UseTargetGroupPreset("youtube"), () => !IsBusy);
        UseDiscordPresetCommand = new RelayCommand(() => UseTargetGroupPreset("discord"), () => !IsBusy);
        UseCloudflarePresetCommand = new RelayCommand(() => UseTargetGroupPreset("cloudflare"), () => !IsBusy);

        RefreshSelectedTargetsDisplay();
    }

    public ObservableCollection<ConfigProfile> Configs { get; }
    public ObservableCollection<ConfigTableRow> ConfigRows { get; }
    public ObservableCollection<GameModeOption> GameModeOptions { get; }

    public AsyncRelayCommand BrowseCommand { get; }
    public AsyncRelayCommand DownloadZapretCommand { get; }
    public AsyncRelayCommand DeleteZapretCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public AsyncRelayCommand OpenTargetsFileCommand { get; }
    public AsyncRelayCommand OpenIncludedDomainsEditorCommand { get; }
    public AsyncRelayCommand OpenExcludedDomainsEditorCommand { get; }
    public AsyncRelayCommand OpenHostsEditorCommand { get; }
    public AsyncRelayCommand OpenUserSubnetsEditorCommand { get; }
    public AsyncRelayCommand OpenHiddenConfigsCommand { get; }
    public AsyncRelayCommand OpenIpSetModeCommand { get; }
    public AsyncRelayCommand OpenDnsSettingsCommand { get; }
    public AsyncRelayCommand OpenDiagnosticsCommand { get; }
    public AsyncRelayCommand OpenAboutCommand { get; }
    public AsyncRelayCommand UpdateIpSetListCommand { get; }
    public AsyncRelayCommand UpdateHostsFileCommand { get; }
    public AsyncRelayCommand ClearDiscordCacheCommand { get; }
    public RelayCommand UseDefaultTargetsCommand { get; }
    public RelayCommand UseYouTubePresetCommand { get; }
    public RelayCommand UseDiscordPresetCommand { get; }
    public RelayCommand UseCloudflarePresetCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand QuickSearchCommand { get; }
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public RelayCommand HideSelectedConfigCommand { get; }
    public AsyncRelayCommand AutoInstallCommand { get; }
    public AsyncRelayCommand InstallServiceCommand { get; }
    public AsyncRelayCommand RemoveServiceCommand { get; }
    public RelayCommand RunTestsCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }
    public AsyncRelayCommand ApplyUpdateCommand { get; }
    public AsyncRelayCommand RestorePreviousVersionCommand { get; }
    public AsyncRelayCommand CheckManagerUpdateCommand { get; }
    public AsyncRelayCommand ApplyGameModeCommand { get; }

    public string InstallationPath
    {
        get => _installationPath;
        set => SetProperty(ref _installationPath, value);
    }

    public string WindowTitle
    {
        get => _windowTitle;
        set => SetProperty(ref _windowTitle, value);
    }

    public string VersionText
    {
        get => _versionText;
        set => SetProperty(ref _versionText, value);
    }

    public string ManagerVersionLabel => $"v{_managerVersion}";

    public string RuntimeStatus
    {
        get => _runtimeStatus;
        set => SetProperty(ref _runtimeStatus, value);
    }

    public string ServiceStatus
    {
        get => _serviceStatus;
        set => SetProperty(ref _serviceStatus, value);
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        set => SetProperty(ref _updateStatus, value);
    }

    public string GameModeStatus
    {
        get => _gameModeStatus;
        set => SetProperty(ref _gameModeStatus, value);
    }

    public string LastActionText
    {
        get => _lastActionText;
        set
        {
            if (SetProperty(ref _lastActionText, value))
            {
                OnPropertyChanged(nameof(BusyActionText));
            }
        }
    }

    public string BusyActionText => TrimActionPrefix(_lastActionText);

    public string BusyEtaText
    {
        get => _busyEtaText;
        set => SetProperty(ref _busyEtaText, value);
    }

    public double BusyProgressValue
    {
        get => _busyProgressValue;
        set => SetProperty(ref _busyProgressValue, value);
    }

    public bool BusyProgressIsIndeterminate
    {
        get => _busyProgressIsIndeterminate;
        set => SetProperty(ref _busyProgressIsIndeterminate, value);
    }

    public string ManualTarget
    {
        get => _manualTarget;
        set => SetProperty(ref _manualTarget, value);
    }

    public string RecommendedConfigText
    {
        get => _recommendedConfigText;
        set => SetProperty(ref _recommendedConfigText, value);
    }

    public string SelectedSummaryText
    {
        get => _selectedSummaryText;
        set => SetProperty(ref _selectedSummaryText, value);
    }

    public string DefaultTargetsHint
    {
        get => _defaultTargetsHint;
        set => SetProperty(ref _defaultTargetsHint, value);
    }

    public string SelectedTargetsDisplayText
    {
        get => _selectedTargetsDisplayText;
        set => SetProperty(ref _selectedTargetsDisplayText, value);
    }

    public string InlineNotificationText
    {
        get => _inlineNotificationText;
        set => SetProperty(ref _inlineNotificationText, value);
    }

    public bool IsInlineNotificationVisible
    {
        get => _isInlineNotificationVisible;
        set => SetProperty(ref _isInlineNotificationVisible, value);
    }

    public bool IsInlineNotificationError
    {
        get => _isInlineNotificationError;
        set => SetProperty(ref _isInlineNotificationError, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool HasUpdate
    {
        get => _hasUpdate;
        set
        {
            if (SetProperty(ref _hasUpdate, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool HasPreviousVersion
    {
        get => _hasPreviousVersion;
        set
        {
            if (SetProperty(ref _hasPreviousVersion, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsProbeRunning
    {
        get => _isProbeRunning;
        set
        {
            if (SetProperty(ref _isProbeRunning, value))
            {
                ProbeButtonText = value ? "Отмена" : "Проверить";
                RaiseCommandStates();
            }
        }
    }

    public bool AutoCheckUpdatesEnabled
    {
        get => _autoCheckUpdatesEnabled;
        set
        {
            if (SetProperty(ref _autoCheckUpdatesEnabled, value))
            {
                _settings.AutoCheckUpdatesEnabled = value;
                _settingsService.Save(_settings);
                LastActionText = value
                    ? "Действие: автопроверка обновлений включена"
                    : "Действие: автопроверка обновлений выключена";
            }
        }
    }

    public bool StartWithWindowsEnabled
    {
        get => _startWithWindowsEnabled;
        set
        {
            if (SetProperty(ref _startWithWindowsEnabled, value))
            {
                var previousValue = !value;
                try
                {
                    _settings.StartWithWindowsEnabled = value;
                    _settingsService.Save(_settings);
                    _startupRegistrationService.SetEnabled(value);
                    LastActionText = value
                        ? "Действие: автозапуск с Windows включен"
                        : "Действие: автозапуск с Windows выключен";
                }
                catch (Exception ex)
                {
                    _startWithWindowsEnabled = previousValue;
                    OnPropertyChanged(nameof(StartWithWindowsEnabled));
                    _settings.StartWithWindowsEnabled = previousValue;
                    _settingsService.Save(_settings);
                    LastActionText = value
                        ? "Действие: не удалось включить автозапуск с Windows"
                        : "Действие: не удалось выключить автозапуск с Windows";
                    var displayMessage = _startupRegistrationService.BuildSetEnabledErrorMessage(ex, value);
                    DialogService.ShowError(displayMessage, "Zapret Manager");
                }
            }
        }
    }

    public bool CloseToTrayEnabled
    {
        get => _closeToTrayEnabled;
        set
        {
            if (SetProperty(ref _closeToTrayEnabled, value))
            {
                _settings.CloseToTrayEnabled = value;
                _settingsService.Save(_settings);
                LastActionText = value
                    ? "Действие: закрытие будет сворачивать программу в трей"
                    : "Действие: закрытие будет завершать программу";
            }
        }
    }

    public bool MinimizeToTrayEnabled
    {
        get => _minimizeToTrayEnabled;
        set
        {
            if (SetProperty(ref _minimizeToTrayEnabled, value))
            {
                _settings.MinimizeToTrayEnabled = value;
                _settingsService.Save(_settings);
                LastActionText = value
                    ? "Действие: сворачивание уводит программу в трей"
                    : "Действие: сворачивание оставляет программу в панели задач";
            }
        }
    }

    public bool UseLightThemeEnabled
    {
        get => _useLightThemeEnabled;
        set
        {
            if (SetProperty(ref _useLightThemeEnabled, value))
            {
                _settings.UseLightTheme = value;
                _settingsService.Save(_settings);
                LastActionText = value
                    ? "Действие: включена светлая тема"
                    : "Действие: включена тёмная тема";
            }
        }
    }

    public string ProbeButtonText
    {
        get => _probeButtonText;
        set => SetProperty(ref _probeButtonText, value);
    }

    public ConfigProfile? SelectedConfig
    {
        get => _selectedConfig;
        set
        {
            if (SetProperty(ref _selectedConfig, value))
            {
                if (value is not null)
                {
                    _settings.LastSelectedConfigPath = value.FilePath;
                    _settingsService.Save(_settings);
                }

                RaiseCommandStates();
            }
        }
    }

    public ConfigTableRow? SelectedConfigRow
    {
        get => _selectedConfigRow;
        set
        {
            if (SetProperty(ref _selectedConfigRow, value) && value is not null)
            {
                SelectedConfig = Configs.FirstOrDefault(item =>
                    string.Equals(item.FilePath, value.FilePath, StringComparison.OrdinalIgnoreCase));

                if (SelectedConfig is not null && !_isRebuildingRows && !IsProbeRunning)
                {
                    LastActionText = $"Действие: выбран конфиг {SelectedConfig.Name}";
                }

                SelectedSummaryText = BuildSelectedSummaryText(value.Summary, value.HasProbeResult);
            }
        }
    }

    public GameModeOption? SelectedGameMode
    {
        get => _selectedGameMode;
        set
        {
            if (SetProperty(ref _selectedGameMode, value))
            {
                if (value is not null && !string.Equals(value.Value, "disabled", StringComparison.OrdinalIgnoreCase))
                {
                    _settings.PreferredGameModeValue = value.Value;
                    _settingsService.Save(_settings);
                }

                RaiseCommandStates();
            }
        }
    }

    public async Task InitializeAsync()
    {
        await RefreshAsync();

        if (AutoCheckUpdatesEnabled)
        {
            await CheckUpdatesAsync(false);

            if (HasUpdate && _installation is not null)
            {
                var shouldUpdate = DialogService.ConfirmCustom(
                    $"Найдена новая версия: {(_updateLatestVersion ?? "доступно обновление")}{Environment.NewLine}Текущая версия: {_installation.Version}",
                    "Zapret Manager",
                    primaryButtonText: "Обновить",
                    secondaryButtonText: "Закрыть");

                if (shouldUpdate)
                {
                    await ApplyUpdateAsync();
                }
            }
        }

        await RefreshStatusAsync();
    }

    public async Task RefreshStatusAsync()
    {
        try
        {
            _installation ??= ResolveInstallation();
            if (_installation is null)
            {
                return;
            }

            try
            {
                await RefreshLiveStatusCoreAsync();
            }
            catch
            {
            }

            try
            {
                if (!IsBusy)
                {
                    await RestoreSuspendedServiceIfNeededAsync();
                }
            }
            catch
            {
            }
        }
        catch
        {
        }
    }

    private static string BuildSelectedSummaryText(string? summary, bool hasProbeResult)
    {
        if (!hasProbeResult || string.IsNullOrWhiteSpace(summary))
        {
            return "Подробности появятся после проверки.";
        }

        return string.Equals(summary, "✓", StringComparison.Ordinal)
            ? "✓ Все цели доступны."
            : summary;
    }

    private async Task RefreshAsync()
    {
        if (IsProbeRunning)
        {
            LastActionText = "Действие: сначала остановите проверку конфигов";
            return;
        }

        await RunBusyAsync(RefreshCoreAsync);
    }

    private async Task QuickSearchAsync()
    {
        if (IsProbeRunning)
        {
            LastActionText = "Действие: сначала остановите проверку конфигов";
            return;
        }

        ZapretInstallation? installation = null;
        await RunBusyAsync(async () =>
        {
            RuntimeStatus = "Ищем zapret в типичных местах...";
            LastActionText = "Действие: быстрый поиск папки zapret";

            installation = await Task.Run(() => _discoveryService.DiscoverQuick(Directory.GetCurrentDirectory()));
            if (installation is null)
            {
                RuntimeStatus = "winws.exe не запущен";
                LastActionText = "Действие: быстрый поиск ничего не нашёл";
                return;
            }
        });

        if (installation is null)
        {
            DialogService.ShowInfo("Быстрый поиск не нашёл рабочую папку zapret. Можно выбрать её вручную.");
            return;
        }

        await SelectInstallationAsync(installation, $"Действие: быстрый поиск нашёл {installation.RootPath}");
    }

    private async Task RefreshCoreAsync()
    {
        _installation = ResolveInstallation();

        if (_installation is null)
        {
            InstallationPath = "Папка zapret не найдена. Выберите её вручную.";
            VersionText = "Версия: неизвестно";
            RuntimeStatus = "Сборка не подключена";
            ServiceStatus = "Служба: недоступно";
            UpdateStatus = "Обновления: выберите папку zapret";
            GameModeStatus = "Игровой режим: недоступен";
            LastActionText = "Действие: подключите рабочую папку zapret";
            RecommendedConfigText = "Рекомендуемый конфиг: сначала подключите сборку";
            SelectedSummaryText = "Подробности появятся после проверки.";
            DefaultTargetsHint = "Пресеты появятся после выбора рабочей папки zapret.";
            Configs.Clear();
            ConfigRows.Clear();
            _probeResults.Clear();
            HasPreviousVersion = false;
            SelectedConfig = null;
            SelectedConfigRow = null;
            SelectedGameMode = GameModeOptions.FirstOrDefault();
            RaiseCommandStates();
            return;
        }

        _updateService.DisableInternalCheckUpdatesForSiblingInstallations(_installation.RootPath);
        HasPreviousVersion = _updateService.HasStoredPreviousVersion(_installation.RootPath);

        InstallationPath = _installation.RootPath;
        VersionText = $"Версия: {_installation.Version}";

        _probeResults.Clear();
        Configs.Clear();
        foreach (var profile in _installation.Profiles)
        {
            Configs.Add(profile);
        }

        PruneHiddenConfigPaths();

        var visibleProfiles = GetVisibleProfiles().ToList();
        if (!visibleProfiles.Any())
        {
            SelectedConfig = null;
        }
        else if (SelectedConfig is null || !visibleProfiles.Any(item => string.Equals(item.FilePath, SelectedConfig.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedConfig = visibleProfiles.FirstOrDefault(item =>
                string.Equals(item.FilePath, _settings.LastSelectedConfigPath, StringComparison.OrdinalIgnoreCase))
                ?? visibleProfiles.FirstOrDefault();
        }

        RebuildConfigRows();
        DefaultTargetsHint = BuildTargetsHint();
        RecommendedConfigText = "Рекомендуемый конфиг: появится после проверки";
        SelectedSummaryText = "Подробности появятся после проверки.";
        var gameModeValue = _gameModeService.GetModeValue(_installation);
        SelectedGameMode = GameModeOptions.FirstOrDefault(item => item.Value == gameModeValue) ?? GameModeOptions.First();
        await RefreshLiveStatusCoreAsync();
    }

    private Task RefreshLiveStatusCoreAsync()
    {
        if (_installation is null)
        {
            return Task.CompletedTask;
        }

        VersionText = $"Версия: {_installation.Version}";

        var runningCount = _processService.GetRunningProcessCount(_installation);
        RuntimeStatus = runningCount > 0
            ? $"Запущен winws.exe: {runningCount}"
            : "winws.exe не запущен";

        var service = _serviceManager.GetStatus();
        RememberRunningServiceProfile(service);
        ServiceStatus = service.IsInstalled
            ? service.IsRunning
                ? $"Служба запущена: {service.ProfileName ?? "профиль не определён"}"
                : ShouldRestoreSuspendedService(_installation)
                    ? $"Служба временно остановлена: {service.ProfileName ?? "профиль не определён"}"
                    : $"Служба установлена: {service.ProfileName ?? "профиль не определён"}"
            : "Служба не установлена";

        GameModeStatus = $"Игровой режим: {_gameModeService.GetModeLabel(_installation)}";
        RaiseCommandStates();
        return Task.CompletedTask;
    }

    private ZapretInstallation? ResolveInstallation()
    {
        if (!string.IsNullOrWhiteSpace(_settings.LastInstallationPath))
        {
            var saved = _discoveryService.TryLoad(_settings.LastInstallationPath);
            if (saved is not null)
            {
                return saved;
            }
        }

        return _discoveryService.Discover(Directory.GetCurrentDirectory());
    }

    private async Task BrowseFolderAsync()
    {
        if (IsProbeRunning)
        {
            LastActionText = "Действие: нельзя менять папку во время проверки";
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку со сборкой zapret"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var installation = _discoveryService.TryLoad(dialog.FolderName);
        if (installation is null)
        {
            DialogService.ShowInfo("В выбранной папке не найдено service.bat и bin\\winws.exe.");
            return;
        }

        await SelectInstallationAsync(installation, $"Действие: выбрана папка {installation.RootPath}");
    }

    private async Task DownloadZapretAsync()
    {
        if (IsProbeRunning)
        {
            LastActionText = "Действие: нельзя скачивать сборку во время проверки";
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку, куда установить zapret"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        UpdateOperationResult? installResult = null;
        await RunBusyAsync(async () =>
        {
            UpdateStatus = "Обновления: скачиваем свежую сборку...";
            LastActionText = "Действие: скачиваем zapret";
            installResult = await _updateService.InstallFreshAsync(dialog.FolderName);
            UpdateStatus = $"Обновления: установлена версия {installResult.InstalledVersion}";
        });

        if (installResult is null)
        {
            return;
        }

        var installation = _discoveryService.TryLoad(installResult.ActiveRootPath)
                          ?? throw new InvalidOperationException("Свежая сборка скачалась, но её не удалось подключить.");

        await SelectInstallationAsync(installation, $"Действие: zapret установлен в {installResult.ActiveRootPath}");
        DialogService.ShowInfo(
            $"Свежая сборка zapret установлена в:{Environment.NewLine}{installResult.ActiveRootPath}",
            "Zapret Manager");
    }

    private async Task DeleteZapretAsync()
    {
        if (_installation is null)
        {
            return;
        }

        var installationToDelete = _installation;
        var deleteChoice = DialogService.ChooseDeleteZapretMode(installationToDelete.RootPath);
        if (deleteChoice == DeleteZapretChoice.Cancel)
        {
            return;
        }

        var preserveUserLists = deleteChoice == DeleteZapretChoice.DeleteKeepLists;

        await RunBusyAsync(async () =>
        {
            if (IsProbeRunning)
            {
                RuntimeStatus = "Останавливаем проверку перед удалением...";
                CancelProbe();
                await WaitForProbeStopAsync(TimeSpan.FromSeconds(40));
            }

            LastActionText = "Действие: подготавливаем удаление zapret";
            RuntimeStatus = "Останавливаем winws и службу перед удалением...";
            ClearSuspendedServiceRestore();
            var cleanupWarnings = new List<string>();

            RuntimeStatus = "Удаляем службу перед удалением папки...";
            await _serviceManager.RemoveAsync();
            await WaitForServiceRemovalAsync(TimeSpan.FromSeconds(20));

            await _processService.StopAsync(null);
            await _processService.StopAsync(installationToDelete);
            await WaitForProcessExitAsync(installationToDelete, TimeSpan.FromSeconds(20));
            await WaitForDriverReleaseAsync(installationToDelete.RootPath, TimeSpan.FromSeconds(60));

            var preservedFilesCount = 0;
            if (preserveUserLists)
            {
                RuntimeStatus = "Сохраняем пользовательские списки перед удалением...";
                preservedFilesCount = _preservedUserDataService.BackupFromInstallation(installationToDelete);
            }
            else
            {
                RuntimeStatus = "Очищаем сохранённые списки и hosts перед удалением...";
                _preservedUserDataService.Clear();

                try
                {
                    var removedHostsEntries = await _repositoryMaintenanceService.RemoveManagedHostsBlockAsync();
                    if (removedHostsEntries > 0)
                    {
                        cleanupWarnings.Add($"hosts очищен: удалено {removedHostsEntries} записей");
                    }
                }
                catch (Exception ex)
                {
                    cleanupWarnings.Add($"hosts не удалось очистить: {ex.Message}");
                }
            }

            var pendingDeletePath = await DeleteInstallationDirectoryAsync(installationToDelete.RootPath);
            var pendingPreviousDeletePath = await _updateService.DeleteStoredPreviousVersionAsync(installationToDelete.RootPath);

            if (string.Equals(_settings.LastInstallationPath, installationToDelete.RootPath, StringComparison.OrdinalIgnoreCase))
            {
                _settings.LastInstallationPath = null;
            }

            if (PathStartsWith(_settings.LastSelectedConfigPath, installationToDelete.RootPath))
            {
                _settings.LastSelectedConfigPath = null;
            }

            if (PathStartsWith(_settings.LastStartedConfigPath, installationToDelete.RootPath))
            {
                _settings.LastStartedConfigPath = null;
            }

            if (PathStartsWith(_settings.LastInstalledServiceConfigPath, installationToDelete.RootPath))
            {
                RememberInstalledServiceProfile(profile: null, saveImmediately: false);
            }

            _settings.HiddenConfigPaths.RemoveAll(path => PathStartsWith(path, installationToDelete.RootPath));

            _settingsService.Save(_settings);
            _installation = null;
            await RefreshCoreAsync();
            LastActionText = pendingDeletePath is null && pendingPreviousDeletePath is null
                ? "Действие: сборки zapret удалены"
                : "Действие: папки перенесены на удаление и дочищаются в фоне";
            UpdateStatus = "Обновления: выберите папку zapret";

            if (pendingDeletePath is null && pendingPreviousDeletePath is null)
            {
                if (preserveUserLists)
                {
                    ShowInlineNotification(preservedFilesCount > 0
                        ? $"Сборки zapret удалены. Сохранены пользовательские файлы: {preservedFilesCount}. Они вернутся в следующую сборку автоматически."
                        : "Сборки zapret удалены. Пользовательских файлов для сохранения не было.");
                }
                else if (cleanupWarnings.Count > 0)
                {
                    ShowInlineNotification(
                        "Сборки zapret удалены. " + string.Join("; ", cleanupWarnings),
                        isError: true,
                        durationMs: 6500);
                }
                else
                {
                    ShowInlineNotification("Текущая и сохранённая предыдущая сборки zapret удалены вместе со списками и блоком hosts менеджера.");
                }
            }
            else
            {
                var pendingParts = new List<string>();
                if (pendingDeletePath is not null)
                {
                    pendingParts.Add(pendingDeletePath);
                }

                if (pendingPreviousDeletePath is not null)
                {
                    pendingParts.Add(pendingPreviousDeletePath);
                }

                ShowInlineNotification(
                    $"Сборки отключены. Остатки папок ещё дочищаются: {string.Join("; ", pendingParts)}",
                    isError: true,
                    durationMs: 6500);
            }
        });
    }

    private void OpenFolder()
    {
        if (_installation is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{_installation.RootPath}\"",
            UseShellExecute = true
        });
    }

    private async Task OpenTargetsEditorAsync()
    {
        if (_installation is null)
        {
            return;
        }

        await OpenListEditorAsync(
            title: "Цели для проверки (targets.txt)",
            description: "Редактируйте targets.txt прямо в окне. Активные строки должны иметь формат KeyName = \"https://site.com\" или KeyName = \"PING:1.1.1.1\". Строки с # считаются комментариями.",
            placeholder: "# Пример:\nDiscordMain = \"https://discord.com\"\nCloudflareDNS1111 = \"PING:1.1.1.1\"",
            relativePath: Path.Combine("utils", "targets.txt"),
            successText: "Файл targets.txt сохранён.",
            validationMode: ListEditorWindow.ListEditorValidationMode.TargetFile,
            defaultContent: GetDefaultTargetsTemplate());
    }

    private async Task OpenIncludedDomainsEditorAsync()
    {
        await OpenListEditorAsync(
            title: "Включённые домены",
            description: "Добавьте домены построчно. Эти адреса будут принудительно добавлены в пользовательский список zapret.",
            placeholder: "discord.com\nyoutube.com\ninstagram.com",
            relativePath: Path.Combine("lists", "list-general-user.txt"),
            successText: "Список включённых доменов сохранён.",
            validationMode: ListEditorWindow.ListEditorValidationMode.DomainList,
            allowDomainsImport: true);
    }

    private async Task OpenExcludedDomainsEditorAsync()
    {
        await OpenListEditorAsync(
            title: "Исключённые домены",
            description: "Добавьте домены построчно. Эти адреса будут исключены из пользовательской обработки zapret.",
            placeholder: "example.com\ncdn.example.com",
            relativePath: Path.Combine("lists", "list-exclude-user.txt"),
            successText: "Список исключённых доменов сохранён.",
            validationMode: ListEditorWindow.ListEditorValidationMode.DomainList);
    }

    private async Task OpenHostsEditorAsync()
    {
        var hostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers",
            "etc",
            "hosts");

        await OpenListEditorAsync(
            title: "Системный hosts",
            description: "Редактируйте системный файл hosts прямо из программы. Каждая строка может содержать IP, домен и комментарий. Файл влияет на разрешение имён во всей системе.",
            placeholder: "127.0.0.1 localhost\r\n# Пример:\r\n127.0.0.1 example.com",
            relativePath: hostsPath,
            successText: "Системный hosts сохранён.");
    }

    private async Task OpenUserSubnetsEditorAsync()
    {
        await OpenListEditorAsync(
            title: "Пользовательские подсети (IPSet)",
            description: "Добавьте IP-адреса или подсети в формате CIDR построчно. Эти значения сохраняются отдельно и не пропадают при обновлении сборки.",
            placeholder: "192.168.1.0/24\n10.0.0.0/8\n34.149.116.40/32",
            relativePath: Path.Combine("lists", "ipset-exclude-user.txt"),
            successText: "Пользовательские подсети сохранены.",
            validationMode: ListEditorWindow.ListEditorValidationMode.SubnetList);
    }

    private async Task OpenListEditorAsync(
        string title,
        string description,
        string placeholder,
        string relativePath,
        string successText,
        ListEditorWindow.ListEditorValidationMode validationMode = ListEditorWindow.ListEditorValidationMode.None,
        string? defaultContent = null,
        bool allowDomainsImport = false)
    {
        var filePath = Path.IsPathRooted(relativePath)
            ? relativePath
            : _installation is not null
                ? Path.Combine(_installation.RootPath, relativePath)
                : string.Empty;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            ShowInlineNotification("Сначала подключите рабочую папку zapret.", isError: true);
            return;
        }

        var editor = new ListEditorWindow(title, description, placeholder, filePath, UseLightThemeEnabled, validationMode, defaultContent, allowDomainsImport);
        if (System.Windows.Application.Current?.MainWindow is Window owner && owner.IsLoaded)
        {
            editor.Owner = owner;
        }

        if (await ShowAuxiliaryWindowAsync(editor, () => editor.WasSaved))
        {
            LastActionText = $"Действие: {successText.TrimEnd('.')}";
            await RestartActiveRuntimeAfterListChangeAsync(successText);
        }
    }

    private async Task OpenHiddenConfigsWindowAsync()
    {
        if (_installation is null)
        {
            ShowInlineNotification("Сначала подключите рабочую папку zapret.", isError: true);
            return;
        }

        var hiddenItems = Configs
            .Where(profile => IsHiddenConfig(profile.FilePath))
            .Select(profile => new HiddenConfigItem
            {
                ConfigName = profile.Name,
                FileName = profile.FileName,
                FilePath = profile.FilePath
            })
            .OrderBy(item => item.ConfigName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hiddenItems.Count == 0)
        {
            ShowInlineNotification("Скрытых конфигов сейчас нет.");
            return;
        }

        var window = new HiddenConfigsWindow(hiddenItems, UseLightThemeEnabled);
        if (System.Windows.Application.Current?.MainWindow is Window owner && owner.IsLoaded)
        {
            window.Owner = owner;
        }

        if (!await ShowAuxiliaryWindowAsync(window, () => window.SelectedAction != HiddenConfigsAction.None))
        {
            return;
        }

        switch (window.SelectedAction)
        {
            case HiddenConfigsAction.RestoreSelected when !string.IsNullOrWhiteSpace(window.SelectedFilePath):
                _settings.HiddenConfigPaths.RemoveAll(path =>
                    string.Equals(path, window.SelectedFilePath, StringComparison.OrdinalIgnoreCase));
                _settingsService.Save(_settings);
                RebuildConfigRows();
                LastActionText = "Действие: скрытый конфиг возвращён";
                ShowInlineNotification("Скрытый конфиг возвращён в основной список.");
                break;

            case HiddenConfigsAction.RestoreAll:
                _settings.HiddenConfigPaths.Clear();
                _settingsService.Save(_settings);
                RebuildConfigRows();
                LastActionText = "Действие: все скрытые конфиги возвращены";
                ShowInlineNotification("Все скрытые конфиги возвращены.");
                break;
        }
    }

    private async Task OpenAboutWindowAsync()
    {
        var window = new AboutWindow(
            _managerVersion,
            AuthorGitHubProfileUrl,
            AuthorGitHubRepositoryUrl,
            FlowsealProfileUrl,
            FlowsealRepositoryUrl,
            ZapretProfileUrl,
            ZapretRepositoryUrl,
            IssuesUrl,
            UseLightThemeEnabled);
        if (System.Windows.Application.Current?.MainWindow is Window owner && owner.IsLoaded)
        {
            window.Owner = owner;
        }

        await ShowAuxiliaryWindowAsync(window, () => false);
    }

    private async Task CheckManagerUpdateAsync()
    {
        EnsureManagerExecutableAvailable();

        ManagerUpdateInfo? updateInfo = null;
        await RunBusyAsync(async () =>
        {
            LastActionText = "Действие: проверяем обновление программы";
            updateInfo = await _managerUpdateService.GetUpdateInfoAsync(_managerVersion);
        });

        if (updateInfo is null)
        {
            return;
        }

        if (!updateInfo.IsUpdateAvailable)
        {
            DialogService.ShowInfo("Новых обновлений программы не найдено.", "Zapret Manager");
            LastActionText = $"Действие: обновлений программы нет, версия {_managerVersion} актуальна";
            return;
        }

        var mayRequireElevation = DirectoryMayRequireElevation();
        var promptMessage =
            $"Найдена новая версия программы: {updateInfo.LatestVersion}.{Environment.NewLine}" +
            $"Текущая версия: {_managerVersion}.{Environment.NewLine}{Environment.NewLine}" +
            "Скачать и установить её сейчас? Программа будет полностью закрыта и откроется снова после обновления." +
            (mayRequireElevation
                ? $"{Environment.NewLine}{Environment.NewLine}Если программа лежит в защищённой папке, Windows может попросить подтверждение прав администратора."
                : string.Empty);

        var shouldInstall = DialogService.ConfirmCustom(
            promptMessage,
            "Zapret Manager",
            primaryButtonText: "Обновить",
            secondaryButtonText: "Позже");

        if (!shouldInstall)
        {
            LastActionText = "Действие: обновление программы отложено";
            return;
        }

        var currentProcessPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentProcessPath))
        {
            throw new InvalidOperationException("Не удалось определить путь к ZapretManager.");
        }

        currentProcessPath = Path.GetFullPath(currentProcessPath);
        var serviceStatus = _serviceManager.GetStatus();
        var restartHostedServiceDuringUpdate =
            serviceStatus.IsInstalled &&
            _serviceManager.UsesExecutable(currentProcessPath);
        var reinstallHostedServiceAfterUpdate =
            restartHostedServiceDuringUpdate &&
            !string.IsNullOrWhiteSpace(serviceStatus.InstallationRootPath) &&
            !string.IsNullOrWhiteSpace(serviceStatus.ProfileToken);
        string? downloadedPath = null;
        await RunBusyAsync(async () =>
        {
            LastActionText = $"Действие: скачиваем обновление программы {updateInfo.LatestVersion}";
            downloadedPath = await _managerUpdateService.DownloadUpdateAsync(
                updateInfo.DownloadUrl!,
                updateInfo.AssetFileName,
                updateInfo.LatestVersion);
        });

        if (string.IsNullOrWhiteSpace(downloadedPath))
        {
            return;
        }

        try
        {
            LastActionText = $"Действие: подготавливаем установку обновления программы {updateInfo.LatestVersion}";
            await _managerUpdateService.LaunchPreparedUpdateAsync(
                downloadedPath,
                currentProcessPath,
                Process.GetCurrentProcess().Id,
                WindowsServiceManager.ServiceName,
                restartHostedServiceDuringUpdate,
                reinstallHostedServiceAfterUpdate,
                serviceStatus.InstallationRootPath,
                serviceStatus.ProfileToken);
        }
        catch (OperationCanceledException)
        {
            LastActionText = "Действие: обновление программы отменено";
            ShowInlineNotification("Обновление программы отменено.", isError: true);
            return;
        }
        catch (Exception ex)
        {
            var displayMessage = DialogService.GetDisplayMessage(ex);
            LastActionText = $"Действие: ошибка - {displayMessage}";
            DialogService.ShowError(displayMessage, "Zapret Manager");
            return;
        }

        LastActionText = $"Действие: перезапускаем ZapretManager для обновления до {updateInfo.LatestVersion}";
        if (System.Windows.Application.Current?.MainWindow is MainWindow mainWindow)
        {
            mainWindow.ShutdownForManagerUpdate();
            return;
        }

        System.Windows.Application.Current?.Shutdown();
    }

    private async Task OpenIpSetModeWindowAsync()
    {
        if (_installation is null)
        {
            ShowInlineNotification("Сначала подключите рабочую папку zapret.", isError: true);
            return;
        }

        var dialog = new IpSetModeWindow(_ipSetService.GetModeValue(_installation), UseLightThemeEnabled);
        if (System.Windows.Application.Current?.MainWindow is Window owner && owner.IsLoaded)
        {
            dialog.Owner = owner;
        }

        if (!await ShowAuxiliaryWindowAsync(dialog, () => dialog.WasApplied))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var installation = _installation!;
            var serviceStatus = _serviceManager.GetStatus();
            var shouldRestoreService = serviceStatus.IsRunning || ShouldRestoreSuspendedService(installation);
            var restoreProfile = !serviceStatus.IsRunning && _processService.GetRunningProcessCount(installation) > 0
                ? ResolveRestoreProfile()
                : null;

            _ipSetService.SetMode(installation, dialog.SelectedModeValue);
            LastActionText = $"Действие: режим IPSet изменён на {GetIpSetModeLabel(dialog.SelectedModeValue)}";

            if (serviceStatus.IsRunning)
            {
                RuntimeStatus = "Перезапускаем службу для применения IPSet...";
                await _serviceManager.StopAsync();
                await Task.Delay(1000);
                await _serviceManager.StartAsync();
                ClearSuspendedServiceRestore();
            }
            else if (shouldRestoreService)
            {
                RuntimeStatus = "Возвращаем службу для применения IPSet...";
                await _serviceManager.StartAsync();
                ClearSuspendedServiceRestore();
            }
            else if (restoreProfile is not null)
            {
                RuntimeStatus = $"Перезапускаем {restoreProfile.Name} для применения IPSet...";
                await _processService.StopAsync(installation);
                await Task.Delay(700);
                await _processService.StartAsync(installation, restoreProfile);
                _settings.LastStartedConfigPath = restoreProfile.FilePath;
                _settingsService.Save(_settings);
            }

            await RefreshLiveStatusCoreAsync();
            ShowInlineNotification($"IPSet: {GetIpSetModeLabel(dialog.SelectedModeValue)}");
        });
    }

    public async Task OpenDnsSettingsAsync()
    {
        try
        {
            EnsureManagerExecutableAvailable();
            LastActionText = "Действие: открываем настройки DNS";
            var selectedProfileKey = GetCurrentDnsProfileKey();

            var dialog = new DnsSettingsWindow(
                _dnsService.GetPresetDefinitions(_settings.CustomDnsPrimary, _settings.CustomDnsSecondary, _settings.CustomDnsDohTemplate),
                selectedProfileKey,
                _settings.CustomDnsPrimary,
                _settings.CustomDnsSecondary,
                _settings.DnsOverHttpsEnabled,
                _settings.CustomDnsDohTemplate,
                UseLightThemeEnabled);

            if (!await ShowAuxiliaryWindowAsync(dialog, () => dialog.WasApplied))
            {
                return;
            }

            _settings.CustomDnsPrimary = dialog.CustomPrimary;
            _settings.CustomDnsSecondary = dialog.CustomSecondary;
            _settings.CustomDnsDohTemplate = dialog.CustomDohTemplate;
            _settings.DnsOverHttpsEnabled = dialog.UseDnsOverHttps;
            _settingsService.Save(_settings);

            await ApplyDnsProfileAsync(
                dialog.SelectedProfileKey,
                dialog.CustomPrimary,
                dialog.CustomSecondary,
                dialog.UseDnsOverHttps,
                dialog.CustomDohTemplate);
        }
        catch (Exception ex)
        {
            LastActionText = "Действие: не удалось открыть настройки DNS";
            DialogService.ShowError(ex, "Zapret Manager");
        }
    }

    public void OpenProbeDetails(ConfigTableRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (!_probeResults.TryGetValue(row.ConfigName, out var probeResult) || probeResult.TargetResults.Count == 0)
        {
            ShowInlineNotification("У этого конфига пока нет подробных результатов проверки.", isError: true);
            return;
        }

        LastActionText = $"Действие: подробности проверки {row.ConfigName}";

        if (_probeDetailsWindow is not null)
        {
            try
            {
                _probeDetailsWindow.Close();
            }
            catch
            {
            }

            _probeDetailsWindow = null;
        }

        var window = new ProbeDetailsWindow(row.ConfigName, probeResult, UseLightThemeEnabled);
        ConfigureAuxiliaryWindow(window);

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_probeDetailsWindow, window))
            {
                _probeDetailsWindow = null;
            }

            _openAuxiliaryWindows.Remove(window);
            RestoreOwnerAfterAuxiliaryClose(window);
        };

        _probeDetailsWindow = window;
        _openAuxiliaryWindows.Add(window);
        window.Show();
        window.Activate();
    }

    public async Task OpenDiagnosticsAsync()
    {
        try
        {
            LastActionText = "Действие: запускаем диагностику системы";
            if (_diagnosticsWindow is not null)
            {
                try
                {
                    _diagnosticsWindow.Close();
                }
                catch
                {
                }

                _diagnosticsWindow = null;
            }

            var window = new DiagnosticsWindow(_diagnosticsService, _installation, UseLightThemeEnabled);
            ConfigureAuxiliaryWindow(window);

            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_diagnosticsWindow, window))
                {
                    _diagnosticsWindow = null;
                }

                _openAuxiliaryWindows.Remove(window);
                RestoreOwnerAfterAuxiliaryClose(window);
            };

            _diagnosticsWindow = window;
            _openAuxiliaryWindows.Add(window);
            await window.ShowAndRunAsync();
        }
        catch (Exception ex)
        {
            _diagnosticsWindow = null;
            ShowInlineNotification($"Не удалось открыть диагностику системы: {ex.Message}", isError: true);
        }
    }

    public bool CloseAuxiliaryWindows()
    {
        var closedAny = false;

        foreach (var window in _openAuxiliaryWindows.ToArray())
        {
            TryCloseAuxiliaryWindow(window);
            closedAny = true;
        }

        return closedAny;
    }

    private async Task<bool> ShowAuxiliaryWindowAsync(Window window, Func<bool> acceptedPredicate)
    {
        var completionSource = new TaskCompletionSource<bool>();

        void ClosedHandler(object? sender, EventArgs args)
        {
            window.Closed -= ClosedHandler;
            _openAuxiliaryWindows.Remove(window);
            RestoreOwnerAfterAuxiliaryClose(window);
            completionSource.TrySetResult(acceptedPredicate());
        }

        window.Closed += ClosedHandler;
        _openAuxiliaryWindows.Add(window);
        ConfigureAuxiliaryWindow(window);
        window.Show();
        window.Activate();

        return await completionSource.Task;
    }

    private static void TryCloseAuxiliaryWindow(Window window)
    {
        try
        {
            window.Close();
        }
        catch
        {
        }
    }

    private void RestoreOwnerAfterAuxiliaryClose(Window closedWindow)
    {
        if (_openAuxiliaryWindows.Any(window => window.IsVisible))
        {
            return;
        }

        if (closedWindow.Owner is not Window owner || !owner.IsLoaded || !owner.IsVisible)
        {
            return;
        }

        owner.Dispatcher.BeginInvoke(() =>
        {
            if (_openAuxiliaryWindows.Any(window => window.IsVisible) || !owner.IsLoaded)
            {
                return;
            }

            if (owner.WindowState == WindowState.Minimized)
            {
                owner.WindowState = WindowState.Normal;
            }

            if (!owner.IsVisible)
            {
                return;
            }

            owner.Activate();
            owner.Focus();
        }, DispatcherPriority.ApplicationIdle);
    }

    private static void ConfigureAuxiliaryWindow(Window window)
    {
        if (System.Windows.Application.Current?.MainWindow is Window owner && owner.IsLoaded && owner.IsVisible)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return;
        }

        window.Owner = null;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    public async Task ApplyDnsProfileFromTrayAsync(string profileKey)
    {
        if (IsBusy || IsProbeRunning)
        {
            return;
        }

        if (string.Equals(profileKey, DnsService.CustomProfileKey, StringComparison.OrdinalIgnoreCase) &&
            !HasCustomDnsConfigured())
        {
            ShowInlineNotification("Сначала задайте пользовательский DNS в окне настроек.", isError: true);
            return;
        }

        var customPrimary = string.Equals(profileKey, DnsService.CustomProfileKey, StringComparison.OrdinalIgnoreCase)
            ? _settings.CustomDnsPrimary
            : null;
        var customSecondary = string.Equals(profileKey, DnsService.CustomProfileKey, StringComparison.OrdinalIgnoreCase)
            ? _settings.CustomDnsSecondary
            : null;

        await ApplyDnsProfileAsync(
            profileKey,
            customPrimary,
            customSecondary,
            _settings.DnsOverHttpsEnabled,
            _settings.CustomDnsDohTemplate);
    }

    private async Task<bool> ApplyDnsProfileAsync(
        string profileKey,
        string? customPrimary,
        string? customSecondary,
        bool useDnsOverHttps,
        string? customDohTemplate)
    {
        var rememberedDohPreference = useDnsOverHttps;
        if (string.Equals(profileKey, DnsService.SystemProfileKey, StringComparison.OrdinalIgnoreCase))
        {
            useDnsOverHttps = false;
        }

        var resultFilePath = Path.Combine(Path.GetTempPath(), $"zapretmanager-dns-{Guid.NewGuid():N}.txt");
        var profileLabel = _dnsService.GetProfileLabel(profileKey, customPrimary, customSecondary, customDohTemplate, useDnsOverHttps);

        try
        {
            await RunBusyAsync(async () =>
            {
                RuntimeStatus = $"Меняем DNS: {profileLabel}...";
                LastActionText = $"Действие: применяем DNS {profileLabel}";
                await RunElevatedManagerTaskAsync(
                    BuildDnsArguments(profileKey, customPrimary, customSecondary, useDnsOverHttps, customDohTemplate, resultFilePath),
                    resultFilePath);
            }, rethrowExceptions: true);

            _settings.PreferredDnsProfileKey = profileKey;
            _settings.DnsOverHttpsEnabled = rememberedDohPreference;
            _settingsService.Save(_settings);
            LastActionText = $"Действие: DNS изменён на {profileLabel}";
            ShowInlineNotification($"DNS изменён: {profileLabel}");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            LastActionText = "Действие: изменение DNS отменено пользователем";
            ShowInlineNotification("Изменение DNS отменено.", isError: true);
            return false;
        }
        catch (Exception ex)
        {
            var shortError = _dnsService.BuildApplyProfileShortError(ex.Message, useDnsOverHttps);
            var displayMessage = _dnsService.BuildApplyProfileErrorMessage(ex.Message, profileLabel, useDnsOverHttps);
            LastActionText = $"Действие: DNS не изменён - {shortError}";
            DialogService.ShowError(displayMessage, "Zapret Manager");
            return false;
        }
    }

    private async Task UpdateIpSetListAsync()
    {
        if (_installation is null)
        {
            ShowInlineNotification("Сначала подключите рабочую папку zapret.", isError: true);
            return;
        }

        var installation = _installation;
        var currentMode = _ipSetService.GetModeValue(installation);
        var shouldApplyImmediately = string.Equals(currentMode, "loaded", StringComparison.OrdinalIgnoreCase);

        await RunBusyAsync(async () =>
        {
            RuntimeStatus = "Скачиваем актуальный список IPSet...";
            LastActionText = "Действие: обновляем список IPSet";

            var updateResult = await _repositoryMaintenanceService.UpdateIpSetListAsync(installation);
            await RestartBypassForIpSetChangesAsync(installation, shouldApplyImmediately);
            var activeEntryCount = _repositoryMaintenanceService.GetActiveIpSetEntryCount(installation);
            await RefreshLiveStatusCoreAsync();

            LastActionText = updateResult.AppliedToActiveList
                ? "Действие: список IPSet обновлён и применён"
                : "Действие: список IPSet обновлён";

            ShowInlineNotification(updateResult.AppliedToActiveList
                ? $"IPSet обновлён и применён сразу. Записей: {activeEntryCount}."
                : $"IPSet обновлён. Записей: {updateResult.EntryCount}. Он включится при режиме 'по списку'.");
        });
    }

    private async Task UpdateHostsFileAsync()
    {
        var shouldApplyHosts = DialogService.ConfirmCustom(
            "Менеджер удалит старые записи zapret для тех же доменов и запишет актуальный блок hosts из репозитория. Остальные строки в системном hosts останутся как есть.\n\nПродолжить?",
            "Zapret Manager",
            primaryButtonText: "Обновить hosts",
            secondaryButtonText: "Отмена");
        if (!shouldApplyHosts)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            RuntimeStatus = "Обновляем системный hosts...";
            LastActionText = "Действие: обновляем hosts для zapret";

            var updateResult = await _repositoryMaintenanceService.UpdateHostsFileAsync();
            var managedHostsEntryCount = _repositoryMaintenanceService.GetManagedHostsEntryCount();
            await RefreshLiveStatusCoreAsync();
            LastActionText = "Действие: hosts для zapret обновлён";
            ShowInlineNotification(
                $"Hosts обновлён: заменено {updateResult.ReplacedEntryCount} старых строк, добавлено {managedHostsEntryCount} актуальных.");
        });
    }

    private void HideSelectedConfig()
    {
        if (_installation is null || SelectedConfig is null)
        {
            return;
        }

        var serviceStatus = _serviceManager.GetStatus();
        if (serviceStatus.IsInstalled &&
            string.Equals(serviceStatus.ProfileName, SelectedConfig.Name, StringComparison.OrdinalIgnoreCase))
        {
            ShowInlineNotification("Сначала удалите службу для этого конфига, а потом скрывайте его.", isError: true);
            return;
        }

        if (_processService.GetRunningProcessCount(_installation) > 0 &&
            string.Equals(_settings.LastStartedConfigPath, SelectedConfig.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            ShowInlineNotification("Сначала остановите активный профиль, а потом скрывайте его.", isError: true);
            return;
        }

        if (!_settings.SkipHideConfigConfirmation)
        {
            var confirmation = DialogService.ConfirmWithRemember(
                $"Скрыть конфиг {SelectedConfig.Name}? Он исчезнет из основного списка и не будет участвовать в проверке.",
                "Zapret Manager",
                rememberText: "Больше не спрашивать");
            if (!confirmation.Accepted)
            {
                return;
            }

            if (confirmation.RememberChoice)
            {
                _settings.SkipHideConfigConfirmation = true;
                _settingsService.Save(_settings);
            }
        }

        if (!_settings.HiddenConfigPaths.Any(path =>
                string.Equals(path, SelectedConfig.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            _settings.HiddenConfigPaths.Add(SelectedConfig.FilePath);
        }

        _settingsService.Save(_settings);
        _probeResults.Remove(SelectedConfig.Name);
        LastActionText = $"Действие: конфиг {SelectedConfig.Name} скрыт";
        RebuildConfigRows();
        ShowInlineNotification($"Конфиг {SelectedConfig.Name} скрыт.");
    }

    private async Task ClearDiscordCacheAsync()
    {
        await RunBusyAsync(async () =>
        {
            LastActionText = "Действие: очищаем кэш Discord";
            var cleared = _discordCacheService.Clear();
            RuntimeStatus = "Кэш Discord очищен";
            LastActionText = cleared > 0
                ? $"Действие: очищен кэш Discord ({cleared} папок)"
                : "Действие: папки кэша Discord не найдены";
            ShowInlineNotification(cleared > 0 ? $"Кэш Discord очищен: {cleared} папок." : "Папки кэша Discord не найдены.");
            await Task.CompletedTask;
        });
    }

    private async Task RestartActiveRuntimeAfterListChangeAsync(string successText)
    {
        if (_installation is null)
        {
            ShowInlineNotification(successText);
            return;
        }

        var installation = _installation;
        var serviceStatus = _serviceManager.GetStatus();
        var shouldRestoreService = serviceStatus.IsRunning || ShouldRestoreSuspendedService(installation);
        var restoreProfile = !serviceStatus.IsRunning && _processService.GetRunningProcessCount(installation) > 0
            ? ResolveRestoreProfile()
            : null;

        if (!shouldRestoreService && restoreProfile is null)
        {
            ShowInlineNotification(successText);
            return;
        }

        await RunBusyAsync(async () =>
        {
            if (serviceStatus.IsRunning)
            {
                RuntimeStatus = "Перезапускаем службу для применения списков...";
                await _serviceManager.StopAsync();
                await Task.Delay(1000);
                await _serviceManager.StartAsync();
                ClearSuspendedServiceRestore();
                LastActionText = $"Действие: {successText.TrimEnd('.')}, служба перезапущена";
            }
            else if (shouldRestoreService)
            {
                RuntimeStatus = "Возвращаем службу для применения списков...";
                await _serviceManager.StartAsync();
                ClearSuspendedServiceRestore();
                LastActionText = $"Действие: {successText.TrimEnd('.')}, служба снова запущена";
            }
            else if (restoreProfile is not null)
            {
                RuntimeStatus = $"Перезапускаем {restoreProfile.Name} для применения списков...";
                await _processService.StopAsync(installation);
                await Task.Delay(700);
                await _processService.StartAsync(installation, restoreProfile);
                _settings.LastStartedConfigPath = restoreProfile.FilePath;
                _settingsService.Save(_settings);
                LastActionText = $"Действие: {successText.TrimEnd('.')}, профиль {restoreProfile.Name} перезапущен";
            }

            await RefreshLiveStatusCoreAsync();
            ShowInlineNotification(successText);
        });
    }

    private static string GetIpSetModeLabel(string modeValue)
    {
        return modeValue switch
        {
            "loaded" => "по списку",
            "none" => "выключен",
            _ => "все IP"
        };
    }

    private async Task RestartBypassForIpSetChangesAsync(ZapretInstallation installation, bool shouldApplyImmediately)
    {
        if (!shouldApplyImmediately)
        {
            return;
        }

        var serviceStatus = _serviceManager.GetStatus();
        var shouldRestoreService = serviceStatus.IsRunning || ShouldRestoreSuspendedService(installation);
        var restoreProfile = !serviceStatus.IsRunning && _processService.GetRunningProcessCount(installation) > 0
            ? ResolveRestoreProfile()
            : null;

        if (serviceStatus.IsRunning)
        {
            RuntimeStatus = "Перезапускаем службу для нового списка IPSet...";
            await _serviceManager.StopAsync();
            await Task.Delay(1000);
            await _serviceManager.StartAsync();
            ClearSuspendedServiceRestore();
            return;
        }

        if (shouldRestoreService)
        {
            RuntimeStatus = "Возвращаем службу для нового списка IPSet...";
            await _serviceManager.StartAsync();
            ClearSuspendedServiceRestore();
            return;
        }

        if (restoreProfile is not null)
        {
            RuntimeStatus = $"Перезапускаем {restoreProfile.Name} для нового списка IPSet...";
            await _processService.StopAsync(installation);
            await Task.Delay(700);
            await _processService.StartAsync(installation, restoreProfile);
            _settings.LastStartedConfigPath = restoreProfile.FilePath;
            _settingsService.Save(_settings);
        }
    }

    private async Task StartSelectedAsync()
    {
        if (_installation is null || SelectedConfig is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            LastActionText = $"Действие: перезапускаем {SelectedConfig.Name}";
            var serviceStatus = _serviceManager.GetStatus();
            var shouldReturnServiceAfterStandalone = serviceStatus.IsRunning || ShouldRestoreSuspendedService(_installation);
            var standaloneStarted = false;

            try
            {
                if (serviceStatus.IsRunning)
                {
                    MarkSuspendedServiceForRestore(_installation);
                    RuntimeStatus = "Останавливаем службу zapret перед запуском профиля...";
                    await _serviceManager.StopAsync();
                    await Task.Delay(1000);
                }

                RuntimeStatus = "Останавливаем текущий winws.exe...";
                await StopRelatedInstallationsAsync(_installation.RootPath, waitForDrivers: false);
                await Task.Delay(500);

                RuntimeStatus = $"Запуск профиля {SelectedConfig.Name}...";
                await _processService.StartAsync(_installation, SelectedConfig);
                standaloneStarted = true;
                _settings.LastStartedConfigPath = SelectedConfig.FilePath;
                _settingsService.Save(_settings);

                if (shouldReturnServiceAfterStandalone && ShouldRestoreSuspendedService(_installation))
                {
                    StartSuspendedServiceRestoreWatch(_installation);
                    ShowInlineNotification("Служба временно остановлена. После закрытия ручного профиля она вернётся автоматически.");
                }

                await Task.Delay(1500);
                await RefreshLiveStatusCoreAsync();
                LastActionText = _processService.GetRunningProcessCount(_installation) > 0
                    ? $"Действие: профиль {SelectedConfig.Name} запущен"
                    : $"Действие: профиль {SelectedConfig.Name} не запустился";
            }
            catch
            {
                if (shouldReturnServiceAfterStandalone && !standaloneStarted && ShouldRestoreSuspendedService(_installation))
                {
                    await RestoreSuspendedServiceIfNeededAsync();
                }

                throw;
            }
        });
    }

    private async Task StopAsync()
    {
        if (_installation is null)
        {
            return;
        }

        if (!CanStopCurrentRuntime())
        {
            LastActionText = "Действие: вручную запущенный профиль не найден";
            ShowInlineNotification("Кнопка «Остановить» завершает только вручную запущенный профиль. Установленная служба этой кнопкой не трогается.");
            await RefreshLiveStatusCoreAsync();
            return;
        }

        await RunBusyAsync(async () =>
        {
            RuntimeStatus = ShouldRestoreSuspendedService(_installation)
                ? "Останавливаем вручную запущенный профиль..."
                : "Остановка winws.exe...";
            await StopRelatedInstallationsAsync(_installation.RootPath, waitForDrivers: false);
            await Task.Delay(500);
            var restoredService = await RestoreSuspendedServiceIfNeededAsync();
            await RefreshLiveStatusCoreAsync();
            LastActionText = restoredService
                ? "Действие: winws.exe остановлен, служба снова запущена"
                : "Действие: winws.exe остановлен";
        });
    }

    private async Task InstallServiceAsync()
    {
        if (_installation is null || SelectedConfig is null)
        {
            return;
        }

        await InstallServiceAsync(SelectedConfig);
    }

    private async Task InstallServiceAsync(ConfigProfile profile)
    {
        if (_installation is null)
        {
            return;
        }

        var installation = _installation;

        await RunBusyAsync(async () =>
        {
            LastActionText = $"Действие: устанавливаем службу для {profile.Name}";
            RuntimeStatus = "Останавливаем активные процессы zapret перед установкой службы...";
            await StopRelatedInstallationsAsync(installation.RootPath, waitForDrivers: true);

            await _serviceManager.InstallAsync(installation, profile);
            _settings.LastStartedConfigPath = profile.FilePath;
            RememberInstalledServiceProfile(profile, saveImmediately: false);
            _settingsService.Save(_settings);
            await Task.Delay(1200);
            await RefreshLiveStatusCoreAsync();

            var actualServiceStatus = _serviceManager.GetStatus();
            if (!actualServiceStatus.IsRunning)
            {
                throw new InvalidOperationException("Служба создалась, но не осталась запущенной. Проверьте, не блокирует ли запуск старая версия или драйвер.");
            }

            ClearSuspendedServiceRestore();
            LastActionText = $"Действие: служба установлена и запущена для {profile.Name}";
        });
        ShowInlineNotification($"Служба установлена и запущена: {profile.Name}");
    }

    private async Task RemoveServiceAsync()
    {
        if (_installation is null)
        {
            return;
        }

        var installedServiceStatus = _serviceManager.GetStatus();
        if (installedServiceStatus.IsInstalled && !string.IsNullOrWhiteSpace(installedServiceStatus.ProfileName))
        {
            var installedProfile = FindProfileByIdentity(_installation, installedServiceStatus.ProfileName, profileFileName: null);
            if (installedProfile is not null)
            {
                RememberInstalledServiceProfile(installedProfile);
            }
        }

        await RunBusyAsync(async () =>
        {
            LastActionText = "Действие: удаляем службу";
            await _serviceManager.RemoveAsync();
            ClearSuspendedServiceRestore();
            await RefreshLiveStatusCoreAsync();
            ServiceStatus = "Служба не установлена";
            LastActionText = "Действие: служба удалена";
        });
        ShowInlineNotification("Служба удалена.");
    }

    private async Task ApplyGameModeAsync()
    {
        if (_installation is null || SelectedGameMode is null)
        {
            return;
        }

        var installation = _installation;
        var selectedGameMode = SelectedGameMode;
        var serviceStatus = _serviceManager.GetStatus();
        var shouldRestoreService = serviceStatus.IsRunning || ShouldRestoreSuspendedService(installation);
        var restoreProfile = !serviceStatus.IsRunning && _processService.GetRunningProcessCount(installation) > 0
            ? ResolveRestoreProfile()
            : null;
        var notificationText = $"Игровой режим: {selectedGameMode.Label}. Настройка сохранена.";

        await RunBusyAsync(async () =>
        {
            _gameModeService.SetMode(installation, selectedGameMode.Value);
            GameModeStatus = $"Игровой режим: {selectedGameMode.Label}";

            if (serviceStatus.IsRunning)
            {
                RuntimeStatus = "Перезапускаем службу для применения игрового режима...";
                await _serviceManager.StopAsync();
                await Task.Delay(1000);
                await _serviceManager.StartAsync();
                ClearSuspendedServiceRestore();
                LastActionText = $"Действие: игровой режим {selectedGameMode.Label}, служба перезапущена";
                notificationText = $"Игровой режим: {selectedGameMode.Label}. Служба перезапущена.";
            }
            else if (shouldRestoreService)
            {
                RuntimeStatus = "Возвращаем службу для применения игрового режима...";
                await _serviceManager.StartAsync();
                ClearSuspendedServiceRestore();
                LastActionText = $"Действие: игровой режим {selectedGameMode.Label}, служба снова запущена";
                notificationText = $"Игровой режим: {selectedGameMode.Label}. Служба снова запущена.";
            }
            else if (restoreProfile is not null)
            {
                RuntimeStatus = $"Перезапускаем {restoreProfile.Name} для применения игрового режима...";
                await _processService.StopAsync(installation);
                await Task.Delay(700);
                await _processService.StartAsync(installation, restoreProfile);
                _settings.LastStartedConfigPath = restoreProfile.FilePath;
                _settingsService.Save(_settings);
                LastActionText = $"Действие: игровой режим {selectedGameMode.Label}, профиль {restoreProfile.Name} перезапущен";
                notificationText = $"Игровой режим: {selectedGameMode.Label}. Профиль {restoreProfile.Name} перезапущен.";
            }
            else
            {
                RuntimeStatus = "Игровой режим обновлён";
                LastActionText = $"Действие: игровой режим {selectedGameMode.Label} сохранён";
                notificationText = $"Игровой режим: {selectedGameMode.Label}. Настройка сохранена.";
            }

            await RefreshLiveStatusCoreAsync();
        });

        ShowInlineNotification(notificationText);
    }

    private async Task RunTestsAsync(bool allowDnsSuggestion = true, bool isAutomaticMode = false)
    {
        if (_installation is null)
        {
            return;
        }

        var shouldRestoreService = false;
        ConfigProfile? restoreProfile = null;
        var shouldOfferDnsSuggestion = false;
        var wasCancelled = false;
        var failedWithException = false;
        var targetArg = string.IsNullOrWhiteSpace(ManualTarget) ? null : ManualTarget.Trim();

        try
        {
            IsBusy = true;
            IsProbeRunning = true;
            _probeCancellation = new CancellationTokenSource();
            _probeResults.Clear();
            RebuildConfigRows();
            RecommendedConfigText = "Рекомендуемый конфиг: идёт проверка...";
            RuntimeStatus = "Готовим проверку конфигов...";
            LastActionText = "Действие: запускаем проверку конфигов";

            var serviceStatus = _serviceManager.GetStatus();
            shouldRestoreService = serviceStatus.IsRunning || ShouldRestoreSuspendedService(_installation);
            var shouldRestoreProfile = !shouldRestoreService && _processService.GetRunningProcessCount(_installation) > 0;
            restoreProfile = shouldRestoreProfile ? ResolveRestoreProfile() : null;
            var cancellationToken = _probeCancellation.Token;

            if (shouldRestoreService)
            {
                RuntimeStatus = "Останавливаем службу перед проверкой...";
                await _serviceManager.StopAsync();
                await Task.Delay(1000, cancellationToken);
            }

            RuntimeStatus = "Останавливаем активные процессы перед проверкой...";
            await StopRelatedInstallationsAsync(_installation.RootPath, waitForDrivers: false, cancellationToken);
            await Task.Delay(500, cancellationToken);

            var visibleProfiles = GetVisibleProfiles().ToList();
            BeginProbeProgress(visibleProfiles.Count, shouldRestoreService || restoreProfile is not null);
            for (var index = 0; index < visibleProfiles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var profile = visibleProfiles[index];
                RuntimeStatus = $"Проверяем {profile.Name} ({index + 1}/{visibleProfiles.Count})";
                LastActionText = $"Действие: проверяем конфиг {profile.Name} ({index + 1}/{visibleProfiles.Count})";
                MarkProbeProfileStarted(index);

                var result = await _connectivityTestService.ProbeConfigAsync(_installation, profile, targetArg, cancellationToken);
                _probeResults[result.ConfigName] = result;
                RebuildConfigRows();
                MarkProbeProfileCompleted(index + 1);

                var bestSoFar = GetRecommendedResult();
                if (bestSoFar is not null)
                {
                    RecommendedConfigText = $"Рекомендуемый конфиг: {bestSoFar.ConfigName} (пока лучший)";
                }
            }

            var recommended = GetRecommendedResult();
            if (recommended is not null)
            {
                RecommendedConfigText = $"Рекомендуемый конфиг: {recommended.ConfigName}";
                SelectedConfigRow = ConfigRows.FirstOrDefault(item =>
                    string.Equals(item.ConfigName, recommended.ConfigName, StringComparison.OrdinalIgnoreCase));
                LastActionText = $"Действие: лучший найденный конфиг {recommended.ConfigName}";
            }
            else
            {
                RecommendedConfigText = "Рекомендуемый конфиг: определить не удалось";
                LastActionText = "Действие: проверка завершена без подходящего результата";
            }

            shouldOfferDnsSuggestion = allowDnsSuggestion && ShouldOfferDnsSuggestion();
            RuntimeStatus = "Проверка конфигов завершена";
            BusyEtaText = shouldRestoreService || restoreProfile is not null
                ? "Осталось примерно: меньше 10 сек."
                : string.Empty;
            BusyProgressValue = 100;
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            RuntimeStatus = "Проверка конфигов остановлена пользователем";
            RecommendedConfigText = "Рекомендуемый конфиг: проверка остановлена";
            LastActionText = "Действие: проверка конфигов отменена";
        }
        catch (Exception ex)
        {
            failedWithException = true;
            LastActionText = "Действие: ошибка проверки конфигов";
            DialogService.ShowError(ex, "Zapret Manager");
        }
        finally
        {
            if (_installation is not null)
            {
                try
                {
                    if (shouldRestoreService)
                    {
                        RuntimeStatus = "Возвращаем службу после проверки...";
                        BusyEtaText = "Осталось примерно: меньше 10 сек.";
                        await _serviceManager.StartAsync();
                        ClearSuspendedServiceRestore();
                        LastActionText = $"{LastActionText}. Служба снова запущена";
                    }
                    else if (restoreProfile is not null)
                    {
                        RuntimeStatus = $"Возвращаем профиль {restoreProfile.Name}...";
                        BusyEtaText = "Осталось примерно: меньше 10 сек.";
                        await _processService.StartAsync(_installation, restoreProfile);
                        _settings.LastStartedConfigPath = restoreProfile.FilePath;
                        _settingsService.Save(_settings);
                        LastActionText = $"{LastActionText}. Возвращён профиль {restoreProfile.Name}";
                    }
                }
                catch (Exception restoreEx)
                {
                    LastActionText = $"{LastActionText}. Ошибка возврата: {restoreEx.Message}";
                }
            }

            _probeCancellation?.Dispose();
            _probeCancellation = null;
            ResetBusyProgressState();
            IsProbeRunning = false;
            IsBusy = false;
            await RefreshLiveStatusCoreAsync();
            RaiseCommandStates();
        }

        if (!wasCancelled &&
            !failedWithException &&
            allowDnsSuggestion &&
            shouldOfferDnsSuggestion &&
            await TrySuggestDnsAndRerunAsync(isAutomaticMode, targetArg))
        {
            return;
        }
    }

    private async Task RunAutomaticInstallAsync()
    {
        if (IsProbeRunning)
        {
            LastActionText = "Действие: нельзя запускать автоматический режим во время проверки";
            return;
        }

        if (_installation is null)
        {
            var discoveredInstallation = _discoveryService.DiscoverQuick(Directory.GetCurrentDirectory());
            if (discoveredInstallation is not null)
            {
                await SelectInstallationAsync(
                    discoveredInstallation,
                    $"Действие: автоматический режим нашёл существующую сборку {discoveredInstallation.RootPath}",
                    checkUpdates: false);

                ShowInlineNotification($"Автоматический режим нашёл существующую сборку: {discoveredInstallation.Version}.");

                var shouldContinueAutomaticMode = await PromptAutomaticModeUpdateAsync();
                if (!shouldContinueAutomaticMode || _installation is null)
                {
                    return;
                }
            }
            else
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Выберите папку, куда установить zapret для автоматического режима"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                UpdateOperationResult? installResult = null;
                await RunBusyAsync(async () =>
                {
                    UpdateStatus = "Обновления: скачиваем свежую сборку для автоматического режима...";
                    RuntimeStatus = "Автоматический режим: скачиваем zapret...";
                    LastActionText = "Действие: автоматический режим скачивает свежий zapret";
                    installResult = await _updateService.InstallFreshAsync(dialog.FolderName);
                    UpdateStatus = $"Обновления: установлена версия {installResult.InstalledVersion}";
                });

                if (installResult is null)
                {
                    return;
                }

                var installation = _discoveryService.TryLoad(installResult.ActiveRootPath)
                                  ?? throw new InvalidOperationException("Свежая сборка скачалась, но её не удалось подключить.");

                await SelectInstallationAsync(installation, $"Действие: автоматический режим подключил {installResult.ActiveRootPath}");
            }
        }
        else
        {
            LastActionText = $"Действие: автоматический режим использует {Path.GetFileName(_installation.RootPath)}";
        }

        var preparationWarnings = await PrepareAutomaticModeEnvironmentAsync(_installation);
        if (preparationWarnings.Count > 0)
        {
            ShowInlineNotification(
                "Автоматический режим продолжил работу, но не всё удалось подготовить: " + string.Join("; ", preparationWarnings),
                isError: true,
                durationMs: 6500);
        }

        await RunTestsAsync(allowDnsSuggestion: true, isAutomaticMode: true);

        if (IsProbeRunning || _installation is null)
        {
            return;
        }

        var recommended = GetRecommendedResult();
        if (recommended is null)
        {
            RecommendedConfigText = "Рекомендуемый конфиг: автоматический режим не нашёл подходящий результат";
            ShowInlineNotification("Автоматический режим не смог подобрать подходящий конфиг.", isError: true);
            return;
        }

        if (!HasFullPrimaryCoverage(recommended))
        {
            RecommendedConfigText = $"Рекомендуемый конфиг: {recommended.ConfigName} (частично подходит)";
            SelectedConfigRow = ConfigRows.FirstOrDefault(item =>
                string.Equals(item.ConfigName, recommended.ConfigName, StringComparison.OrdinalIgnoreCase));
            LastActionText = $"Действие: автоматический режим не нашёл полностью подходящий конфиг, лучший результат {recommended.ConfigName}";
            ShowInlineNotification(
                $"Автоматический режим не стал устанавливать службу: лучший результат {recommended.ConfigName} дал только {FormatPrimaryCoverage(recommended)} по главным сайтам.",
                isError: true,
                durationMs: 6500);
            DialogService.ShowInfo(
                $"Автоматический режим не нашёл полностью подходящий конфиг.{Environment.NewLine}{Environment.NewLine}Лучший результат:{Environment.NewLine}Конфиг: {recommended.ConfigName}{Environment.NewLine}Главные сайты: {FormatPrimaryCoverage(recommended)}{Environment.NewLine}Сводка: {recommended.Details}{Environment.NewLine}{Environment.NewLine}Служба автоматически не установлена. Можно посмотреть таблицу результатов, попробовать другой DNS или настроить конфиг вручную.",
                "Автоматический режим");
            return;
        }

        SelectedConfigRow = ConfigRows.FirstOrDefault(item =>
            string.Equals(item.ConfigName, recommended.ConfigName, StringComparison.OrdinalIgnoreCase));

        if (SelectedConfig is null)
        {
            RecommendedConfigText = $"Рекомендуемый конфиг: {recommended.ConfigName}";
            ShowInlineNotification($"Автоматический режим нашёл {recommended.ConfigName}, но не смог выбрать его в списке.", isError: true);
            return;
        }

        RuntimeStatus = $"Автоматический режим: устанавливаем службу для {SelectedConfig.Name}...";
        LastActionText = $"Действие: автоматический режим выбрал {SelectedConfig.Name}";
        await InstallServiceAsync();
        RecommendedConfigText = $"Рекомендуемый конфиг: {SelectedConfig.Name}";
        LastActionText = $"Действие: автоматический режим установил службу для {SelectedConfig.Name}";
        ShowInlineNotification($"Автоматический режим: выбран и установлен {SelectedConfig.Name}.");
        DialogService.ShowInfo(
            $"Лучший конфиг найден, выбран и сразу установлен как служба.{Environment.NewLine}{Environment.NewLine}Сводка:{Environment.NewLine}Сборка: {_installation.Version}{Environment.NewLine}Конфиг: {SelectedConfig.Name}{Environment.NewLine}Служба: установлена и запущена{Environment.NewLine}Проверка: {recommended.Details}{Environment.NewLine}{Environment.NewLine}Теперь можно сразу открыть нужные сайты и проверить результат.",
            "Автоматический режим готов");
    }

    private async Task<List<string>> PrepareAutomaticModeEnvironmentAsync(ZapretInstallation? installation)
    {
        var warnings = new List<string>();
        if (installation is null)
        {
            return warnings;
        }

        try
        {
            RuntimeStatus = "Автоматический режим: обновляем список IPSet...";
            LastActionText = "Действие: автоматический режим обновляет список IPSet";
            await _repositoryMaintenanceService.UpdateIpSetListAsync(installation);
            _ipSetService.SetMode(installation, "loaded");
            var activeIpSetEntryCount = _repositoryMaintenanceService.GetActiveIpSetEntryCount(installation);
            if (activeIpSetEntryCount == 0)
            {
                throw new InvalidOperationException("Активный список IPSet остался пустым.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add("IPSet не обновлён");
            LastActionText = $"Действие: не удалось обновить IPSet - {ex.Message}";
        }

        try
        {
            RuntimeStatus = "Автоматический режим: обновляем hosts...";
            LastActionText = "Действие: автоматический режим обновляет hosts";
            await _repositoryMaintenanceService.UpdateHostsFileAsync();
            var managedHostsEntryCount = _repositoryMaintenanceService.GetManagedHostsEntryCount();
            if (managedHostsEntryCount == 0)
            {
                throw new InvalidOperationException("В системный hosts не попал ни один управляемый адрес.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add("hosts не обновлён");
            LastActionText = $"Действие: не удалось обновить hosts - {ex.Message}";
        }

        await RefreshLiveStatusCoreAsync();
        if (warnings.Count == 0)
        {
            LastActionText = "Действие: автоматический режим подготовил IPSet и hosts";
        }

        return warnings;
    }

    private bool ShouldOfferDnsSuggestion()
    {
        if (_probeResults.Count == 0 ||
            !string.Equals(GetCurrentDnsProfileKey(), DnsService.SystemProfileKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _probeResults.Values.All(result =>
            result.PrimaryTotalCount > 0 &&
            result.PrimaryFailedTargetNames.Count > 0);
    }

    private async Task<bool> TrySuggestDnsAndRerunAsync(bool isAutomaticMode, string? targetArg)
    {
        if (_installation is null)
        {
            return false;
        }

        var diagnosis = await DiagnoseDnsIssueAsync(_installation, targetArg);
        if (diagnosis is null)
        {
            return false;
        }

        const string suggestedDnsKey = DnsService.GoogleProfileKey;
        var suggestedDnsLabel = _dnsService.GetProfileLabel(
            suggestedDnsKey,
            customPrimary: null,
            customSecondary: null,
            customDohTemplate: null,
            useDnsOverHttps: true);

        var message = isAutomaticMode
            ? $"{diagnosis} Включить {suggestedDnsLabel} и сразу повторить автоматическую проверку?"
            : $"{diagnosis} Включить {suggestedDnsLabel} и повторить проверку?";

        var accepted = DialogService.ConfirmCustom(
            message,
            "Проверка DNS",
            primaryButtonText: "Включить DNS",
            secondaryButtonText: "Оставить как есть");

        if (!accepted)
        {
            LastActionText = "Действие: повторная проверка с DNS отклонена";
            return false;
        }

        var dnsApplied = await ApplyDnsProfileAsync(
            suggestedDnsKey,
            customPrimary: null,
            customSecondary: null,
            useDnsOverHttps: true,
            customDohTemplate: _settings.CustomDnsDohTemplate);

        if (!dnsApplied)
        {
            return false;
        }

        ShowInlineNotification($"Включён {suggestedDnsLabel}. Повторяем проверку...");
        await RunTestsAsync(allowDnsSuggestion: false, isAutomaticMode);
        return true;
    }

    private async Task<string?> DiagnoseDnsIssueAsync(ZapretInstallation installation, string? targetArg)
    {
        var candidateHosts = GetDnsDiagnosisHosts(installation, targetArg);
        if (candidateHosts.Count == 0)
        {
            return null;
        }

        var diagnosis = await _dnsDiagnosisService.AnalyzeAsync(candidateHosts);
        var matches = diagnosis.Results
            .Where(item => !item.SystemResolved && item.PublicResolved)
            .Select(item => item.Host)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matches.Length == 0)
        {
            return null;
        }

        var visibleHosts = matches.Take(2).ToArray();
        var suffix = matches.Length > visibleHosts.Length
            ? $" и ещё {matches.Length - visibleHosts.Length}"
            : string.Empty;

        return $"Похоже, текущий DNS не может нормально разрешить: {string.Join(", ", visibleHosts)}{suffix}.";
    }

    private IReadOnlyList<string> GetDnsDiagnosisHosts(ZapretInstallation installation, string? targetArg)
    {
        var targetMap = _connectivityTestService.BuildTargetMap(installation, targetArg);
        if (targetMap.Count == 0 || _probeResults.Count == 0)
        {
            return [];
        }

        var threshold = Math.Max(1, _probeResults.Count);
        var failureCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in _probeResults.Values)
        {
            foreach (var failedTargetName in result.PrimaryFailedTargetNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!targetMap.TryGetValue(failedTargetName, out var target))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(target.PingHost))
                {
                    continue;
                }

                failureCounts[target.PingHost] = failureCounts.TryGetValue(target.PingHost, out var count)
                    ? count + 1
                    : 1;
            }
        }

        return failureCounts
            .Where(item => item.Value >= threshold)
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Key)
            .Take(5)
            .ToArray();
    }

    private void ToggleProbe()
    {
        if (IsProbeRunning)
        {
            CancelProbe();
            return;
        }

        try
        {
            EnsureManagerExecutableAvailable();
        }
        catch (Exception ex)
        {
            LastActionText = "Действие: проверка недоступна после переноса программы";
            DialogService.ShowError(ex, "Zapret Manager");
            return;
        }

        _ = RunTestsAsync(allowDnsSuggestion: true, isAutomaticMode: false);
    }

    private void CancelProbe()
    {
        _probeCancellation?.Cancel();
    }

    private ConfigProfile? ResolveRestoreProfile()
    {
        return Configs.FirstOrDefault(item =>
                   string.Equals(item.FilePath, _settings.LastStartedConfigPath, StringComparison.OrdinalIgnoreCase))
               ?? SelectedConfig;
    }

    private ConfigProfile? ResolveLastInstalledServiceProfile()
    {
        if (_installation is null)
        {
            return null;
        }

        return _installation.Profiles.FirstOrDefault(item =>
                   string.Equals(item.FilePath, _settings.LastInstalledServiceConfigPath, StringComparison.OrdinalIgnoreCase))
               ?? FindProfileByIdentity(
                   _installation,
                   _settings.LastInstalledServiceProfileName,
                   _settings.LastInstalledServiceProfileFileName);
    }

    private ConfigProfile? ResolveTrayServiceProfile()
    {
        return ResolveLastInstalledServiceProfile() ?? SelectedConfig;
    }

    private void RememberRunningServiceProfile(ServiceStatusInfo serviceStatus)
    {
        if (_installation is null || !serviceStatus.IsInstalled || string.IsNullOrWhiteSpace(serviceStatus.ProfileName))
        {
            return;
        }

        var installedProfile = FindProfileByIdentity(_installation, serviceStatus.ProfileName, profileFileName: null);
        if (installedProfile is not null)
        {
            RememberInstalledServiceProfile(installedProfile);
        }
    }

    private void RememberInstalledServiceProfile(ConfigProfile? profile, bool saveImmediately = true)
    {
        var newPath = profile?.FilePath;
        var newName = profile?.Name;
        var newFileName = profile?.FileName;
        var changed =
            !string.Equals(_settings.LastInstalledServiceConfigPath, newPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_settings.LastInstalledServiceProfileName, newName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_settings.LastInstalledServiceProfileFileName, newFileName, StringComparison.OrdinalIgnoreCase);

        _settings.LastInstalledServiceConfigPath = newPath;
        _settings.LastInstalledServiceProfileName = newName;
        _settings.LastInstalledServiceProfileFileName = newFileName;

        if (saveImmediately && changed)
        {
            _settingsService.Save(_settings);
        }
    }

    private IEnumerable<ConfigProfile> GetVisibleProfiles()
    {
        return Configs
            .Where(profile => !IsHiddenConfig(profile.FilePath))
            .OrderBy(profile => profile, ConfigProfileNaturalComparer.Instance);
    }

    private bool IsHiddenConfig(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        return _settings.HiddenConfigPaths.Any(path =>
            string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ConfigProfileNaturalComparer : IComparer<ConfigProfile>
    {
        public static ConfigProfileNaturalComparer Instance { get; } = new();

        public int Compare(ConfigProfile? x, ConfigProfile? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var rankCompare = GetPriority(x).CompareTo(GetPriority(y));
            if (rankCompare != 0)
            {
                return rankCompare;
            }

            var nameCompare = CompareNatural(x.Name, y.Name);
            if (nameCompare != 0)
            {
                return nameCompare;
            }

            return CompareNatural(x.FileName, y.FileName);
        }

        private static int GetPriority(ConfigProfile profile)
        {
            var normalized = profile.Name.Trim();
            if (string.Equals(normalized, "general", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (normalized.StartsWith("general", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return 2;
        }

        private static int CompareNatural(string? left, string? right)
        {
            left ??= string.Empty;
            right ??= string.Empty;

            var leftIndex = 0;
            var rightIndex = 0;

            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                var leftIsDigit = char.IsDigit(left[leftIndex]);
                var rightIsDigit = char.IsDigit(right[rightIndex]);

                if (leftIsDigit && rightIsDigit)
                {
                    var leftStart = leftIndex;
                    while (leftIndex < left.Length && char.IsDigit(left[leftIndex]))
                    {
                        leftIndex++;
                    }

                    var rightStart = rightIndex;
                    while (rightIndex < right.Length && char.IsDigit(right[rightIndex]))
                    {
                        rightIndex++;
                    }

                    var leftNumber = left[leftStart..leftIndex].TrimStart('0');
                    var rightNumber = right[rightStart..rightIndex].TrimStart('0');
                    leftNumber = leftNumber.Length == 0 ? "0" : leftNumber;
                    rightNumber = rightNumber.Length == 0 ? "0" : rightNumber;

                    var lengthCompare = leftNumber.Length.CompareTo(rightNumber.Length);
                    if (lengthCompare != 0)
                    {
                        return lengthCompare;
                    }

                    var numericCompare = string.Compare(leftNumber, rightNumber, StringComparison.Ordinal);
                    if (numericCompare != 0)
                    {
                        return numericCompare;
                    }

                    continue;
                }

                var charCompare = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
                if (charCompare != 0)
                {
                    return charCompare;
                }

                leftIndex++;
                rightIndex++;
            }

            return left.Length.CompareTo(right.Length);
        }
    }

    private void PruneHiddenConfigPaths()
    {
        var normalizedPaths = _settings.HiddenConfigPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_installation is not null)
        {
            normalizedPaths = normalizedPaths
                .Where(path => !PathStartsWith(path, _installation.RootPath) || File.Exists(path))
                .ToList();
        }

        if (normalizedPaths.Count == _settings.HiddenConfigPaths.Count &&
            !_settings.HiddenConfigPaths.Except(normalizedPaths, StringComparer.OrdinalIgnoreCase).Any())
        {
            return;
        }

        _settings.HiddenConfigPaths = normalizedPaths;
        _settingsService.Save(_settings);
    }

    private string GetDefaultTargetsTemplate()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var bundledPath = Path.Combine(Path.GetDirectoryName(processPath)!, "Defaults", "targets.default.txt");
            if (File.Exists(bundledPath))
            {
                return File.ReadAllText(bundledPath);
            }
        }

        return """
               # targets.txt - endpoint list for zapret.ps1 tests
               DiscordMain = "https://discord.com"
               DiscordGateway = "https://gateway.discord.gg"
               DiscordCDN = "https://cdn.discordapp.com"
               DiscordUpdates = "https://updates.discord.com"
               YouTubeWeb = "https://www.youtube.com"
               YouTubeShort = "https://youtu.be"
               YouTubeImage = "https://i.ytimg.com"
               YouTubeVideoRedirect = "https://redirector.googlevideo.com"
               GoogleMain = "https://www.google.com"
               GoogleGstatic = "https://www.gstatic.com"
               CloudflareWeb = "https://www.cloudflare.com"
               CloudflareCDN = "https://cdnjs.cloudflare.com"
               CloudflareDNS1111 = "PING:1.1.1.1"
               CloudflareDNS1001 = "PING:1.0.0.1"
               GoogleDNS8888 = "PING:8.8.8.8"
               GoogleDNS8844 = "PING:8.8.4.4"
               Quad9DNS9999 = "PING:9.9.9.9"
               """ + Environment.NewLine;
    }

    private ConfigProbeResult? GetRecommendedResult()
    {
        return _probeResults.Values
            .OrderBy(item => item.PrimaryFailedTargetNames.Count)
            .ThenBy(item => item.PrimaryPartialTargetNames.Count)
            .ThenBy(item => item.SupplementaryFailedTargetNames.Count)
            .ThenByDescending(item => item.PrimarySuccessCount)
            .ThenByDescending(item => item.PrimaryTotalCount)
            .ThenByDescending(item => item.SuccessCount)
            .ThenBy(item => item.PartialCount)
            .ThenBy(item => item.AveragePingMilliseconds ?? long.MaxValue)
            .ThenBy(item => item.ConfigName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private void RebuildConfigRows()
    {
        var selectedPath = SelectedConfig?.FilePath ?? _settings.LastSelectedConfigPath;
        _isRebuildingRows = true;
        ConfigRows.Clear();

        foreach (var profile in GetVisibleProfiles())
        {
            _probeResults.TryGetValue(profile.Name, out var probeResult);
            ConfigRows.Add(new ConfigTableRow
            {
                ConfigName = profile.Name,
                FileName = profile.FileName,
                FilePath = profile.FilePath,
                Outcome = probeResult?.Outcome ?? ProbeOutcomeKind.Failure,
                AveragePingMilliseconds = probeResult?.AveragePingMilliseconds,
                SuccessCount = probeResult?.SuccessCount,
                TotalCount = probeResult?.TotalCount,
                PartialCount = probeResult?.PartialCount,
                Summary = probeResult?.Summary ?? string.Empty,
                Details = probeResult?.Details ?? string.Empty
            });
        }

        SelectedConfigRow = ConfigRows.FirstOrDefault(item =>
                               string.Equals(item.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
                           ?? ConfigRows.FirstOrDefault();
        SelectedConfig = SelectedConfigRow is null
            ? null
            : Configs.FirstOrDefault(item => string.Equals(item.FilePath, SelectedConfigRow.FilePath, StringComparison.OrdinalIgnoreCase));
        _isRebuildingRows = false;
    }

    private async Task CheckUpdatesAsync(bool showMessage)
    {
        if (_installation is null)
        {
            return;
        }

        var hasUpdate = await RefreshUpdateAvailabilityAsync(showNoUpdatesMessage: showMessage);
        if (!showMessage || !hasUpdate || string.IsNullOrWhiteSpace(_updateLatestVersion))
        {
            return;
        }

        var currentVersion = _installation.Version;
        var shouldUpdate = DialogService.ConfirmCustom(
            $"Найдена новая версия: {_updateLatestVersion}.{Environment.NewLine}Текущая: {currentVersion}",
            "Zapret Manager",
            primaryButtonText: "Обновить",
            secondaryButtonText: "Закрыть");

        if (shouldUpdate)
        {
            await ApplyUpdateAsync();
        }
    }

    private async Task<bool> RefreshUpdateAvailabilityAsync(bool showNoUpdatesMessage)
    {
        if (_installation is null)
        {
            return false;
        }

        var installationVersion = _installation.Version;
        await RunBusyAsync(async () =>
        {
            UpdateStatus = "Обновления: проверяем...";
            LastActionText = "Действие: проверяем обновления";
            var update = await _updateService.GetUpdateInfoAsync(installationVersion);
            _updateDownloadUrl = update.DownloadUrl;
            _updateLatestVersion = update.LatestVersion;
            HasUpdate = update.IsUpdateAvailable && !string.IsNullOrWhiteSpace(update.DownloadUrl);

            if (HasUpdate)
            {
                UpdateStatus = $"Обновления: доступна версия {update.LatestVersion}";
                LastActionText = $"Действие: найдено обновление {update.LatestVersion}";
            }
            else
            {
                UpdateStatus = $"Обновления: актуальная версия {installationVersion}";
                LastActionText = $"Действие: обновлений нет, версия {installationVersion} актуальна";
            }
        });

        if (!HasUpdate && showNoUpdatesMessage)
        {
            DialogService.ShowInfo("Новых обновлений не найдено.", "Zapret Manager");
        }

        return HasUpdate;
    }

    private async Task ApplyUpdateAsync(bool promptForAutomaticMode = true)
    {
        if (_installation is null || string.IsNullOrWhiteSpace(_updateDownloadUrl) || string.IsNullOrWhiteSpace(_updateLatestVersion))
        {
            return;
        }

        var currentInstallation = _installation;
        var selectedProfileName = SelectedConfig?.Name;
        var selectedProfileFileName = SelectedConfig?.FileName;
        var startedProfile = currentInstallation.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.FilePath, _settings.LastStartedConfigPath, StringComparison.OrdinalIgnoreCase));
        var startedProfileName = startedProfile?.Name;
        var startedProfileFileName = startedProfile?.FileName;
        var installedServiceProfile = currentInstallation.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.FilePath, _settings.LastInstalledServiceConfigPath, StringComparison.OrdinalIgnoreCase));
        var installedServiceProfileName = installedServiceProfile?.Name ?? _settings.LastInstalledServiceProfileName;
        var installedServiceProfileFileName = installedServiceProfile?.FileName ?? _settings.LastInstalledServiceProfileFileName;

        UpdateOperationResult? updateResult = null;
        await RunBusyAsync(async () =>
        {
            ClearSuspendedServiceRestore();
            UpdateStatus = $"Обновления: обновляем сборку до {_updateLatestVersion}...";
            RuntimeStatus = "Обновление: переносим ваши списки и ставим новую версию...";
            LastActionText = $"Действие: обновляем сборку до {_updateLatestVersion}";
            updateResult = await _updateService.ApplyUpdateAsync(currentInstallation.RootPath, _updateDownloadUrl, _updateLatestVersion);

            _installation = _discoveryService.TryLoad(updateResult.ActiveRootPath);
            if (_installation is not null)
            {
                _settings.LastInstallationPath = _installation.RootPath;
                _settings.HiddenConfigPaths.Clear();
                UpdateRememberedProfilePaths(
                    _installation,
                    selectedProfileName,
                    selectedProfileFileName,
                    startedProfileName,
                    startedProfileFileName,
                    installedServiceProfileName,
                    installedServiceProfileFileName);
                _settingsService.Save(_settings);
            }

            UpdateStatus = $"Обновления: установлена версия {updateResult.InstalledVersion}";
            LastActionText = $"Действие: сборка обновлена в {updateResult.ActiveRootPath}";
            await RefreshCoreAsync();
        }, rethrowExceptions: true);

        if (_installation is not null && AutoCheckUpdatesEnabled)
        {
            await CheckUpdatesAsync(false);
        }

        if (updateResult is null)
        {
            return;
        }

        var previousVersionNote = string.IsNullOrWhiteSpace(updateResult.BackupRootPath)
            ? "Предыдущая версия не была сохранена."
            : "Предыдущая версия сохранена. Её можно вернуть кнопкой «Откатить сборку».";

        if (!promptForAutomaticMode)
        {
            ShowInlineNotification("Обновление завершено. Новая сборка подключена.");
            return;
        }

        var shouldRunAutomaticMode = DialogService.ConfirmCustom(
            $"Новая версия установлена в:{Environment.NewLine}{updateResult.ActiveRootPath}{Environment.NewLine}{Environment.NewLine}{previousVersionNote}{Environment.NewLine}{Environment.NewLine}Ваши списки и targets.txt перенесены в новую сборку.{Environment.NewLine}{Environment.NewLine}Запустить автоматический режим сейчас?",
            "Zapret Manager",
            primaryButtonText: "Авто режим",
            secondaryButtonText: "Позже");

        if (shouldRunAutomaticMode)
        {
            await RunAutomaticInstallAsync();
            return;
        }

        ShowInlineNotification("Обновление завершено. Новая сборка подключена.");
    }

    private async Task RestorePreviousVersionAsync()
    {
        if (_installation is null)
        {
            return;
        }

        var currentInstallation = _installation;
        var previousRootPath = _updateService.TryGetStoredPreviousVersionPath(currentInstallation.RootPath);
        if (string.IsNullOrWhiteSpace(previousRootPath))
        {
            HasPreviousVersion = false;
            ShowInlineNotification("Сохранённая предыдущая версия zapret не найдена.", isError: true);
            return;
        }

        var previousInstallation = _discoveryService.TryLoad(previousRootPath);
        var previousVersion = previousInstallation?.Version ?? "предыдущая версия";
        var shouldRestore = DialogService.ConfirmCustom(
            $"Текущая версия: {currentInstallation.Version}{Environment.NewLine}Сохранённая предыдущая версия: {previousVersion}{Environment.NewLine}{Environment.NewLine}Откатить сборку сейчас?",
            "Zapret Manager",
            primaryButtonText: "Откатить",
            secondaryButtonText: "Отмена");

        if (!shouldRestore)
        {
            return;
        }

        var selectedProfileName = SelectedConfig?.Name;
        var selectedProfileFileName = SelectedConfig?.FileName;
        var startedProfile = currentInstallation.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.FilePath, _settings.LastStartedConfigPath, StringComparison.OrdinalIgnoreCase));
        var startedProfileName = startedProfile?.Name;
        var startedProfileFileName = startedProfile?.FileName;
        var installedServiceProfile = currentInstallation.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.FilePath, _settings.LastInstalledServiceConfigPath, StringComparison.OrdinalIgnoreCase));
        var installedServiceProfileName = installedServiceProfile?.Name ?? _settings.LastInstalledServiceProfileName;
        var installedServiceProfileFileName = installedServiceProfile?.FileName ?? _settings.LastInstalledServiceProfileFileName;

        UpdateOperationResult? restoreResult = null;
        await RunBusyAsync(async () =>
        {
            ClearSuspendedServiceRestore();
            UpdateStatus = $"Обновления: откатываем сборку до {previousVersion}...";
            RuntimeStatus = "Откат: возвращаем предыдущую версию zapret...";
            LastActionText = $"Действие: откат сборки до {previousVersion}";

            var serviceStatus = _serviceManager.GetStatus();
            if (serviceStatus.IsInstalled)
            {
                await _serviceManager.RemoveAsync();
                await WaitForServiceRemovalAsync(TimeSpan.FromSeconds(20));
            }

            await _processService.StopCheckUpdatesShellsAsync();
            await _processService.StopAsync(null);
            await _processService.StopAsync(currentInstallation);
            await WaitForProcessExitAsync(currentInstallation, TimeSpan.FromSeconds(20));
            await WaitForDriverReleaseAsync(currentInstallation.RootPath, TimeSpan.FromSeconds(60));

            restoreResult = await _updateService.RestorePreviousVersionAsync(currentInstallation.RootPath);

            _installation = _discoveryService.TryLoad(restoreResult.ActiveRootPath);
            if (_installation is not null)
            {
                _settings.LastInstallationPath = _installation.RootPath;
                _settings.HiddenConfigPaths.Clear();
                UpdateRememberedProfilePaths(
                    _installation,
                    selectedProfileName,
                    selectedProfileFileName,
                    startedProfileName,
                    startedProfileFileName,
                    installedServiceProfileName,
                    installedServiceProfileFileName);
                _settingsService.Save(_settings);
            }

            UpdateStatus = $"Обновления: активна версия {restoreResult.InstalledVersion}";
            LastActionText = $"Действие: восстановлена версия {restoreResult.InstalledVersion}";
            await RefreshCoreAsync();
        }, rethrowExceptions: true);

        if (restoreResult is null)
        {
            return;
        }

        if (_installation is not null && AutoCheckUpdatesEnabled)
        {
            await CheckUpdatesAsync(false);
        }

        ShowInlineNotification($"Восстановлена предыдущая версия {restoreResult.InstalledVersion}. Текущая сборка сохранена для обратного отката.");
    }

    private void UpdateRememberedProfilePaths(
        ZapretInstallation installation,
        string? selectedProfileName,
        string? selectedProfileFileName,
        string? startedProfileName,
        string? startedProfileFileName,
        string? installedServiceProfileName,
        string? installedServiceProfileFileName)
    {
        var selectedProfile = FindProfileByIdentity(installation, selectedProfileName, selectedProfileFileName);
        _settings.LastSelectedConfigPath = selectedProfile?.FilePath;

        var startedProfile = FindProfileByIdentity(installation, startedProfileName, startedProfileFileName);
        _settings.LastStartedConfigPath = startedProfile?.FilePath;

        var installedServiceProfile = FindProfileByIdentity(installation, installedServiceProfileName, installedServiceProfileFileName);
        RememberInstalledServiceProfile(installedServiceProfile, saveImmediately: false);
    }

    private static ConfigProfile? FindProfileByIdentity(
        ZapretInstallation installation,
        string? profileName,
        string? profileFileName)
    {
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            var byName = installation.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return byName;
            }
        }

        if (!string.IsNullOrWhiteSpace(profileFileName))
        {
            var byFileName = installation.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.FileName, profileFileName, StringComparison.OrdinalIgnoreCase));
            if (byFileName is not null)
            {
                return byFileName;
            }
        }

        return null;
    }

    private void UseDefaultTargets()
    {
        ManualTarget = string.Empty;
        _settings.SelectedTargetGroupKeys = [];
        _settingsService.Save(_settings);
        RefreshSelectedTargetsDisplay();
    }

    private void UseTargetGroupPreset(string key)
    {
        if (!_builtInTargetGroups.TryGetValue(key, out var group))
        {
            return;
        }

        ManualTarget = group.TargetsText;
        _settings.SelectedTargetGroupKeys = [];
        _settingsService.Save(_settings);
        RefreshSelectedTargetsDisplay();
    }

    private void RefreshSelectedTargetsDisplay()
    {
        var selectedGroups = GetAllTargetGroups()
            .Where(group => _settings.SelectedTargetGroupKeys.Contains(group.Key, StringComparer.OrdinalIgnoreCase))
            .Select(group => group.Name)
            .ToList();

        SelectedTargetsDisplayText = selectedGroups.Count == 0
            ? "Все цели из targets.txt"
            : string.Join(", ", selectedGroups);
    }

    private List<TargetGroupDefinition> GetAllTargetGroups()
    {
        var customGroups = (_settings.CustomTargetGroups ?? [])
            .Where(group => !string.IsNullOrWhiteSpace(group.Name) && !string.IsNullOrWhiteSpace(group.TargetsText))
            .Select(group => new TargetGroupDefinition
            {
                Key = string.IsNullOrWhiteSpace(group.Key) ? "custom-" + Guid.NewGuid().ToString("N") : group.Key,
                Name = group.Name.Trim(),
                TargetsText = group.TargetsText.Trim(),
                IsCustom = true
            });

        return _builtInTargetGroups.Values
            .Concat(customGroups)
            .OrderBy(group => group.IsCustom)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<TargetGroupDefinition> CreateBuiltInTargetGroups()
    {
        yield return new TargetGroupDefinition
        {
            Key = "youtube",
            Name = "YouTube",
            TargetsText = "www.youtube.com, https://www.youtube.com/watch?v=jNQXAC9IVRw, youtu.be, i.ytimg.com, redirector.googlevideo.com"
        };
        yield return new TargetGroupDefinition
        {
            Key = "discord",
            Name = "Discord",
            TargetsText = "discord.com, gateway.discord.gg, cdn.discordapp.com, updates.discord.com"
        };
        yield return new TargetGroupDefinition
        {
            Key = "google",
            Name = "Google",
            TargetsText = "google.com, gstatic.com"
        };
        yield return new TargetGroupDefinition
        {
            Key = "cloudflare",
            Name = "Cloudflare",
            TargetsText = "cloudflare.com, cdnjs.cloudflare.com"
        };
        yield return new TargetGroupDefinition
        {
            Key = "instagram",
            Name = "Instagram",
            TargetsText = "instagram.com, www.instagram.com, cdninstagram.com"
        };
        yield return new TargetGroupDefinition
        {
            Key = "x",
            Name = "X / Twitter",
            TargetsText = "x.com, twitter.com, twimg.com"
        };
        yield return new TargetGroupDefinition
        {
            Key = "tiktok",
            Name = "TikTok",
            TargetsText = "tiktok.com, www.tiktok.com, tiktokcdn.com"
        };
        yield return new TargetGroupDefinition
        {
            Key = "twitch",
            Name = "Twitch",
            TargetsText = "twitch.tv, www.twitch.tv, static-cdn.jtvnw.net"
        };
    }

    private string BuildTargetsHint()
    {
        if (_installation is null)
        {
            return "Цели из targets.txt, пресеты YouTube/Discord/Cloudflare или ваш набор доменов.";
        }

        var targets = _connectivityTestService.LoadTargets(_installation, null);
        var mainCount = targets.Count(item => !item.IsDiagnosticOnly);
        var diagnosticCount = targets.Count(item => item.IsDiagnosticOnly);

        return mainCount > 0
            ? $"Целей в списке: {mainCount}. Ping-диагностика: {diagnosticCount}. Быстрые пресеты: YouTube, Discord, Cloudflare."
            : "Файл targets.txt пуст или не найден. Используйте быстрые пресеты или свой список доменов.";
    }

    private bool CanStopCurrentRuntime()
    {
        if (_installation is null || IsBusy)
        {
            return false;
        }

        if (_processService.GetRunningProcessCount(_installation) == 0)
        {
            return false;
        }

        if (ShouldRestoreSuspendedService(_installation))
        {
            return true;
        }

        var serviceStatus = _serviceManager.GetStatus();
        return !serviceStatus.IsRunning;
    }

    private static bool HasFullPrimaryCoverage(ConfigProbeResult result)
    {
        return result.PrimaryTotalCount == 0 || result.PrimarySuccessCount >= result.PrimaryTotalCount;
    }

    private static string FormatPrimaryCoverage(ConfigProbeResult result)
    {
        return $"{result.PrimarySuccessCount}/{result.PrimaryTotalCount}";
    }

    private async Task RunBusyAsync(Func<Task> action, bool rethrowExceptions = false)
    {
        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception ex)
        {
            var displayMessage = DialogService.GetDisplayMessage(ex);
            LastActionText = $"Действие: ошибка - {displayMessage}";
            if (rethrowExceptions)
            {
                throw;
            }

            DialogService.ShowError(displayMessage, "Zapret Manager");
        }
        finally
        {
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    public async Task InstallSelectedServiceFromTrayAsync()
    {
        if (_installation is null || IsBusy || IsProbeRunning)
        {
            return;
        }

        var trayServiceProfile = ResolveTrayServiceProfile();
        if (trayServiceProfile is null)
        {
            return;
        }

        await InstallServiceAsync(trayServiceProfile);
    }

    public async Task RemoveServiceFromTrayAsync()
    {
        if (_installation is null || IsBusy || IsProbeRunning)
        {
            return;
        }

        await RemoveServiceAsync();
    }

    public bool HasCustomDnsConfigured()
    {
        return _dnsService.HasCustomDns(_settings.CustomDnsPrimary, _settings.CustomDnsSecondary);
    }

    public string GetCurrentDnsProfileKey()
    {
        var current = _settings.PreferredDnsProfileKey;
        if (string.IsNullOrWhiteSpace(current))
        {
            return DnsService.SystemProfileKey;
        }

        return _dnsService.GetPresetDefinitions(_settings.CustomDnsPrimary, _settings.CustomDnsSecondary)
            .Any(item => string.Equals(item.Key, current, StringComparison.OrdinalIgnoreCase))
            ? current
            : DnsService.SystemProfileKey;
    }

    public string GetTrayCustomDnsLabel()
    {
        if (!HasCustomDnsConfigured())
        {
            return "Пользовательский DNS";
        }

        var servers = _dnsService.NormalizeDnsServers(_settings.CustomDnsPrimary, _settings.CustomDnsSecondary);
        return servers.Count == 0
            ? "Пользовательский DNS"
            : $"Пользовательский DNS ({string.Join(", ", servers)})";
    }

    public async Task ToggleGameModeFromTrayAsync(bool enable)
    {
        if (_installation is null || IsBusy || IsProbeRunning)
        {
            return;
        }

        var targetValue = enable
            ? string.IsNullOrWhiteSpace(_settings.PreferredGameModeValue) ? "all" : _settings.PreferredGameModeValue
            : "disabled";

        SelectedGameMode = GameModeOptions.FirstOrDefault(item => item.Value == targetValue) ?? GameModeOptions.First();
        await ApplyGameModeAsync();
    }

    public bool IsGameModeEnabled()
    {
        return _installation is not null &&
               !string.Equals(_gameModeService.GetModeValue(_installation), "disabled", StringComparison.OrdinalIgnoreCase);
    }

    public bool CanInstallServiceFromTray()
    {
        return _installation is not null &&
               !IsBusy &&
               !IsProbeRunning &&
               ResolveTrayServiceProfile() is not null;
    }

    public string GetTrayInstallServiceText()
    {
        var trayServiceProfile = ResolveTrayServiceProfile();
        return trayServiceProfile is null
            ? "Установить службу"
            : $"Установить службу: {trayServiceProfile.Name}";
    }

    private void RaiseCommandStates()
    {
        BrowseCommand.NotifyCanExecuteChanged();
        DownloadZapretCommand.NotifyCanExecuteChanged();
        DeleteZapretCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        OpenTargetsFileCommand.NotifyCanExecuteChanged();
        OpenIncludedDomainsEditorCommand.NotifyCanExecuteChanged();
        OpenExcludedDomainsEditorCommand.NotifyCanExecuteChanged();
        OpenHostsEditorCommand.NotifyCanExecuteChanged();
        OpenUserSubnetsEditorCommand.NotifyCanExecuteChanged();
        OpenHiddenConfigsCommand.NotifyCanExecuteChanged();
        OpenIpSetModeCommand.NotifyCanExecuteChanged();
        OpenDnsSettingsCommand.NotifyCanExecuteChanged();
        OpenDiagnosticsCommand.NotifyCanExecuteChanged();
        OpenAboutCommand.NotifyCanExecuteChanged();
        CheckManagerUpdateCommand.NotifyCanExecuteChanged();
        UpdateIpSetListCommand.NotifyCanExecuteChanged();
        UpdateHostsFileCommand.NotifyCanExecuteChanged();
        ClearDiscordCacheCommand.NotifyCanExecuteChanged();
        UseDefaultTargetsCommand.NotifyCanExecuteChanged();
        UseYouTubePresetCommand.NotifyCanExecuteChanged();
        UseDiscordPresetCommand.NotifyCanExecuteChanged();
        UseCloudflarePresetCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        QuickSearchCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        HideSelectedConfigCommand.NotifyCanExecuteChanged();
        AutoInstallCommand.NotifyCanExecuteChanged();
        InstallServiceCommand.NotifyCanExecuteChanged();
        RemoveServiceCommand.NotifyCanExecuteChanged();
        RunTestsCommand.NotifyCanExecuteChanged();
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        ApplyUpdateCommand.NotifyCanExecuteChanged();
        RestorePreviousVersionCommand.NotifyCanExecuteChanged();
        ApplyGameModeCommand.NotifyCanExecuteChanged();
    }

    private async Task<bool> PromptAutomaticModeUpdateAsync()
    {
        if (_installation is null)
        {
            return false;
        }

        var currentVersion = _installation.Version;
        var hasUpdate = await RefreshUpdateAvailabilityAsync(showNoUpdatesMessage: false);
        if (!hasUpdate || string.IsNullOrWhiteSpace(_updateLatestVersion))
        {
            LastActionText = $"Действие: автоматический режим использует найденную сборку {currentVersion}";
            return true;
        }

        var choice = DialogService.ChooseCustom(
            $"Найдена существующая сборка zapret версии {currentVersion}.{Environment.NewLine}Доступна новая версия: {_updateLatestVersion}.{Environment.NewLine}{Environment.NewLine}Обновить сборку перед автоматической настройкой?",
            "Автоматический режим",
            primaryButtonText: "Да",
            secondaryButtonText: "Нет",
            tertiaryButtonText: "Отмена");

        switch (choice)
        {
            case DialogService.DialogChoice.Primary:
                await ApplyUpdateAsync(promptForAutomaticMode: false);
                LastActionText = $"Действие: автоматический режим обновил найденную сборку до {_installation?.Version ?? _updateLatestVersion}";
                return _installation is not null;
            case DialogService.DialogChoice.Secondary:
                LastActionText = $"Действие: автоматический режим использует найденную сборку {currentVersion} без обновления";
                return true;
            default:
                LastActionText = "Действие: автоматический режим отменён пользователем";
                return false;
        }
    }

    private async Task SelectInstallationAsync(ZapretInstallation installation, string actionText, bool checkUpdates = true)
    {
        _installation = installation;
        _updateService.DisableInternalCheckUpdatesForSiblingInstallations(installation.RootPath);
        var restoredUserFilesCount = _preservedUserDataService.RestoreToInstallation(installation);
        _settings.LastInstallationPath = installation.RootPath;
        _settingsService.Save(_settings);
        LastActionText = actionText;
        await RefreshAsync();

        if (restoredUserFilesCount > 0)
        {
            ShowInlineNotification($"В новую сборку автоматически возвращены сохранённые пользовательские файлы: {restoredUserFilesCount}.");
        }

        if (checkUpdates && AutoCheckUpdatesEnabled && _installation is not null)
        {
            await CheckUpdatesAsync(false);
        }
    }

    private static bool PathStartsWith(string? candidatePath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        var fullCandidate = Path.GetFullPath(candidatePath);
        var fullRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<ZapretInstallation> GetInstallationsInSameParent(string rootPath)
    {
        var parentPath = Directory.GetParent(rootPath)?.FullName;
        if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
        {
            var current = _discoveryService.TryLoad(rootPath);
            return current is null ? [] : [current];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ZapretInstallation>();

        void AddInstallation(string candidatePath)
        {
            var installation = _discoveryService.TryLoad(candidatePath);
            if (installation is null || !seen.Add(installation.RootPath))
            {
                return;
            }

            result.Add(installation);
        }

        AddInstallation(rootPath);

        foreach (var candidatePath in Directory.EnumerateDirectories(parentPath))
        {
            AddInstallation(candidatePath);
        }

        return result
            .OrderByDescending(item => string.Equals(item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task StopRelatedInstallationsAsync(string rootPath, bool waitForDrivers, CancellationToken cancellationToken = default)
    {
        var relatedInstallations = GetInstallationsInSameParent(rootPath).ToList();
        foreach (var relatedInstallation in relatedInstallations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _processService.StopAsync(relatedInstallation);
        }

        foreach (var relatedInstallation in relatedInstallations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForProcessExitAsync(relatedInstallation, TimeSpan.FromSeconds(10));
            if (waitForDrivers)
            {
                await WaitForDriverReleaseAsync(relatedInstallation.RootPath, TimeSpan.FromSeconds(20));
            }
        }
    }

    private bool ShouldRestoreSuspendedService(ZapretInstallation installation)
    {
        return _restoreSuspendedServiceAfterStandalone &&
               string.Equals(_suspendedServiceRestoreRootPath, installation.RootPath, StringComparison.OrdinalIgnoreCase);
    }

    private void MarkSuspendedServiceForRestore(ZapretInstallation installation)
    {
        _restoreSuspendedServiceAfterStandalone = true;
        _suspendedServiceRestoreRootPath = installation.RootPath;
    }

    private void ClearSuspendedServiceRestore()
    {
        CancelSuspendedServiceRestoreWatch();
        _restoreSuspendedServiceAfterStandalone = false;
        _suspendedServiceRestoreRootPath = null;
    }

    private void StartSuspendedServiceRestoreWatch(ZapretInstallation installation)
    {
        CancelSuspendedServiceRestoreWatch();

        var cancellation = new CancellationTokenSource();
        _suspendedServiceRestoreWatchCancellation = cancellation;
        _ = WatchSuspendedServiceExitAsync(installation, cancellation.Token);
    }

    private void CancelSuspendedServiceRestoreWatch()
    {
        if (_suspendedServiceRestoreWatchCancellation is null)
        {
            return;
        }

        try
        {
            _suspendedServiceRestoreWatchCancellation.Cancel();
        }
        catch
        {
        }
        finally
        {
            _suspendedServiceRestoreWatchCancellation.Dispose();
            _suspendedServiceRestoreWatchCancellation = null;
        }
    }

    private async Task WatchSuspendedServiceExitAsync(ZapretInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1200, cancellationToken);

                if (!ShouldRestoreSuspendedService(installation))
                {
                    return;
                }

                if (_processService.GetRunningProcessCount(installation) > 0)
                {
                    continue;
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is null)
                {
                    await RestoreSuspendedServiceIfNeededAsync();
                }
                else
                {
                    await dispatcher.InvokeAsync(() => RestoreSuspendedServiceIfNeededAsync()).Task.Unwrap();
                }

                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private async Task<bool> RestoreSuspendedServiceIfNeededAsync()
    {
        if (_installation is null ||
            _isRestoringSuspendedService ||
            !ShouldRestoreSuspendedService(_installation) ||
            _processService.GetRunningProcessCount(_installation) > 0)
        {
            return false;
        }

        var serviceStatus = _serviceManager.GetStatus();
        if (!serviceStatus.IsInstalled)
        {
            ClearSuspendedServiceRestore();
            return false;
        }

        if (serviceStatus.IsRunning)
        {
            ClearSuspendedServiceRestore();
            return false;
        }

        try
        {
            _isRestoringSuspendedService = true;
            RuntimeStatus = "Возвращаем установленную службу...";
            await _serviceManager.StartAsync();
            await Task.Delay(1200);
            ClearSuspendedServiceRestore();
            await RefreshLiveStatusCoreAsync();
            LastActionText = $"Действие: служба снова запущена для {serviceStatus.ProfileName ?? "выбранного профиля"}";
            ShowInlineNotification($"Служба снова запущена: {serviceStatus.ProfileName ?? "профиль не определён"}");
            return true;
        }
        catch (Exception ex)
        {
            ClearSuspendedServiceRestore();
            LastActionText = $"Действие: не удалось вернуть службу - {ex.Message}";
            ShowInlineNotification($"Не удалось вернуть службу: {ex.Message}", isError: true, durationMs: 5200);
            return false;
        }
        finally
        {
            _isRestoringSuspendedService = false;
        }
    }

    private static void DeleteDirectoryTree(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (var directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            File.SetAttributes(directory, FileAttributes.Normal);
        }

        Directory.Delete(rootPath, recursive: true);
    }

    private static async Task WaitForDriverReleaseAsync(string rootPath, TimeSpan timeout)
    {
        var driverPaths = new[]
        {
            Path.Combine(rootPath, "WinDivert64.sys"),
            Path.Combine(rootPath, "WinDivert32.sys"),
            Path.Combine(rootPath, "bin", "WinDivert64.sys"),
            Path.Combine(rootPath, "bin", "WinDivert32.sys")
        }.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (driverPaths.Length == 0)
        {
            return;
        }

        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            var allReleased = true;
            foreach (var driverPath in driverPaths)
            {
                try
                {
                    using var stream = new FileStream(driverPath, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                catch (IOException)
                {
                    allReleased = false;
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    allReleased = false;
                    break;
                }
            }

            if (allReleased)
            {
                return;
            }

            await Task.Delay(1000);
        }
    }

    private async Task WaitForProcessExitAsync(ZapretInstallation installation, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            if (_processService.GetRunningProcessCount(installation) == 0)
            {
                return;
            }

            await Task.Delay(700);
        }
    }

    private async Task WaitForServiceRemovalAsync(TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            var status = _serviceManager.GetStatus();
            if (!status.IsInstalled)
            {
                return;
            }

            await Task.Delay(700);
        }
    }

    private async Task WaitForProbeStopAsync(TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            if (!IsProbeRunning)
            {
                return;
            }

            await Task.Delay(300);
        }
    }

    private static async Task<string?> DeleteInstallationDirectoryAsync(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return null;
        }

        Exception? lastException = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                DeleteDirectoryTree(rootPath);
                return null;
            }
            catch (IOException ex)
            {
                lastException = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
            }

            await Task.Delay(1000);
        }

        var pendingDeletePath = $"{rootPath}.delete-{DateTime.Now:yyyyMMdd-HHmmss}";
        if (Directory.Exists(pendingDeletePath))
        {
            pendingDeletePath = $"{pendingDeletePath}-{Guid.NewGuid():N}";
        }

        try
        {
            Directory.Move(rootPath, pendingDeletePath);
        }
        catch
        {
            StartBackgroundDelete(rootPath);
            return rootPath;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                DeleteDirectoryTree(pendingDeletePath);
                return null;
            }
            catch (IOException ex)
            {
                lastException = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
            }

            await Task.Delay(1000);
        }

        StartBackgroundDelete(pendingDeletePath);
        return pendingDeletePath;
    }

    private static void StartBackgroundDelete(string path)
    {
        try
        {
            var escapedPath = path.Replace("'", "''");
            var script = $"for ($i=0; $i -lt 40; $i++) {{ try {{ Remove-Item -LiteralPath '{escapedPath}' -Recurse -Force -ErrorAction Stop; break }} catch {{ Start-Sleep -Seconds 3 }} }}";
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch
        {
        }
    }

    private static IReadOnlyList<string> BuildDnsArguments(
        string profileKey,
        string? customPrimary,
        string? customSecondary,
        bool useDnsOverHttps,
        string? customDohTemplate,
        string resultFilePath)
    {
        return
        [
            "--set-dns-profile",
            profileKey,
            string.IsNullOrWhiteSpace(customPrimary) ? "__EMPTY__" : customPrimary,
            string.IsNullOrWhiteSpace(customSecondary) ? "__EMPTY__" : customSecondary,
            useDnsOverHttps.ToString(),
            string.IsNullOrWhiteSpace(customDohTemplate) ? "__EMPTY__" : customDohTemplate,
            resultFilePath
        ];
    }

    private static async Task RunElevatedManagerTaskAsync(IEnumerable<string> arguments, string? resultFilePath = null)
    {
        var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Не удалось определить путь к Zapret Manager.");
        }

        processPath = Path.GetFullPath(processPath);
        if (!File.Exists(processPath))
        {
            throw new InvalidOperationException("Файл ZapretManager был перемещён после запуска. Закройте программу и откройте её заново из новой папки.");
        }

        var workingDirectory = Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            throw new InvalidOperationException("Не удалось определить рабочую папку ZapretManager. Закройте программу и откройте её заново.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            Verb = "runas"
        };

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            throw new InvalidOperationException("Файл ZapretManager был перемещён после запуска. Закройте программу и откройте её заново из новой папки.", ex);
        }

        using var elevatedProcess = process
            ?? throw new InvalidOperationException("Не удалось запустить административную операцию.");

        try
        {
            await elevatedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            TryKillProcess(elevatedProcess);
            throw new TimeoutException("Системная операция изменения DNS зависла и была остановлена.");
        }

        string? resultMessage = null;
        if (!string.IsNullOrWhiteSpace(resultFilePath) && File.Exists(resultFilePath))
        {
            resultMessage = await File.ReadAllTextAsync(resultFilePath);
            TryDeleteFile(resultFilePath);
        }

        if (elevatedProcess.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(resultMessage)
                ? $"Системная операция завершилась с кодом {elevatedProcess.ExitCode}."
                : resultMessage.Trim());
        }
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        var needsQuotes = argument.Any(char.IsWhiteSpace) || argument.Contains('"');
        if (!needsQuotes)
        {
            return argument;
        }

        var builder = new System.Text.StringBuilder();
        builder.Append('"');
        var backslashCount = 0;

        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount);
                backslashCount = 0;
            }

            builder.Append(character);
        }

        if (backslashCount > 0)
        {
            builder.Append('\\', backslashCount * 2);
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static string GetManagerVersion()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var productVersion = FileVersionInfo.GetVersionInfo(processPath).ProductVersion;
                if (!string.IsNullOrWhiteSpace(productVersion))
                {
                    return productVersion.Trim();
                }
            }
        }
        catch
        {
        }

        return "1.0";
    }

    private static void EnsureManagerExecutableAvailable()
    {
        var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Не удалось определить путь к ZapretManager.");
        }

        if (!File.Exists(Path.GetFullPath(processPath)))
        {
            throw new InvalidOperationException(ManagerMovedMessage);
        }
    }

    private static bool DirectoryMayRequireElevation()
    {
        var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(processPath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var testFilePath = Path.Combine(directory, $".zapretmanager-write-test-{Guid.NewGuid():N}.tmp");
            using (File.Create(testFilePath, 1, FileOptions.DeleteOnClose))
            {
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static string TrimActionPrefix(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        const string prefix = "Действие:";
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? text[prefix.Length..].TrimStart()
            : text;
    }

    private void BeginProbeProgress(int totalProfiles, bool includeRestoreStep)
    {
        _probeStopwatch.Restart();
        _probeProgressTotalProfiles = totalProfiles;
        _probeProgressCompletedProfiles = 0;
        _probeProgressIncludesRestoreStep = includeRestoreStep;
        _probeCurrentProfileActive = totalProfiles > 0;
        _probeCurrentProfileStartedAtUtc = DateTime.UtcNow;
        _probeInitialProfileEstimate = _connectivityTestService.GetEstimatedProfileProbeDuration();
        _probeLastDisplayedRemaining = null;
        _probeLastEtaUpdatedAtUtc = DateTime.UtcNow;
        BusyProgressIsIndeterminate = false;
        BusyProgressValue = 0;
        RefreshProbeProgressDisplay();
        _probeProgressTimer.Stop();
        _probeProgressTimer.Start();
    }

    private void MarkProbeProfileStarted(int completedProfiles)
    {
        _probeProgressCompletedProfiles = Math.Clamp(completedProfiles, 0, _probeProgressTotalProfiles);
        _probeCurrentProfileActive = _probeProgressCompletedProfiles < _probeProgressTotalProfiles;
        _probeCurrentProfileStartedAtUtc = DateTime.UtcNow;
        RefreshProbeProgressDisplay();
    }

    private void MarkProbeProfileCompleted(int completedProfiles)
    {
        _probeProgressCompletedProfiles = Math.Clamp(completedProfiles, 0, _probeProgressTotalProfiles);
        _probeCurrentProfileActive = false;
        RefreshProbeProgressDisplay();
    }

    private void RefreshProbeProgressDisplay()
    {
        if (_probeProgressTotalProfiles <= 0)
        {
            BusyProgressIsIndeterminate = false;
            BusyProgressValue = 0;
            BusyEtaText = string.Empty;
            return;
        }

        BusyProgressIsIndeterminate = false;
        var averageProfileDuration = _probeInitialProfileEstimate;
        if (_probeProgressCompletedProfiles > 0 && _probeStopwatch.Elapsed > TimeSpan.Zero)
        {
            var observedAverage = TimeSpan.FromTicks(_probeStopwatch.Elapsed.Ticks / _probeProgressCompletedProfiles);
            averageProfileDuration = TimeSpan.FromTicks(Math.Max(_probeInitialProfileEstimate.Ticks, observedAverage.Ticks));
        }

        var partialProgress = 0d;
        if (_probeCurrentProfileActive && _probeProgressCompletedProfiles < _probeProgressTotalProfiles)
        {
            var currentElapsed = DateTime.UtcNow - _probeCurrentProfileStartedAtUtc;
            partialProgress = Math.Clamp(
                currentElapsed.TotalMilliseconds / Math.Max(averageProfileDuration.TotalMilliseconds, 1),
                0d,
                0.98d);
        }

        var effectiveCompleted = Math.Min(_probeProgressTotalProfiles, _probeProgressCompletedProfiles + partialProgress);
        BusyProgressValue = Math.Clamp(effectiveCompleted / _probeProgressTotalProfiles * 100d, 0d, 100d);

        var remainingProfiles = Math.Max(0d, _probeProgressTotalProfiles - effectiveCompleted);
        if (remainingProfiles <= 0d)
        {
            BusyEtaText = _probeProgressIncludesRestoreStep ? "Осталось примерно: меньше 10 сек." : string.Empty;
            return;
        }

        var remaining = TimeSpan.FromTicks((long)(averageProfileDuration.Ticks * remainingProfiles));
        if (_probeProgressIncludesRestoreStep)
        {
            remaining += TimeSpan.FromSeconds(2);
        }

        BusyEtaText = $"Осталось примерно: {FormatBusyDuration(SmoothProbeRemainingEstimate(remaining))}";
    }

    private void ResetBusyProgressState()
    {
        _probeProgressTimer.Stop();
        _probeStopwatch.Reset();
        _probeProgressTotalProfiles = 0;
        _probeProgressCompletedProfiles = 0;
        _probeProgressIncludesRestoreStep = false;
        _probeCurrentProfileActive = false;
        _probeCurrentProfileStartedAtUtc = default;
        _probeInitialProfileEstimate = default;
        _probeLastDisplayedRemaining = null;
        _probeLastEtaUpdatedAtUtc = default;
        BusyProgressIsIndeterminate = true;
        BusyProgressValue = 0;
        BusyEtaText = string.Empty;
    }

    private TimeSpan SmoothProbeRemainingEstimate(TimeSpan remaining)
    {
        var now = DateTime.UtcNow;
        if (_probeLastDisplayedRemaining is null)
        {
            _probeLastDisplayedRemaining = remaining;
            _probeLastEtaUpdatedAtUtc = now;
            return remaining;
        }

        var previous = _probeLastDisplayedRemaining.Value;
        var elapsed = _probeLastEtaUpdatedAtUtc == default ? TimeSpan.Zero : now - _probeLastEtaUpdatedAtUtc;
        _probeLastEtaUpdatedAtUtc = now;

        var nextExpected = previous > TimeSpan.FromSeconds(10)
            ? previous - elapsed
            : previous;
        if (nextExpected < TimeSpan.FromSeconds(10))
        {
            nextExpected = TimeSpan.FromSeconds(10);
        }

        var smoothed = remaining <= nextExpected ? remaining : nextExpected;
        if (smoothed < TimeSpan.FromSeconds(10))
        {
            smoothed = TimeSpan.FromSeconds(10);
        }

        _probeLastDisplayedRemaining = smoothed;
        return smoothed;
    }

    private static string FormatBusyDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.FromSeconds(10))
        {
            return "меньше 10 сек.";
        }

        var rounded = TimeSpan.FromSeconds(Math.Ceiling(duration.TotalSeconds));
        if (rounded.TotalMinutes < 1)
        {
            return $"{(int)rounded.TotalSeconds} сек.";
        }

        var minutes = (int)rounded.TotalMinutes;
        var seconds = rounded.Seconds;
        return seconds == 0
            ? $"{minutes} мин."
            : $"{minutes} мин. {seconds} сек.";
    }

    private void ShowInlineNotification(string message, bool isError = false, int durationMs = 4200)
    {
        _notificationCancellation?.Cancel();
        _notificationCancellation?.Dispose();
        _notificationCancellation = new CancellationTokenSource();
        var token = _notificationCancellation.Token;

        InlineNotificationText = message;
        IsInlineNotificationError = isError;
        IsInlineNotificationVisible = true;

        _ = HideInlineNotificationAsync(durationMs, token);
    }

    private async Task HideInlineNotificationAsync(int durationMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(durationMs, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            IsInlineNotificationVisible = false;
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void SynchronizeStartupRegistration()
    {
        try
        {
            var shouldEnableStartup = _settings.StartWithWindowsEnabled;
            _startWithWindowsEnabled = shouldEnableStartup;
            _settings.StartWithWindowsEnabled = shouldEnableStartup;
            _startupRegistrationService.SetEnabled(shouldEnableStartup);
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            _startWithWindowsEnabled = false;
            _settings.StartWithWindowsEnabled = false;
            _settingsService.Save(_settings);
            var shortError = DialogService.GetShortDisplayMessage(ex, "не удалось настроить автозапуск");
            _lastActionText = $"Действие: автозапуск не настроен - {shortError}";
        }
    }
}
