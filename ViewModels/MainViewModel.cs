using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
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
    private readonly DpiTcpFreezeTestService _dpiTcpFreezeTestService = new();
    private readonly ManagerUpdateService _managerUpdateService = new();
    private readonly ProgramRemovalService _programRemovalService = new();
    private readonly TgWsProxyService _tgWsProxyService = new();
    private readonly AppSettings _settings;
    private readonly Dictionary<string, ConfigProbeResult> _probeResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TargetGroupDefinition> _builtInTargetGroups = CreateBuiltInTargetGroups()
        .ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
    private readonly string _managerVersion = GetManagerVersion();
    private bool _isSyncingEmbeddedNetworkSettings;

    private ZapretInstallation? _installation;
    private bool _isRebuildingRows;
    private ConfigProfile? _selectedConfig;
    private ConfigTableRow? _selectedConfigRow;
    private readonly HashSet<string> _selectedConfigPaths = new(StringComparer.OrdinalIgnoreCase);
    private GameModeOption? _selectedGameMode;
    private DnsService.DnsProfileDefinition? _selectedDnsProfile;
    private GameModeOption? _selectedIpSetMode;
    private CancellationTokenSource? _probeCancellation;
    private string _installationPath = "Папка zapret ещё не выбрана";
    private string _windowTitle = "ZapretManager";
    private string _versionText = "Версия: неизвестно";
    private string _runtimeStatus = "Проверяем состояние winws.exe...";
    private string _serviceStatus = "Проверяем состояние службы...";
    private string _updateStatus = "Обновления: не проверялись";
    private string _gameModeStatus = "Игровой режим: не определён";
    private string _managerUpdateStatus = "Обновления программы: не проверялись";
    private string _lastActionText = "Действие: ожидание";
    private string _busyEtaText = string.Empty;
    private string _manualTarget = string.Empty;
    private string _recommendedConfigText = "Рекомендуемый конфиг: появится после проверки";
    private string _selectedSummaryText = "Подробности появятся после проверки.";
    private string _defaultTargetsHint = "Пресеты: YouTube, Discord, Cloudflare или все домены из targets.txt.";
    private string _selectedTargetsDisplayText = "Все домены из targets.txt";
    private string _dnsPrimaryAddress = string.Empty;
    private string _dnsSecondaryAddress = string.Empty;
    private string _dnsDohUrl = string.Empty;
    private string _tgWsProxyStatus = "TG WS Proxy: не установлен";
    private string _tgWsProxyVersionStatus = "Версия TG WS Proxy: не установлена";
    private string _tgWsProxyUpdateStatus = "Обновления TG WS Proxy: не проверялись";
    private string _tgWsProxyHost = "127.0.0.1";
    private string _tgWsProxyPort = "1443";
    private string _tgWsProxySecret = string.Empty;
    private string _tgWsProxyDcMappings = "2:149.154.167.220" + Environment.NewLine + "4:149.154.167.220";
    private string _tgWsProxyCfProxyDomain = string.Empty;
    private string _tgWsProxyBufferKilobytes = "256";
    private string _tgWsProxyPoolSize = "4";
    private string _tgWsProxyLogMaxMegabytes = "5";
    private string _inlineNotificationText = string.Empty;
    private bool _isInlineNotificationVisible;
    private bool _isInlineNotificationError;
    private bool _dnsUseDohEnabled;
    private bool _tgWsProxyCfProxyEnabled = true;
    private bool _tgWsProxyCfProxyPriorityEnabled = true;
    private bool _tgWsProxyCustomDomainEnabled;
    private bool _tgWsProxyVerboseLoggingEnabled;
    private bool _tgWsProxyAutoStartEnabled;
    private bool _tgWsProxyAutoCheckUpdatesEnabled;
    private bool _isTgWsProxyCfProxyTestRunning;
    private bool _isBusy;
    private bool _suppressBusyOverlay;
    private bool _isProbeRunning;
    private bool _hasUpdate;
    private bool _hasPreviousVersion;
    private bool _isTgWsProxyInstalled;
    private bool _isTgWsProxyRunning;
    private bool _tgWsProxyHasUpdate;
    private bool _hasManagerUpdate;
    private bool _autoCheckUpdatesEnabled;
    private bool _startWithWindowsEnabled;
    private bool _closeToTrayEnabled;
    private bool _minimizeToTrayEnabled;
    private bool _useLightThemeEnabled;
    private GameModeOption? _selectedApplicationThemeMode;
    private GameModeOption? _selectedUpdateCheckIntervalOption;
    private bool _autoUpdateIpSetOnStartupEnabled;
    private string? _updateDownloadUrl;
    private string? _updateLatestVersion;
    private string? _tgWsProxyDownloadUrl;
    private string? _tgWsProxyLatestVersion;
    private string? _tgWsProxyAssetFileName;
    private string? _resolvedTgWsProxyExecutablePath;
    private string _probeButtonText = "Проверить все";
    private double _busyProgressValue;
    private bool _busyProgressIsIndeterminate = true;
    private CancellationTokenSource? _notificationCancellation;
    private bool _restoreSuspendedServiceAfterStandalone;
    private string? _suspendedServiceRestoreRootPath;
    private bool _isRestoringSuspendedService;
    private CancellationTokenSource? _suspendedServiceRestoreWatchCancellation;
    private bool _managerUpdatePromptShownThisSession;
    private bool _managerUpdateLaunchRequested;
    private ManagerUpdateInfo? _pendingStartupManagerUpdateInfo;
    private ManagerUpdateInfo? _lastKnownManagerUpdateInfo;
    private UpdateInfo? _pendingStartupZapretUpdateInfo;
    private bool _startupUpdateNotificationShownThisSession;
    private bool _startupUpdatePromptHandled;
    private bool _isPresentingStartupUpdatePrompts;
    private bool _tgWsProxyUpdateNotificationShownThisSession;
    private readonly Stopwatch _probeStopwatch = new();
    private readonly DispatcherTimer _probeProgressTimer;
    private readonly DispatcherTimer _scheduledUpdateTimer;
    private bool _isScheduledUpdateCheckRunning;
    private static readonly TimeSpan MinProbeProfileEstimate = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MaxProbeProfileEstimate = TimeSpan.FromSeconds(45);
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
    private TcpFreezeWindow? _tcpFreezeWindow;
    private readonly HashSet<Window> _openAuxiliaryWindows = [];
    private const string ManagerMovedMessage = "Файл ZapretManager был перемещён после запуска. Закройте программу через трей или Диспетчер задач и откройте её заново из новой папки.";

    public MainViewModel()
    {
        _probeProgressTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _probeProgressTimer.Tick += (_, _) => RefreshProbeProgressDisplay();
        _scheduledUpdateTimer = new DispatcherTimer(DispatcherPriority.Background);
        _scheduledUpdateTimer.Tick += async (_, _) => await ScheduledUpdateTimer_TickAsync();

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
        _settings.ApplicationThemeMode = ApplicationThemeService.NormalizeThemeMode(_settings.ApplicationThemeMode);
        _settings.UpdateCheckIntervalValue = NormalizeUpdateCheckIntervalValue(_settings.UpdateCheckIntervalValue);
        _autoCheckUpdatesEnabled = _settings.AutoCheckUpdatesEnabled;
        _closeToTrayEnabled = _settings.CloseToTrayEnabled;
        _minimizeToTrayEnabled = _settings.MinimizeToTrayEnabled;
        _useLightThemeEnabled = ApplicationThemeService.ResolveUseLightTheme(_settings.ApplicationThemeMode, _settings.UseLightTheme);
        _settings.UseLightTheme = _useLightThemeEnabled;
        _tgWsProxyAutoCheckUpdatesEnabled = _settings.TgWsProxyAutoCheckUpdatesEnabled;
        _autoUpdateIpSetOnStartupEnabled = _settings.AutoUpdateIpSetOnStartupEnabled;
        SynchronizeStartupRegistration();

        Configs = new ObservableCollection<ConfigProfile>();
        ConfigRows = new ObservableCollection<ConfigTableRow>();
        DnsProfileOptions = new ObservableCollection<DnsService.DnsProfileDefinition>();
        GameModeOptions = new ObservableCollection<GameModeOption>
        {
            new("disabled", "Выключен"),
            new("all", "TCP + UDP (обычно)"),
            new("tcp", "Только TCP"),
            new("udp", "Только UDP")
        };
        ApplicationThemeOptions = new ObservableCollection<GameModeOption>
        {
            new(ApplicationThemeService.SystemMode, "Как в системе"),
            new(ApplicationThemeService.LightMode, "Светлая"),
            new(ApplicationThemeService.DarkMode, "Тёмная")
        };
        UpdateCheckIntervalOptions = new ObservableCollection<GameModeOption>
        {
            new("1h", "Каждый час"),
            new("2h", "Каждые 2 часа"),
            new("6h", "Каждые 6 часов"),
            new("12h", "Каждые 12 часов"),
            new("24h", "Раз в сутки")
        };
        IpSetModeOptions = new ObservableCollection<GameModeOption>
        {
            new("loaded", "По списку"),
            new("none", "Выключен"),
            new("any", "Все IP")
        };

        _selectedApplicationThemeMode = ApplicationThemeOptions.FirstOrDefault(option =>
            string.Equals(option.Value, _settings.ApplicationThemeMode, StringComparison.OrdinalIgnoreCase))
            ?? ApplicationThemeOptions[0];
        _selectedUpdateCheckIntervalOption = UpdateCheckIntervalOptions.FirstOrDefault(option =>
            string.Equals(option.Value, _settings.UpdateCheckIntervalValue, StringComparison.OrdinalIgnoreCase))
            ?? UpdateCheckIntervalOptions[1];
        ConfigureScheduledUpdateChecks();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && !IsProbeRunning);
        QuickSearchCommand = new AsyncRelayCommand(QuickSearchAsync, () => !IsBusy && !IsProbeRunning);
        BrowseCommand = new AsyncRelayCommand(BrowseFolderAsync, () => !IsBusy && !IsProbeRunning);
        DownloadZapretCommand = new AsyncRelayCommand(DownloadZapretAsync, () => !IsBusy && !IsProbeRunning);
        DeleteZapretCommand = new AsyncRelayCommand(DeleteZapretAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => _installation is not null && !IsBusy);
        HandleZapretInstallOrUpdateCommand = new AsyncRelayCommand(HandleZapretInstallOrUpdateAsync, () => !IsBusy && !IsProbeRunning);
        CheckInstalledComponentUpdatesCommand = new AsyncRelayCommand(CheckInstalledComponentUpdatesAsync, () => !IsBusy && !IsProbeRunning);
        HomeCheckAllCommand = new AsyncRelayCommand(RunHomeCheckAllAsync, () => !IsBusy && !IsProbeRunning);
        OpenTargetsFileCommand = new AsyncRelayCommand(OpenTargetsEditorAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenIncludedDomainsEditorCommand = new AsyncRelayCommand(OpenIncludedDomainsEditorAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenExcludedDomainsEditorCommand = new AsyncRelayCommand(OpenExcludedDomainsEditorAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenHostsEditorCommand = new AsyncRelayCommand(OpenHostsEditorAsync, () => !IsBusy && !IsProbeRunning);
        OpenUserSubnetsEditorCommand = new AsyncRelayCommand(OpenUserSubnetsEditorAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenHiddenConfigsCommand = new AsyncRelayCommand(OpenHiddenConfigsWindowAsync, () => _installation is not null && !IsBusy);
        OpenIpSetModeCommand = new AsyncRelayCommand(OpenIpSetModeWindowAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenDnsSettingsCommand = new AsyncRelayCommand(OpenDnsSettingsAsync, () => !IsBusy && !IsProbeRunning);
        OpenDiagnosticsCommand = new AsyncRelayCommand(OpenDiagnosticsAsync, () => !IsBusy && !IsProbeRunning);
        OpenTcpFreezeToolCommand = new AsyncRelayCommand(OpenTcpFreezeToolAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        OpenAboutCommand = new AsyncRelayCommand(OpenAboutWindowAsync, () => !IsBusy);
        SaveTgWsProxyConfigCommand = new AsyncRelayCommand(SaveTgWsProxyConfigAsync, () => !IsBusy && !IsProbeRunning);
        CheckTgWsProxyUpdateCommand = new AsyncRelayCommand(HandleTgWsProxyReleaseActionAsync, () => !IsBusy && !IsProbeRunning);
        InstallTgWsProxyCommand = new AsyncRelayCommand(InstallTgWsProxyAsync, () => !IsBusy && !IsProbeRunning);
        ResetTgWsProxyEditorCommand = new RelayCommand(LoadTgWsProxyEditor, () => !IsBusy && !IsProbeRunning);
        AddTgWsProxyToTelegramCommand = new AsyncRelayCommand(AddTgWsProxyToTelegramAsync, () => !IsBusy && !IsProbeRunning);
        TestTgWsProxyCfProxyCommand = new AsyncRelayCommand(TestTgWsProxyCfProxyAsync, () => IsTgWsProxyInstalled && !_isTgWsProxyCfProxyTestRunning && !IsBusy && !IsProbeRunning);
        UpdateIpSetListCommand = new AsyncRelayCommand(UpdateIpSetListAsync, () => _installation is not null && !IsBusy && !IsProbeRunning);
        UpdateHostsFileCommand = new AsyncRelayCommand(UpdateHostsFileAsync, () => !IsBusy && !IsProbeRunning);
        ClearDiscordCacheCommand = new AsyncRelayCommand(ClearDiscordCacheAsync, () => !IsBusy && !IsProbeRunning);
        GenerateTgWsProxySecretCommand = new RelayCommand(GenerateTgWsProxySecret, () => !IsBusy && !IsProbeRunning);
        CopyTgWsProxyLinkCommand = new RelayCommand(CopyTgWsProxyLink, () => !IsBusy && !IsProbeRunning);
        OpenTgWsProxyLinkCommand = new RelayCommand(OpenTgWsProxyLink, () => !IsBusy && !IsProbeRunning);
        LaunchTgWsProxyCommand = new RelayCommand(LaunchOrRestartTgWsProxy, () => IsTgWsProxyInstalled && !IsBusy && !IsProbeRunning);
        StopTgWsProxyCommand = new RelayCommand(StopTgWsProxy, () => IsTgWsProxyRunning && !IsBusy && !IsProbeRunning);
        ToggleTgWsProxyAutoStartCommand = new AsyncRelayCommand(ToggleTgWsProxyAutoStartAsync, () => !IsBusy && !IsProbeRunning);
        OpenTgWsProxyLogsCommand = new RelayCommand(OpenTgWsProxyLogs, () => !IsBusy);
        OpenTgWsProxyFolderCommand = new RelayCommand(OpenTgWsProxyFolder, () => !IsBusy);
        DeleteTgWsProxyCommand = new AsyncRelayCommand(DeleteTgWsProxyAsync, CanDeleteTgWsProxy);
        OpenTgWsProxyReleasePageCommand = new RelayCommand(OpenTgWsProxyReleasePage, () => !IsBusy);
        OpenTgWsProxyGuideCommand = new RelayCommand(OpenTgWsProxyGuide, () => !IsBusy);
        HandleTgWsProxyInstallOrUpdateCommand = new AsyncRelayCommand(HandleTgWsProxyInstallOrUpdateAsync, () => !IsBusy && !IsProbeRunning);
        StartCommand = new AsyncRelayCommand(StartSelectedAsync, () => _installation is not null && SelectedConfig is not null && !IsBusy);
        StopCommand = new AsyncRelayCommand(StopAsync, CanStopCurrentRuntime);
        HideSelectedConfigCommand = new RelayCommand(HideSelectedConfig, () => _installation is not null && GetSelectedProfilesForHide().Count > 0 && !IsBusy && !IsProbeRunning);
        AutoInstallCommand = new AsyncRelayCommand(RunAutomaticInstallAsync, () => !IsBusy && !IsProbeRunning);
        AutoConfigureTgWsProxyCommand = new AsyncRelayCommand(RunAutomaticTgWsProxyModeAsync, () => !IsBusy && !IsProbeRunning);
        InstallServiceCommand = new AsyncRelayCommand(InstallServiceAsync, () => _installation is not null && SelectedConfig is not null && !IsBusy && !IsProbeRunning && !HomeServiceIsRunning);
        RemoveServiceCommand = new AsyncRelayCommand(RemoveServiceAsync, () => _installation is not null && !IsBusy && !IsProbeRunning && HomeServiceIsInstalled);
        ToggleSelectedServiceCommand = new AsyncRelayCommand(ToggleSelectedServiceAsync, () => _installation is not null && SelectedConfig is not null && !IsBusy);
        RunTestsCommand = new RelayCommand(ToggleProbe, () => _installation is not null && ConfigRows.Count > 0 && (!IsBusy || IsProbeRunning));
        RunSelectedTestCommand = new RelayCommand(ToggleSelectedProbe, () => _installation is not null && GetSelectedProfilesForProbe().Count > 0 && !IsBusy && !IsProbeRunning);
        CheckUpdatesCommand = new AsyncRelayCommand(() => CheckUpdatesAsync(true, true), () => _installation is not null && !IsBusy);
        ApplyUpdateCommand = new AsyncRelayCommand(() => ApplyUpdateAsync(), () => _installation is not null && HasUpdate && !IsBusy);
        RestorePreviousVersionCommand = new AsyncRelayCommand(RestorePreviousVersionAsync, () => _installation is not null && HasPreviousVersion && !IsBusy && !IsProbeRunning);
        CheckManagerUpdateCommand = new AsyncRelayCommand(CheckManagerUpdateAsync, () => !IsBusy && !IsProbeRunning);
        UninstallProgramCommand = new AsyncRelayCommand(UninstallProgramAsync, () => !IsBusy && !IsProbeRunning);
        HandleManagerInstallOrUpdateCommand = new AsyncRelayCommand(HandleManagerInstallOrUpdateAsync, () => !IsBusy && !IsProbeRunning);
        OpenManagerFolderCommand = new RelayCommand(OpenManagerFolder, () => !IsBusy);
        ApplyGameModeCommand = new AsyncRelayCommand(ApplyGameModeAsync, () => _installation is not null && SelectedGameMode is not null && !IsBusy);
        ApplyDnsSettingsCommand = new AsyncRelayCommand(ApplyEmbeddedDnsSettingsAsync, () => !IsBusy && !IsProbeRunning && SelectedDnsProfile is not null);
        ApplyIpSetModeCommand = new AsyncRelayCommand(ApplyEmbeddedIpSetModeAsync, () => _installation is not null && !IsBusy && !IsProbeRunning && SelectedIpSetMode is not null);
        UseDefaultTargetsCommand = new RelayCommand(UseDefaultTargets, () => !IsBusy);
        UseYouTubePresetCommand = new RelayCommand(() => UseTargetGroupPreset("youtube"), () => !IsBusy);
        UseDiscordPresetCommand = new RelayCommand(() => UseTargetGroupPreset("discord"), () => !IsBusy);
        UseCloudflarePresetCommand = new RelayCommand(() => UseTargetGroupPreset("cloudflare"), () => !IsBusy);

        RefreshSelectedTargetsDisplay();
        SyncEmbeddedNetworkSettings();
        LoadTgWsProxyEditor();
    }

    public ObservableCollection<ConfigProfile> Configs { get; }
    public ObservableCollection<ConfigTableRow> ConfigRows { get; }
    public ObservableCollection<DnsService.DnsProfileDefinition> DnsProfileOptions { get; }
    public ObservableCollection<GameModeOption> GameModeOptions { get; }
    public ObservableCollection<GameModeOption> ApplicationThemeOptions { get; }
    public ObservableCollection<GameModeOption> UpdateCheckIntervalOptions { get; }
    public ObservableCollection<GameModeOption> IpSetModeOptions { get; }

    public AsyncRelayCommand BrowseCommand { get; }
    public AsyncRelayCommand DownloadZapretCommand { get; }
    public AsyncRelayCommand DeleteZapretCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public AsyncRelayCommand HandleZapretInstallOrUpdateCommand { get; }
    public AsyncRelayCommand CheckInstalledComponentUpdatesCommand { get; }
    public AsyncRelayCommand HomeCheckAllCommand { get; }
    public AsyncRelayCommand OpenTargetsFileCommand { get; }
    public AsyncRelayCommand OpenIncludedDomainsEditorCommand { get; }
    public AsyncRelayCommand OpenExcludedDomainsEditorCommand { get; }
    public AsyncRelayCommand OpenHostsEditorCommand { get; }
    public AsyncRelayCommand OpenUserSubnetsEditorCommand { get; }
    public AsyncRelayCommand OpenHiddenConfigsCommand { get; }
    public AsyncRelayCommand OpenIpSetModeCommand { get; }
    public AsyncRelayCommand OpenDnsSettingsCommand { get; }
    public AsyncRelayCommand OpenDiagnosticsCommand { get; }
    public AsyncRelayCommand OpenTcpFreezeToolCommand { get; }
    public AsyncRelayCommand OpenAboutCommand { get; }
    public AsyncRelayCommand SaveTgWsProxyConfigCommand { get; }
    public AsyncRelayCommand CheckTgWsProxyUpdateCommand { get; }
    public AsyncRelayCommand InstallTgWsProxyCommand { get; }
    public RelayCommand ResetTgWsProxyEditorCommand { get; }
    public AsyncRelayCommand AddTgWsProxyToTelegramCommand { get; }
    public AsyncRelayCommand TestTgWsProxyCfProxyCommand { get; }
    public AsyncRelayCommand UpdateIpSetListCommand { get; }
    public AsyncRelayCommand UpdateHostsFileCommand { get; }
    public AsyncRelayCommand ClearDiscordCacheCommand { get; }
    public RelayCommand GenerateTgWsProxySecretCommand { get; }
    public RelayCommand CopyTgWsProxyLinkCommand { get; }
    public RelayCommand OpenTgWsProxyLinkCommand { get; }
    public RelayCommand LaunchTgWsProxyCommand { get; }
    public RelayCommand StopTgWsProxyCommand { get; }
    public AsyncRelayCommand ToggleTgWsProxyAutoStartCommand { get; }
    public RelayCommand OpenTgWsProxyLogsCommand { get; }
    public RelayCommand OpenTgWsProxyFolderCommand { get; }
    public AsyncRelayCommand DeleteTgWsProxyCommand { get; }
    public RelayCommand OpenTgWsProxyReleasePageCommand { get; }
    public RelayCommand OpenTgWsProxyGuideCommand { get; }
    public AsyncRelayCommand HandleTgWsProxyInstallOrUpdateCommand { get; }
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
    public AsyncRelayCommand AutoConfigureTgWsProxyCommand { get; }
    public AsyncRelayCommand InstallServiceCommand { get; }
    public AsyncRelayCommand RemoveServiceCommand { get; }
    public AsyncRelayCommand ToggleSelectedServiceCommand { get; }
    public RelayCommand RunTestsCommand { get; }
    public RelayCommand RunSelectedTestCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }
    public AsyncRelayCommand ApplyUpdateCommand { get; }
    public AsyncRelayCommand RestorePreviousVersionCommand { get; }
    public AsyncRelayCommand CheckManagerUpdateCommand { get; }
    public AsyncRelayCommand UninstallProgramCommand { get; }
    public AsyncRelayCommand HandleManagerInstallOrUpdateCommand { get; }
    public RelayCommand OpenManagerFolderCommand { get; }
    public AsyncRelayCommand ApplyGameModeCommand { get; }
    public AsyncRelayCommand ApplyDnsSettingsCommand { get; }
    public AsyncRelayCommand ApplyIpSetModeCommand { get; }

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
    public string ManagerInstallationPath => GetManagerInstallationDirectory();
    public string TgWsProxyInstallationPath => !string.IsNullOrWhiteSpace(_resolvedTgWsProxyExecutablePath)
        ? Path.GetDirectoryName(_resolvedTgWsProxyExecutablePath) ?? _resolvedTgWsProxyExecutablePath
        : string.Empty;
    public bool IsZapretInstalled => _installation is not null;
    public string ZapretInstalledVersionValue => FormatZapretVersionLabel(_installation?.Version);
    public string TgWsProxyInstalledVersionValue => IsTgWsProxyInstalled
        ? FormatTgWsProxyVersionLabel(_tgWsProxyService.GetInstalledVersion(_resolvedTgWsProxyExecutablePath), prefixWithV: true)
        : string.Empty;
    public string ManagerInstalledVersionValue => ManagerVersionLabel;
    public Visibility ZapretPrimaryActionVisibility => !IsZapretInstalled || HasUpdate ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TgWsProxyPrimaryActionVisibility => !IsTgWsProxyInstalled || TgWsProxyHasUpdate ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ManagerPrimaryActionVisibility => HasManagerUpdate ? Visibility.Visible : Visibility.Collapsed;
    public string ZapretUpdateBadgeText => !IsZapretInstalled
        ? "Не установлено"
        : HasUpdate
            ? "Есть обновление"
            : HasCheckedZapretUpdates
                ? "Обновлено"
                : "Не проверено";
    public string TgWsProxyUpdateBadgeText => !IsTgWsProxyInstalled
        ? "Не установлено"
        : TgWsProxyHasUpdate
            ? "Есть обновление"
            : HasCheckedTgWsProxyUpdates
                ? "Обновлено"
                : "Не проверено";
    public string ManagerUpdateBadgeText => HasManagerUpdate
        ? "Есть обновление"
        : HasCheckedManagerUpdates
            ? "Обновлено"
            : "Не проверено";
    public string ZapretUpdateBadgeState => !IsZapretInstalled
        ? "missing"
        : HasUpdate
            ? "available"
            : HasCheckedZapretUpdates
                ? "ok"
                : "unchecked";
    public string TgWsProxyUpdateBadgeState => !IsTgWsProxyInstalled
        ? "missing"
        : TgWsProxyHasUpdate
            ? "available"
            : HasCheckedTgWsProxyUpdates
                ? "ok"
                : "unchecked";
    public string ManagerUpdateBadgeState => HasManagerUpdate
        ? "available"
        : HasCheckedManagerUpdates
            ? "ok"
            : "unchecked";
    public bool HasCheckedZapretUpdates => !string.IsNullOrWhiteSpace(_updateLatestVersion);
    public bool HasCheckedTgWsProxyUpdates => !string.IsNullOrWhiteSpace(_tgWsProxyLatestVersion);
    public bool HasCheckedManagerUpdates => _lastKnownManagerUpdateInfo is not null;
    public string HomeOverallStatusState
    {
        get
        {
            if (!IsZapretInstalled)
            {
                return "missing";
            }

            if (IsBusy || IsProbeRunning)
            {
                return "available";
            }

            if (!HasAnyHomeProbeResults)
            {
                return "available";
            }

            return HomeHasPartialRecommendation ? "warning" : "ok";
        }
    }

    public string HomeOverallStatusIcon => HomeOverallStatusState switch
    {
        "missing" => "!",
        "warning" => "!",
        "available" => "↻",
        _ => "✓"
    };

    public string HomeOverallStatusTitle
    {
        get
        {
            if (!IsZapretInstalled)
            {
                return "Сначала подключите zapret";
            }

            if (IsBusy || IsProbeRunning)
            {
                return "Проверяем и подготавливаем";
            }

            if (!HasAnyHomeProbeResults)
            {
                return "Нужно проверить конфиги";
            }

            return HomeHasPartialRecommendation
                ? "Требуется уточнить конфиг"
                : "Готово к работе";
        }
    }

    public string HomeOverallStatusDescription
    {
        get
        {
            if (!IsZapretInstalled)
            {
                return "Сборка zapret не подключена. Сначала установите её, затем проверьте конфиги.";
            }

            if (IsBusy || IsProbeRunning)
            {
                return BusyActionText;
            }

            if (!HasAnyHomeProbeResults)
            {
                return "Все основные компоненты найдены. Следующий шаг — проверить конфиги.";
            }

            if (HomeHasPartialRecommendation)
            {
                return "Компоненты готовы, но лучший конфиг дал частичный результат. Лучше перепроверить детали.";
            }

            return "Все ключевые компоненты найдены. Можно запускать рекомендуемый профиль или быстрые режимы.";
        }
    }

    public string HomeZapretBadgeText => IsZapretInstalled ? "Zapret: Установлен" : "Zapret: Не найден";
    public string HomeZapretBadgeState => IsZapretInstalled ? "ok" : "missing";

    public string HomeServiceBadgeText
    {
        get
        {
            return HomeServiceIsRunning
                ? "Служба: Активна"
                : HomeServiceIsInstalled
                    ? "Служба: Установлена"
                    : "Служба: Не установлена";
        }
    }

    public string HomeServiceBadgeState
    {
        get
        {
            return HomeServiceIsRunning
                ? "ok"
                : HomeServiceIsInstalled
                    ? "neutral"
                    : "available";
        }
    }

    public string HomeTelegramBadgeText => IsTgWsProxyInstalled
        ? IsTgWsProxyRunning
            ? "TG WS Proxy: Запущен"
            : "TG WS Proxy: Установлен"
        : "TG WS Proxy: Не установлен";

    public string HomeTelegramBadgeState => IsTgWsProxyInstalled
        ? IsTgWsProxyRunning
            ? "ok"
            : "neutral"
        : "available";

    public string HomeUpdatesBadgeText => HomeHasAvailableUpdates
        ? "Обновления: Есть"
        : HasCheckedAnyInstalledComponentUpdates
            ? "Обновления: Нет"
            : "Обновления: Не проверены";

    public string HomeUpdatesBadgeState => HomeHasAvailableUpdates
        ? "available"
        : HasCheckedAnyInstalledComponentUpdates
            ? "ok"
            : "neutral";

    public string HomeRecommendedActionKind
    {
        get
        {
            if (!IsZapretInstalled)
            {
                return "install-zapret";
            }

            if (IsBusy || IsProbeRunning)
            {
                return "busy";
            }

            if (!HasAnyHomeProbeResults)
            {
                return "test-configs";
            }

            return HomeHasPartialRecommendation
                ? "review-configs"
                : "start-recommended";
        }
    }

    public string HomeRecommendedActionTitle
    {
        get
        {
            var recommended = GetRecommendedResult();
            return HomeRecommendedActionKind switch
            {
                "install-zapret" => "Нужно подключить zapret",
                "busy" => "Сейчас выполняется операция",
                "test-configs" => "Сначала проверьте конфиги",
                "review-configs" => $"Лучший результат: {recommended?.ConfigName ?? "нужна ручная проверка"}",
                "start-recommended" => $"Рекомендуемый конфиг: {recommended?.ConfigName ?? SelectedConfig?.Name ?? "не определён"}",
                _ => "Рекомендуемое действие"
            };
        }
    }

    public string HomeRecommendedActionDescription
    {
        get
        {
            var recommended = GetRecommendedResult();
            return HomeRecommendedActionKind switch
            {
                "install-zapret" => "Без рабочей сборки zapret нельзя запустить профиль и установить службу.",
                "busy" => "Дождитесь завершения текущей операции. После этого рекомендации обновятся автоматически.",
                "test-configs" => "Конфиги ещё не проверялись. Сначала запустите проверку.",
                "review-configs" => recommended is null
                    ? "Лучший результат ещё не определён. Откройте вкладку конфигов и проверьте доступность вручную."
                    : $"Лучший найденный вариант подходит частично: {FormatPrimaryCoverage(recommended)}. Лучше открыть результаты вручную.",
                "start-recommended" => "Оптимальный вариант для большинства сценариев. Можно запускать сразу.",
                _ => "Подберите рабочий сценарий запуска."
            };
        }
    }

    public string HomeRecommendedActionButtonText => HomeRecommendedActionKind switch
    {
        "install-zapret" => "Установить zapret",
        "busy" => "Проверка выполняется",
        "test-configs" => "Проверить конфиги",
        "review-configs" => "Открыть конфиги",
        "start-recommended" => "Запустить рекомендуемый",
        _ => "Продолжить"
    };

    public string HomeRecommendedActionButtonIcon => HomeRecommendedActionKind switch
    {
        "install-zapret" => "⤓",
        "busy" => "…",
        "test-configs" => "↻",
        "review-configs" => "☷",
        "start-recommended" => "▶",
        _ => "•"
    };

    public bool HomeRecommendedActionEnabled => HomeRecommendedActionKind switch
    {
        "install-zapret" => HandleZapretInstallOrUpdateCommand.CanExecute(null),
        "busy" => false,
        "test-configs" => RunTestsCommand.CanExecute(null),
        "review-configs" => ConfigRows.Count > 0,
        "start-recommended" => StartCommand.CanExecute(null),
        _ => false
    };

    public string HomeLastActionText => BusyActionText;

    public Visibility HomeLastActionVisibility
    {
        get
        {
            if (string.IsNullOrWhiteSpace(BusyActionText))
            {
                return Visibility.Collapsed;
            }

            return BusyActionText.Contains("включена тёмная тема", StringComparison.OrdinalIgnoreCase) ||
                   BusyActionText.Contains("включена светлая тема", StringComparison.OrdinalIgnoreCase) ||
                   BusyActionText.Contains("ожидание", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    public string HomeConfigCardTitle => SelectedConfig?.Name
        ?? GetRecommendedResult()?.ConfigName
        ?? "Конфиг не выбран";

    public string HomeConfigCardDescription => HasAnyHomeProbeResults
        ? SelectedSummaryText
        : IsZapretInstalled
            ? "Сначала проверьте конфиги."
            : "Сначала подключите zapret.";

    public string HomeServiceCardTitle
    {
        get
        {
            return HomeServiceIsInstalled
                ? (GetHomeServiceProfileName() ?? "Профиль не определён")
                : "Служба не установлена";
        }
    }

    public string HomeServiceCardDescription
    {
        get
        {
            if (HomeServiceIsRunning)
            {
                return "Служба установлена и активна";
            }

            return HomeServiceIsInstalled
                ? "Служба установлена, но не запущена"
                : "Профиль работает только вручную";
        }
    }

    public string HomeServiceCardState
    {
        get
        {
            return HomeServiceIsRunning
                ? "ok"
                : HomeServiceIsInstalled
                    ? "available"
                    : "neutral";
        }
    }

    public string HomeTelegramCardTitle => IsTgWsProxyInstalled
        ? IsTgWsProxyRunning
            ? "TG WS Proxy запущен"
            : "TG WS Proxy установлен"
        : "TG WS Proxy не установлен";

    public string HomeTelegramCardDescription => IsTgWsProxyInstalled
        ? IsTgWsProxyRunning
            ? "Подключение активно"
            : "Компонент установлен и готов к запуску"
        : "Установите компонент, если нужен Telegram-режим.";

    public string HomeTelegramCardState => IsTgWsProxyInstalled
        ? IsTgWsProxyRunning
            ? "ok"
            : "available"
        : "neutral";

    public string HomeAttentionState
    {
        get
        {
            if (!IsZapretInstalled)
            {
                return "missing";
            }

            if (!HasAnyHomeProbeResults)
            {
                return "available";
            }

            if (HomeHasPartialRecommendation)
            {
                return "warning";
            }

            return HomeHasAvailableUpdates ? "available" : "ok";
        }
    }

    public string HomeAttentionTitle
    {
        get
        {
            if (!IsZapretInstalled)
            {
                return "Нужно подключить рабочую сборку zapret.";
            }

            if (!HasAnyHomeProbeResults)
            {
                return "Конфиги ещё не проверялись.";
            }

            if (HomeHasPartialRecommendation)
            {
                return "Лучший найденный конфиг подходит только частично.";
            }

            return HomeHasAvailableUpdates
                ? "Доступны обновления компонентов."
                : "Критических проблем не обнаружено.";
        }
    }

    public string HomeAttentionIcon => HomeAttentionState == "ok" ? "✓" : "!";

    public string HomeAttentionDescription
    {
        get
        {
            if (!IsZapretInstalled)
            {
                return "Сначала скачайте или подключите zapret, затем можно будет проверить конфиги.";
            }

            if (!HasAnyHomeProbeResults)
            {
                return "Без проверки лучше не выбирать профиль наугад. Запустите проверку конфигов.";
            }

            if (HomeHasPartialRecommendation)
            {
                return "Автоматически запускать такой результат не стоит. Лучше открыть таблицу конфигов и посмотреть детали.";
            }

            if (HomeHasAvailableUpdates)
            {
                return "Проверьте вкладку установки и обновления или воспользуйтесь кнопкой проверки компонентов.";
            }

            return IsTgWsProxyInstalled
                ? "Все ключевые компоненты готовы. При необходимости можно открыть вкладку Telegram / Proxy."
                : "Основной сценарий готов. TG WS Proxy можно установить позже, если понадобится Telegram-режим.";
        }
    }

    public string RuntimeStatus
    {
        get => _runtimeStatus;
        set
        {
            if (SetProperty(ref _runtimeStatus, value))
            {
                NotifyHomeDashboardChanged();
            }
        }
    }

    public string ServiceStatus
    {
        get => _serviceStatus;
        set
        {
            if (SetProperty(ref _serviceStatus, value))
            {
                NotifyHomeDashboardChanged();
            }
        }
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        set
        {
            if (SetProperty(ref _updateStatus, value))
            {
                NotifyHomeDashboardChanged();
            }
        }
    }

    public string GameModeStatus
    {
        get => _gameModeStatus;
        set => SetProperty(ref _gameModeStatus, value);
    }

    public string ManagerUpdateStatus
    {
        get => _managerUpdateStatus;
        set
        {
            if (SetProperty(ref _managerUpdateStatus, value))
            {
                NotifyHomeDashboardChanged();
            }
        }
    }

    public string LastActionText
    {
        get => _lastActionText;
        set
        {
            if (SetProperty(ref _lastActionText, value))
            {
                OnPropertyChanged(nameof(BusyActionText));
                NotifyHomeDashboardChanged();
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
        set
        {
            if (SetProperty(ref _recommendedConfigText, value))
            {
                NotifyHomeDashboardChanged();
            }
        }
    }

    public string SelectedSummaryText
    {
        get => _selectedSummaryText;
        set
        {
            if (SetProperty(ref _selectedSummaryText, value))
            {
                NotifyHomeDashboardChanged();
            }
        }
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

    public bool IsSelectedConfigRunning => IsSelectedConfigRunningCore();
    public bool IsSelectedConfigInstalledAsService => IsSelectedConfigInstalledAsServiceCore();
    public string SelectedServiceActionText => IsSelectedConfigInstalledAsService ? "Удалить службу" : "Установить службу";
    public bool IsSystemDnsProfileSelected => string.Equals(SelectedDnsProfile?.Key, DnsService.SystemProfileKey, StringComparison.OrdinalIgnoreCase);
    public bool IsCustomDnsProfileSelected => string.Equals(SelectedDnsProfile?.Key, DnsService.CustomProfileKey, StringComparison.OrdinalIgnoreCase);
    public bool CanUseDnsOverHttps => !IsSystemDnsProfileSelected;
    public bool ShowDnsInputs => !IsSystemDnsProfileSelected;
    public bool ShowDnsDohUrl => CanUseDnsOverHttps && DnsUseDohEnabled;

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
                OnPropertyChanged(nameof(ShowBusyOverlay));
                NotifyHomeDashboardChanged();
                RaiseCommandStates();
            }
        }
    }

    public bool SuppressBusyOverlay
    {
        get => _suppressBusyOverlay;
        set
        {
            if (SetProperty(ref _suppressBusyOverlay, value))
            {
                OnPropertyChanged(nameof(ShowBusyOverlay));
            }
        }
    }

    public bool ShowBusyOverlay => IsBusy && !SuppressBusyOverlay;

    public bool HasUpdate
    {
        get => _hasUpdate;
        set
        {
            if (SetProperty(ref _hasUpdate, value))
            {
                NotifyZapretPresentationChanged();
                NotifyHomeDashboardChanged();
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
                ProbeButtonText = value ? "Отмена" : "Проверить все";
                NotifyHomeDashboardChanged();
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
                ConfigureScheduledUpdateChecks();
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
            }
        }
    }

    public GameModeOption? SelectedApplicationThemeMode
    {
        get => _selectedApplicationThemeMode;
        set
        {
            if (SetProperty(ref _selectedApplicationThemeMode, value) && value is not null)
            {
                _settings.ApplicationThemeMode = ApplicationThemeService.NormalizeThemeMode(value.Value);
                UseLightThemeEnabled = ApplicationThemeService.ResolveUseLightTheme(_settings.ApplicationThemeMode, _settings.UseLightTheme);
                _settingsService.Save(_settings);
                LastActionText = $"Действие: тема приложения — {value.Label.ToLowerInvariant()}";
            }
        }
    }

    public GameModeOption? SelectedUpdateCheckIntervalOption
    {
        get => _selectedUpdateCheckIntervalOption;
        set
        {
            if (SetProperty(ref _selectedUpdateCheckIntervalOption, value) && value is not null)
            {
                _settings.UpdateCheckIntervalValue = NormalizeUpdateCheckIntervalValue(value.Value);
                _settingsService.Save(_settings);
                ConfigureScheduledUpdateChecks();
                LastActionText = $"Действие: интервал проверки обновлений — {value.Label.ToLowerInvariant()}";
            }
        }
    }

    public bool AutoUpdateIpSetOnStartupEnabled
    {
        get => _autoUpdateIpSetOnStartupEnabled;
        set
        {
            if (SetProperty(ref _autoUpdateIpSetOnStartupEnabled, value))
            {
                _settings.AutoUpdateIpSetOnStartupEnabled = value;
                _settingsService.Save(_settings);
                LastActionText = value
                    ? "Действие: автообновление списка IPSet при запуске включено"
                    : "Действие: автообновление списка IPSet при запуске выключено";
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

                NotifyHomeDashboardChanged();
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
                NotifyHomeDashboardChanged();
            }
        }
    }

    public void UpdateSelectedConfigRows(IEnumerable<ConfigTableRow> rows)
    {
        _selectedConfigPaths.Clear();

        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.FilePath))
            {
                _selectedConfigPaths.Add(row.FilePath);
            }
        }

        RaiseCommandStates();
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

    public DnsService.DnsProfileDefinition? SelectedDnsProfile
    {
        get => _selectedDnsProfile;
        set
        {
            if (SetProperty(ref _selectedDnsProfile, value))
            {
                UpdateEmbeddedDnsEditorState();
                RaiseCommandStates();
            }
        }
    }

    public GameModeOption? SelectedIpSetMode
    {
        get => _selectedIpSetMode;
        set
        {
            if (SetProperty(ref _selectedIpSetMode, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string DnsPrimaryAddress
    {
        get => _dnsPrimaryAddress;
        set => SetProperty(ref _dnsPrimaryAddress, value);
    }

    public string DnsSecondaryAddress
    {
        get => _dnsSecondaryAddress;
        set => SetProperty(ref _dnsSecondaryAddress, value);
    }

    public string DnsDohUrl
    {
        get => _dnsDohUrl;
        set => SetProperty(ref _dnsDohUrl, value);
    }

    public bool DnsUseDohEnabled
    {
        get => _dnsUseDohEnabled;
        set
        {
            if (SetProperty(ref _dnsUseDohEnabled, value))
            {
                OnPropertyChanged(nameof(ShowDnsDohUrl));
            }
        }
    }

    public string TgWsProxyStatus
    {
        get => _tgWsProxyStatus;
        set
        {
            if (SetProperty(ref _tgWsProxyStatus, value))
            {
                NotifyHomeDashboardChanged();
            }
        }
    }

    public string TgWsProxyVersionStatus
    {
        get => _tgWsProxyVersionStatus;
        set => SetProperty(ref _tgWsProxyVersionStatus, value);
    }

    public string TgWsProxyUpdateStatus
    {
        get => _tgWsProxyUpdateStatus;
        set
        {
            if (SetProperty(ref _tgWsProxyUpdateStatus, value))
            {
                NotifyHomeDashboardChanged();
            }
        }
    }

    public string TgWsProxyHost
    {
        get => _tgWsProxyHost;
        set => SetProperty(ref _tgWsProxyHost, value);
    }

    public string TgWsProxyPort
    {
        get => _tgWsProxyPort;
        set => SetProperty(ref _tgWsProxyPort, value);
    }

    public string TgWsProxySecret
    {
        get => _tgWsProxySecret;
        set => SetProperty(ref _tgWsProxySecret, value);
    }

    public string TgWsProxyDcMappings
    {
        get => _tgWsProxyDcMappings;
        set => SetProperty(ref _tgWsProxyDcMappings, value);
    }

    public bool TgWsProxyCfProxyEnabled
    {
        get => _tgWsProxyCfProxyEnabled;
        set => SetProperty(ref _tgWsProxyCfProxyEnabled, value);
    }

    public bool TgWsProxyCfProxyPriorityEnabled
    {
        get => _tgWsProxyCfProxyPriorityEnabled;
        set => SetProperty(ref _tgWsProxyCfProxyPriorityEnabled, value);
    }

    public bool TgWsProxyCustomDomainEnabled
    {
        get => _tgWsProxyCustomDomainEnabled;
        set => SetProperty(ref _tgWsProxyCustomDomainEnabled, value);
    }

    public string TgWsProxyCfProxyDomain
    {
        get => _tgWsProxyCfProxyDomain;
        set => SetProperty(ref _tgWsProxyCfProxyDomain, value);
    }

    public string TgWsProxyBufferKilobytes
    {
        get => _tgWsProxyBufferKilobytes;
        set => SetProperty(ref _tgWsProxyBufferKilobytes, value);
    }

    public string TgWsProxyPoolSize
    {
        get => _tgWsProxyPoolSize;
        set => SetProperty(ref _tgWsProxyPoolSize, value);
    }

    public string TgWsProxyLogMaxMegabytes
    {
        get => _tgWsProxyLogMaxMegabytes;
        set => SetProperty(ref _tgWsProxyLogMaxMegabytes, value);
    }

    public bool TgWsProxyVerboseLoggingEnabled
    {
        get => _tgWsProxyVerboseLoggingEnabled;
        set => SetProperty(ref _tgWsProxyVerboseLoggingEnabled, value);
    }

    public bool TgWsProxyAutoStartEnabled
    {
        get => _tgWsProxyAutoStartEnabled;
        set
        {
            if (SetProperty(ref _tgWsProxyAutoStartEnabled, value))
            {
                OnPropertyChanged(nameof(TgWsProxyAutoStartButtonText));
            }
        }
    }

    public bool TgWsProxyAutoCheckUpdatesEnabled
    {
        get => _tgWsProxyAutoCheckUpdatesEnabled;
        set
        {
            if (SetProperty(ref _tgWsProxyAutoCheckUpdatesEnabled, value))
            {
                _settings.TgWsProxyAutoCheckUpdatesEnabled = value;
                _settingsService.Save(_settings);
                LastActionText = value
                    ? "Действие: автопроверка TG WS Proxy включена"
                    : "Действие: автопроверка TG WS Proxy выключена";
            }
        }
    }

    public bool IsTgWsProxyInstalled
    {
        get => _isTgWsProxyInstalled;
        set
        {
            if (SetProperty(ref _isTgWsProxyInstalled, value))
            {
                OnPropertyChanged(nameof(TgWsProxyInstallButtonText));
                OnPropertyChanged(nameof(TgWsProxyLaunchButtonText));
                OnPropertyChanged(nameof(TgWsProxyRunningVisibility));
                OnPropertyChanged(nameof(TgWsProxyStoppedVisibility));
                NotifyTgWsProxyPresentationChanged();
                NotifyHomeDashboardChanged();
                RaiseCommandStates();
            }
        }
    }

    public bool IsTgWsProxyRunning
    {
        get => _isTgWsProxyRunning;
        set
        {
            if (SetProperty(ref _isTgWsProxyRunning, value))
            {
                OnPropertyChanged(nameof(TgWsProxyLaunchButtonText));
                OnPropertyChanged(nameof(TgWsProxyRunningVisibility));
                OnPropertyChanged(nameof(TgWsProxyStoppedVisibility));
                NotifyHomeDashboardChanged();
                RaiseCommandStates();
            }
        }
    }

    public bool TgWsProxyHasUpdate
    {
        get => _tgWsProxyHasUpdate;
        set
        {
            if (SetProperty(ref _tgWsProxyHasUpdate, value))
            {
                OnPropertyChanged(nameof(TgWsProxyReleaseActionButtonText));
                NotifyTgWsProxyPresentationChanged();
                NotifyHomeDashboardChanged();
                RaiseCommandStates();
            }
        }
    }

    public bool HasManagerUpdate
    {
        get => _hasManagerUpdate;
        set
        {
            if (SetProperty(ref _hasManagerUpdate, value))
            {
                NotifyManagerPresentationChanged();
                NotifyHomeDashboardChanged();
                RaiseCommandStates();
            }
        }
    }

    public string TgWsProxyInstallButtonText => !IsTgWsProxyInstalled
        ? "Установить TG WS Proxy"
        : "Переустановить";

    public string TgWsProxyInstallOrUpdateButtonText => !IsTgWsProxyInstalled
        ? "Установить"
        : "Обновить";

    public string TgWsProxyReleaseActionButtonText => TgWsProxyHasUpdate
        ? "Обновить релиз"
        : "Проверить релиз";

    public string TgWsProxyLaunchButtonText => IsTgWsProxyRunning
        ? "Перезапустить"
        : "Запустить";

    public string TgWsProxyAutoStartButtonText => TgWsProxyAutoStartEnabled
        ? "Выключить автозапуск"
        : "Включить автозапуск";

    public string ZapretInstallOrUpdateButtonText => _installation is null
        ? "Установить"
        : "Обновить";

    public string ManagerInstallOrUpdateButtonText => "Обновить";
    public Visibility ZapretInstalledVisibility => IsZapretInstalled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ZapretMissingVisibility => IsZapretInstalled ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TgWsProxyInstalledVisibility => IsTgWsProxyInstalled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TgWsProxyMissingVisibility => IsTgWsProxyInstalled ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TgWsProxyRunningVisibility => IsTgWsProxyInstalled && IsTgWsProxyRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TgWsProxyStoppedVisibility => IsTgWsProxyInstalled && !IsTgWsProxyRunning ? Visibility.Visible : Visibility.Collapsed;

    private void NotifyZapretPresentationChanged()
    {
        OnPropertyChanged(nameof(IsZapretInstalled));
        OnPropertyChanged(nameof(ZapretInstalledVersionValue));
        OnPropertyChanged(nameof(ZapretInstalledVisibility));
        OnPropertyChanged(nameof(ZapretMissingVisibility));
        OnPropertyChanged(nameof(ZapretPrimaryActionVisibility));
        OnPropertyChanged(nameof(ZapretInstallOrUpdateButtonText));
        OnPropertyChanged(nameof(ZapretUpdateBadgeText));
        OnPropertyChanged(nameof(ZapretUpdateBadgeState));
        OnPropertyChanged(nameof(HasCheckedZapretUpdates));
        NotifyHomeDashboardChanged();
    }

    private void NotifyTgWsProxyPresentationChanged()
    {
        OnPropertyChanged(nameof(TgWsProxyInstalledVersionValue));
        OnPropertyChanged(nameof(TgWsProxyInstallationPath));
        OnPropertyChanged(nameof(TgWsProxyInstalledVisibility));
        OnPropertyChanged(nameof(TgWsProxyMissingVisibility));
        OnPropertyChanged(nameof(TgWsProxyRunningVisibility));
        OnPropertyChanged(nameof(TgWsProxyStoppedVisibility));
        OnPropertyChanged(nameof(TgWsProxyPrimaryActionVisibility));
        OnPropertyChanged(nameof(TgWsProxyInstallOrUpdateButtonText));
        OnPropertyChanged(nameof(TgWsProxyUpdateBadgeText));
        OnPropertyChanged(nameof(TgWsProxyUpdateBadgeState));
        OnPropertyChanged(nameof(HasCheckedTgWsProxyUpdates));
        NotifyHomeDashboardChanged();
    }

    private void NotifyManagerPresentationChanged()
    {
        OnPropertyChanged(nameof(ManagerInstalledVersionValue));
        OnPropertyChanged(nameof(ManagerPrimaryActionVisibility));
        OnPropertyChanged(nameof(ManagerInstallOrUpdateButtonText));
        OnPropertyChanged(nameof(ManagerUpdateBadgeText));
        OnPropertyChanged(nameof(ManagerUpdateBadgeState));
        OnPropertyChanged(nameof(HasCheckedManagerUpdates));
        NotifyHomeDashboardChanged();
    }

    private bool HasAnyHomeProbeResults => _probeResults.Count > 0;
    private bool HomeHasAvailableUpdates => HasUpdate || TgWsProxyHasUpdate || HasManagerUpdate;
    private bool HasCheckedAnyInstalledComponentUpdates => HasCheckedZapretUpdates || HasCheckedTgWsProxyUpdates || HasCheckedManagerUpdates;
    private bool HomeServiceIsMissing =>
        string.IsNullOrWhiteSpace(ServiceStatus) ||
        ServiceStatus.Contains("не установлена", StringComparison.OrdinalIgnoreCase) ||
        ServiceStatus.Contains("недоступно", StringComparison.OrdinalIgnoreCase);
    private bool HomeServiceIsRunning => ServiceStatus.Contains("запущена", StringComparison.OrdinalIgnoreCase);
    private bool HomeServiceIsInstalled => !HomeServiceIsMissing;
    private bool HomeHasPartialRecommendation
    {
        get
        {
            var recommended = GetRecommendedResult();
            return recommended is not null && !HasFullPrimaryCoverage(recommended);
        }
    }

    private string? GetHomeServiceProfileName()
    {
        if (HomeServiceIsMissing || string.IsNullOrWhiteSpace(ServiceStatus))
        {
            return null;
        }

        var colonIndex = ServiceStatus.IndexOf(':');
        if (colonIndex < 0 || colonIndex >= ServiceStatus.Length - 1)
        {
            return null;
        }

        return ServiceStatus[(colonIndex + 1)..].Trim();
    }

    private void NotifyHomeDashboardChanged()
    {
        OnPropertyChanged(nameof(HomeOverallStatusState));
        OnPropertyChanged(nameof(HomeOverallStatusIcon));
        OnPropertyChanged(nameof(HomeOverallStatusTitle));
        OnPropertyChanged(nameof(HomeOverallStatusDescription));
        OnPropertyChanged(nameof(HomeZapretBadgeText));
        OnPropertyChanged(nameof(HomeZapretBadgeState));
        OnPropertyChanged(nameof(HomeServiceBadgeText));
        OnPropertyChanged(nameof(HomeServiceBadgeState));
        OnPropertyChanged(nameof(HomeTelegramBadgeText));
        OnPropertyChanged(nameof(HomeTelegramBadgeState));
        OnPropertyChanged(nameof(HomeUpdatesBadgeText));
        OnPropertyChanged(nameof(HomeUpdatesBadgeState));
        OnPropertyChanged(nameof(HomeRecommendedActionKind));
        OnPropertyChanged(nameof(HomeRecommendedActionTitle));
        OnPropertyChanged(nameof(HomeRecommendedActionDescription));
        OnPropertyChanged(nameof(HomeRecommendedActionButtonText));
        OnPropertyChanged(nameof(HomeRecommendedActionButtonIcon));
        OnPropertyChanged(nameof(HomeRecommendedActionEnabled));
        OnPropertyChanged(nameof(HomeLastActionText));
        OnPropertyChanged(nameof(HomeLastActionVisibility));
        OnPropertyChanged(nameof(HomeConfigCardTitle));
        OnPropertyChanged(nameof(HomeConfigCardDescription));
        OnPropertyChanged(nameof(HomeServiceCardTitle));
        OnPropertyChanged(nameof(HomeServiceCardDescription));
        OnPropertyChanged(nameof(HomeServiceCardState));
        OnPropertyChanged(nameof(HomeTelegramCardTitle));
        OnPropertyChanged(nameof(HomeTelegramCardDescription));
        OnPropertyChanged(nameof(HomeTelegramCardState));
        OnPropertyChanged(nameof(HomeAttentionState));
        OnPropertyChanged(nameof(HomeAttentionTitle));
        OnPropertyChanged(nameof(HomeAttentionIcon));
        OnPropertyChanged(nameof(HomeAttentionDescription));
    }

    private async Task RunHomeCheckAllAsync()
    {
        LastActionText = "Действие: проверяем состояние компонентов";
        await RefreshStatusAsync();
        await CheckInstalledComponentUpdatesAsync();
    }

    public async Task InitializeAsync(bool startHidden = false)
    {
        await RefreshAsync();
        await TryMigrateInstalledServiceToCurrentVersionAsync();

        if (startHidden)
        {
            _ = RunHiddenStartupInitializationAsync();
            await RefreshStatusAsync();
            return;
        }

        await TryAutoUpdateIpSetOnStartupAsync();
        await RunAutomaticUpdateChecksIfNeededAsync(promptToInstall: true, notifyWhenAvailable: false, force: false);

        await RefreshStatusAsync();
    }

    private async Task RunHiddenStartupInitializationAsync()
    {
        try
        {
            SuppressBusyOverlay = true;
            await TryAutoUpdateIpSetOnStartupAsync();
            await RunAutomaticUpdateChecksIfNeededAsync(promptToInstall: false, notifyWhenAvailable: true, force: false);
        }
        catch
        {
        }
        finally
        {
            SuppressBusyOverlay = false;
            await RefreshStatusAsync();
        }
    }

    private async Task ScheduledUpdateTimer_TickAsync()
    {
        if (_isScheduledUpdateCheckRunning || !AutoCheckUpdatesEnabled || IsBusy || IsProbeRunning)
        {
            return;
        }

        try
        {
            _isScheduledUpdateCheckRunning = true;
            await RunAutomaticUpdateChecksIfNeededAsync(promptToInstall: false, notifyWhenAvailable: true, force: true);
        }
        finally
        {
            _isScheduledUpdateCheckRunning = false;
        }
    }

    private void ConfigureScheduledUpdateChecks()
    {
        _scheduledUpdateTimer.Stop();
        if (!AutoCheckUpdatesEnabled)
        {
            return;
        }

        _scheduledUpdateTimer.Interval = GetUpdateCheckInterval(NormalizeUpdateCheckIntervalValue(_settings.UpdateCheckIntervalValue));
        _scheduledUpdateTimer.Start();
    }

    private async Task RunAutomaticUpdateChecksIfNeededAsync(bool promptToInstall, bool notifyWhenAvailable, bool force)
    {
        if (!AutoCheckUpdatesEnabled)
        {
            return;
        }

        if (!force && !ShouldRunAutomaticUpdateChecksNow())
        {
            return;
        }

        _settings.LastAutomaticUpdateCheckUtc = DateTime.UtcNow;
        _settingsService.Save(_settings);

        await CheckManagerUpdateAsync(showNoUpdatesMessage: false, promptToInstall: promptToInstall, showErrorDialog: false);
        if (_managerUpdateLaunchRequested)
        {
            return;
        }

        if (_installation is not null)
        {
            await CheckUpdatesAsync(showNoUpdatesMessage: false, promptToInstall: promptToInstall, showErrorDialog: false);
        }

        if (!promptToInstall)
        {
            QueueStartupUpdateNotificationIfNeeded();
        }

        if (TgWsProxyAutoCheckUpdatesEnabled && IsTgWsProxyInstalled)
        {
            await CheckTgWsProxyUpdateAsync(showNoUpdatesMessage: false, showErrorDialog: false, notifyWhenAvailable: notifyWhenAvailable);
        }
    }

    private bool ShouldRunAutomaticUpdateChecksNow()
    {
        if (!_settings.LastAutomaticUpdateCheckUtc.HasValue)
        {
            return true;
        }

        var interval = GetUpdateCheckInterval(NormalizeUpdateCheckIntervalValue(_settings.UpdateCheckIntervalValue));
        return DateTime.UtcNow - _settings.LastAutomaticUpdateCheckUtc.Value >= interval;
    }

    private async Task TryAutoUpdateIpSetOnStartupAsync()
    {
        if (!AutoUpdateIpSetOnStartupEnabled || _installation is null)
        {
            return;
        }

        var previousSuppressBusyOverlay = SuppressBusyOverlay;
        try
        {
            SuppressBusyOverlay = true;
            await RunBusyAsync(() => UpdateIpSetListCoreAsync(_installation, showNotification: false));
        }
        catch
        {
        }
        finally
        {
            SuppressBusyOverlay = previousSuppressBusyOverlay;
        }
    }

    private static string NormalizeUpdateCheckIntervalValue(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "1h" => "1h",
            "2h" => "2h",
            "6h" => "6h",
            "12h" => "12h",
            "24h" => "24h",
            _ => "2h"
        };
    }

    private static TimeSpan GetUpdateCheckInterval(string value)
    {
        return NormalizeUpdateCheckIntervalValue(value) switch
        {
            "1h" => TimeSpan.FromHours(1),
            "2h" => TimeSpan.FromHours(2),
            "6h" => TimeSpan.FromHours(6),
            "12h" => TimeSpan.FromHours(12),
            "24h" => TimeSpan.FromHours(24),
            _ => TimeSpan.FromHours(2)
        };
    }

    public bool HasPendingStartupUpdatePrompt()
    {
        return !_startupUpdatePromptHandled &&
               (_pendingStartupManagerUpdateInfo is not null || _pendingStartupZapretUpdateInfo is not null);
    }

    public async Task PresentPendingStartupUpdatePromptsAsync()
    {
        if (_isPresentingStartupUpdatePrompts || _startupUpdatePromptHandled)
        {
            return;
        }

        if (_pendingStartupManagerUpdateInfo is null && _pendingStartupZapretUpdateInfo is null)
        {
            return;
        }

        try
        {
            _isPresentingStartupUpdatePrompts = true;
            _startupUpdatePromptHandled = true;

            if (_pendingStartupManagerUpdateInfo is not null)
            {
                var managerUpdate = _pendingStartupManagerUpdateInfo;
                _pendingStartupManagerUpdateInfo = null;
                await PromptManagerUpdateAsync(managerUpdate);
                if (_managerUpdateLaunchRequested)
                {
                    return;
                }
            }

            if (_pendingStartupZapretUpdateInfo is not null)
            {
                var zapretUpdate = _pendingStartupZapretUpdateInfo;
                _pendingStartupZapretUpdateInfo = null;
                await PromptZapretUpdateAsync(zapretUpdate);
            }
        }
        finally
        {
            _isPresentingStartupUpdatePrompts = false;
        }
    }

    private void QueueStartupUpdateNotificationIfNeeded()
    {
        if (_startupUpdateNotificationShownThisSession || !HasPendingStartupUpdatePrompt())
        {
            return;
        }

        var message = BuildStartupUpdateNotificationMessage();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _startupUpdateNotificationShownThisSession = true;
        LastActionText = $"Действие: {message}";
    }

    private string? BuildStartupUpdateNotificationMessage()
    {
        var hasManagerUpdate = _pendingStartupManagerUpdateInfo is not null;
        var hasZapretUpdate = _pendingStartupZapretUpdateInfo is not null;

        return (hasManagerUpdate, hasZapretUpdate) switch
        {
            (true, true) => "Найдены обновления программы и сборки zapret. Нажмите уведомление или значок в трее.",
            (true, false) => "Найдено обновление программы. Нажмите уведомление или значок в трее.",
            (false, true) => "Найдено обновление сборки zapret. Нажмите уведомление или значок в трее.",
            _ => null
        };
    }

    private async Task TryMigrateInstalledServiceToCurrentVersionAsync()
    {
        if (_installation is null)
        {
            return;
        }

        var currentProcessPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentProcessPath) || !File.Exists(currentProcessPath))
        {
            return;
        }

        currentProcessPath = Path.GetFullPath(currentProcessPath);
        var serviceStatus = _serviceManager.GetStatus();
        if (!serviceStatus.IsInstalled)
        {
            return;
        }

        var usesCurrentExecutable = _serviceManager.UsesExecutable(currentProcessPath);
        var hasServiceHostArguments =
            !string.IsNullOrWhiteSpace(serviceStatus.InstallationRootPath) &&
            !string.IsNullOrWhiteSpace(serviceStatus.ProfileToken);
        if (usesCurrentExecutable && hasServiceHostArguments)
        {
            return;
        }

        var migrationInstallation = _installation;
        if (!string.IsNullOrWhiteSpace(serviceStatus.InstallationRootPath))
        {
            migrationInstallation = _discoveryService.TryLoad(serviceStatus.InstallationRootPath) ?? migrationInstallation;
        }

        if (migrationInstallation is null)
        {
            return;
        }

        var migrationProfile = FindProfileByIdentity(
            migrationInstallation,
            serviceStatus.ProfileName,
            serviceStatus.ProfileToken);

        if (migrationProfile is null)
        {
            LastActionText = "Действие: найдена старая служба, но профиль не удалось определить автоматически";
            ShowInlineNotification(
                "Обнаружена служба старой версии, но её профиль не удалось определить автоматически. Нажмите «Установить службу» один раз вручную.",
                isError: true);
            return;
        }

        await RunBusyAsync(async () =>
        {
            RuntimeStatus = "Обнаружена старая служба. Переносим её на текущую версию программы...";
            LastActionText = $"Действие: переносим службу на текущую версию для {migrationProfile.Name}";

            await _serviceManager.InstallAsync(migrationInstallation, migrationProfile, currentProcessPath);
            _settings.LastInstallationPath = migrationInstallation.RootPath;
            RememberInstalledServiceProfile(migrationProfile, saveImmediately: false);
            _settingsService.Save(_settings);
            _installation = migrationInstallation;
            await Task.Delay(1200);
            await RefreshLiveStatusCoreAsync();
        });

        ShowInlineNotification($"Служба автоматически перенесена на текущую версию программы: {migrationProfile.Name}");
    }

    public async Task RefreshStatusAsync()
    {
        try
        {
            RefreshTgWsProxyStatusCore();
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

    private void LoadTgWsProxyEditor()
    {
        var config = _tgWsProxyService.LoadConfig();
        var resolvedPath = _tgWsProxyService.ResolveExecutablePath(_settings.LastTgWsProxyExecutablePath);
        PersistResolvedTgWsProxyExecutablePath(resolvedPath);
        TgWsProxyHost = config.Host;
        TgWsProxyPort = config.Port.ToString();
        TgWsProxySecret = config.Secret;
        TgWsProxyDcMappings = string.Join(Environment.NewLine, config.DcIpRules);
        TgWsProxyCfProxyEnabled = config.EnableCfProxy;
        TgWsProxyCfProxyPriorityEnabled = config.PreferCfProxy;
        TgWsProxyCustomDomainEnabled = !string.IsNullOrWhiteSpace(config.UserCfProxyDomain);
        TgWsProxyCfProxyDomain = config.UserCfProxyDomain;
        TgWsProxyVerboseLoggingEnabled = config.VerboseLogging;
        try
        {
            _tgWsProxyService.SaveConfig(config);
        }
        catch
        {
        }
        _tgWsProxyAutoCheckUpdatesEnabled = _settings.TgWsProxyAutoCheckUpdatesEnabled;
        OnPropertyChanged(nameof(TgWsProxyAutoCheckUpdatesEnabled));
        SynchronizeTgWsProxyAutoStartPreference(config.AutoStart, allowRegistryUpdate: true);
        TgWsProxyBufferKilobytes = config.BufferKilobytes.ToString();
        TgWsProxyPoolSize = config.PoolSize.ToString();
        TgWsProxyLogMaxMegabytes = config.LogMaxMegabytes.ToString();
        RefreshTgWsProxyStatusCore();
    }

    private void SynchronizeTgWsProxyAutoStartPreference(bool desiredAutoStart, bool allowRegistryUpdate)
    {
        var resolvedPath = _resolvedTgWsProxyExecutablePath ?? _tgWsProxyService.ResolveExecutablePath(_settings.LastTgWsProxyExecutablePath);
        PersistResolvedTgWsProxyExecutablePath(resolvedPath);

        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            if (allowRegistryUpdate)
            {
                _tgWsProxyService.SetAutoStartEnabled(resolvedPath, desiredAutoStart);
            }

            TgWsProxyAutoStartEnabled = _tgWsProxyService.IsAutoStartEnabled(resolvedPath);
            return;
        }

        TgWsProxyAutoStartEnabled = desiredAutoStart;
    }

    private void RefreshTgWsProxyStatusCore()
    {
        try
        {
            var resolvedPath = _tgWsProxyService.ResolveExecutablePath(_settings.LastTgWsProxyExecutablePath);
            PersistResolvedTgWsProxyExecutablePath(resolvedPath);

            var installedVersion = _tgWsProxyService.GetInstalledVersion(resolvedPath);
            var hasExecutable = !string.IsNullOrWhiteSpace(resolvedPath);
            var hasConfig = File.Exists(_tgWsProxyService.ConfigPath);

            IsTgWsProxyInstalled = hasExecutable;
            IsTgWsProxyRunning = _tgWsProxyService.IsRunning(resolvedPath);

            if (!hasExecutable)
            {
                TgWsProxyStatus = hasConfig
                    ? "TG WS Proxy: exe не найден, но настройки уже импортированы"
                    : "TG WS Proxy: exe не найден";
                TgWsProxyVersionStatus = "Версия TG WS Proxy: не найдена";
            }
            else
            {
                var sourceSuffix = _tgWsProxyService.IsManagedExecutable(resolvedPath)
                    ? "копия ZapretManager"
                    : "найден существующий exe";
                TgWsProxyStatus = IsTgWsProxyRunning
                    ? $"TG WS Proxy: запущен ({sourceSuffix})"
                    : $"TG WS Proxy: готов к запуску ({sourceSuffix})";
                TgWsProxyVersionStatus = $"Версия TG WS Proxy: {FormatTgWsProxyVersionLabel(installedVersion, prefixWithV: true)}";
            }
        }
        catch
        {
            PersistResolvedTgWsProxyExecutablePath(null);
            IsTgWsProxyInstalled = false;
            IsTgWsProxyRunning = false;
            TgWsProxyStatus = "TG WS Proxy: exe не найден";
            TgWsProxyVersionStatus = "Версия TG WS Proxy: не найдена";
        }

        if (string.IsNullOrWhiteSpace(_tgWsProxyLatestVersion))
        {
            TgWsProxyUpdateStatus = "Обновления TG WS Proxy: не проверялись";
        }

        NotifyTgWsProxyPresentationChanged();
        OnPropertyChanged(nameof(TgWsProxyAutoStartButtonText));
    }

    private string BuildTgWsProxyLinkPreview(TgWsProxyConfig config)
    {
        return _tgWsProxyService.BuildTelegramProxyUrl(config);
    }

    private async Task HandleTgWsProxyReleaseActionAsync()
    {
        if (TgWsProxyHasUpdate)
        {
            await InstallTgWsProxyAsync();
            return;
        }

        await CheckTgWsProxyUpdateAsync(showNoUpdatesMessage: true, showErrorDialog: true, notifyWhenAvailable: false);
    }

    private static string FormatTgWsProxyVersionLabel(string? version, bool prefixWithV = false)
    {
        var value = version?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "неизвестна";
        }

        var match = Regex.Match(value, @"\d+(?:\.\d+){0,3}");
        var normalized = match.Success ? match.Value : value.TrimStart('v', 'V');
        var parts = normalized
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        while (parts.Count > 1 && string.Equals(parts[^1], "0", StringComparison.Ordinal))
        {
            parts.RemoveAt(parts.Count - 1);
        }

        var compact = parts.Count > 0 ? string.Join('.', parts) : normalized;
        return prefixWithV ? $"v{compact}" : compact;
    }

    private static string FormatZapretVersionLabel(string? version)
    {
        var value = version?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"v{value}";
    }

    private bool TryBuildTgWsProxyConfigFromInputs(out TgWsProxyConfig config, out string errorMessage)
    {
        config = new TgWsProxyConfig();
        errorMessage = string.Empty;

        var host = string.IsNullOrWhiteSpace(TgWsProxyHost)
            ? "127.0.0.1"
            : TgWsProxyHost.Trim();

        if (!int.TryParse(TgWsProxyPort, out var port) || port is < 1 or > 65535)
        {
            errorMessage = "Укажите корректный порт TG WS Proxy от 1 до 65535.";
            return false;
        }

        var secret = (TgWsProxySecret ?? string.Empty).Trim().ToLowerInvariant();
        if (!Regex.IsMatch(secret, "^[0-9a-f]{32}$"))
        {
            errorMessage = "Secret должен содержать ровно 32 шестнадцатеричных символа.";
            return false;
        }

        if (!int.TryParse(TgWsProxyBufferKilobytes, out var bufferKilobytes) || bufferKilobytes < 1)
        {
            errorMessage = "Буфер TG WS Proxy должен быть положительным числом в КБ.";
            return false;
        }

        if (!int.TryParse(TgWsProxyPoolSize, out var poolSize) || poolSize < 1)
        {
            errorMessage = "Пул WebSocket-сессий должен быть положительным числом.";
            return false;
        }

        if (!int.TryParse(TgWsProxyLogMaxMegabytes, out var logMaxMegabytes) || logMaxMegabytes < 1)
        {
            errorMessage = "Максимальный размер лога должен быть положительным числом в МБ.";
            return false;
        }

        var dcRules = (TgWsProxyDcMappings ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        foreach (var rule in dcRules)
        {
            if (!Regex.IsMatch(rule, @"^\d+\s*:\s*[^\s]+$"))
            {
                errorMessage = $"Строка DC → IP имеет неверный формат: {rule}";
                return false;
            }
        }

        config = new TgWsProxyConfig
        {
            Host = host,
            Port = port,
            Secret = secret,
            DcIpRules = dcRules,
            EnableCfProxy = TgWsProxyCfProxyEnabled,
            PreferCfProxy = TgWsProxyCfProxyPriorityEnabled,
            UserCfProxyDomain = TgWsProxyCustomDomainEnabled ? (TgWsProxyCfProxyDomain ?? string.Empty).Trim() : string.Empty,
            VerboseLogging = TgWsProxyVerboseLoggingEnabled,
            CheckUpdatesOnStart = false,
            AutoStart = TgWsProxyAutoStartEnabled,
            BufferKilobytes = bufferKilobytes,
            PoolSize = poolSize,
            LogMaxMegabytes = logMaxMegabytes
        };

        return true;
    }

    private async Task SaveTgWsProxyConfigAsync()
    {
        if (!TryBuildTgWsProxyConfigFromInputs(out var config, out var errorMessage))
        {
            DialogService.ShowError(errorMessage, "Zapret Manager");
            return;
        }

        var wasRunning = false;
        var saved = await RunBusyAsync(async () =>
        {
            LastActionText = "Действие: сохраняем настройки TG WS Proxy";
            _tgWsProxyService.SaveConfig(config);
            SynchronizeTgWsProxyAutoStartPreference(config.AutoStart, allowRegistryUpdate: true);
            wasRunning = _tgWsProxyService.IsRunning(_resolvedTgWsProxyExecutablePath);

            if (wasRunning)
            {
                _tgWsProxyService.Stop(_resolvedTgWsProxyExecutablePath, TimeSpan.FromSeconds(5));
                await Task.Delay(350);
                _tgWsProxyService.Start(_resolvedTgWsProxyExecutablePath!);
            }

            RefreshTgWsProxyStatusCore();
            LastActionText = wasRunning
                ? "Действие: настройки TG WS Proxy сохранены и прокси перезапущен"
                : "Действие: настройки TG WS Proxy сохранены";
        });

        if (saved)
        {
            ShowInlineNotification(wasRunning
                ? "Настройки TG WS Proxy сохранены, процесс перезапущен."
                : "Настройки TG WS Proxy сохранены.");
        }
    }

    private async Task ToggleTgWsProxyAutoStartAsync()
    {
        var config = _tgWsProxyService.LoadConfig();
        config.AutoStart = !config.AutoStart;

        var toggled = await RunBusyAsync(async () =>
        {
            LastActionText = config.AutoStart
                ? "Действие: включаем автозапуск TG WS Proxy"
                : "Действие: выключаем автозапуск TG WS Proxy";
            _tgWsProxyService.SaveConfig(config);
            SynchronizeTgWsProxyAutoStartPreference(config.AutoStart, allowRegistryUpdate: true);
            RefreshTgWsProxyStatusCore();
            LastActionText = config.AutoStart
                ? "Действие: автозапуск TG WS Proxy включён"
                : "Действие: автозапуск TG WS Proxy выключен";
            await Task.CompletedTask;
        });

        if (toggled)
        {
            ShowInlineNotification(config.AutoStart
                ? "Автозапуск TG WS Proxy включён."
                : "Автозапуск TG WS Proxy выключен.");
        }
    }

    private async Task AddTgWsProxyToTelegramAsync()
    {
        if (!TryBuildTgWsProxyConfigFromInputs(out var config, out var errorMessage))
        {
            DialogService.ShowError(errorMessage, "Zapret Manager");
            return;
        }

        var saved = await RunBusyAsync(async () =>
        {
            LastActionText = "Действие: сохраняем настройки TG WS Proxy перед добавлением прокси";
            _tgWsProxyService.SaveConfig(config);
            await Task.CompletedTask;
        });

        if (!saved)
        {
            return;
        }

        try
        {
            var proxyLink = BuildTgWsProxyLinkPreview(config);
            OpenExternalTarget(proxyLink);
            LastActionText = "Действие: открываем Telegram для добавления MTProto-прокси";
            ShowInlineNotification("Telegram открыт с предложением добавить прокси.");
        }
        catch (Exception ex)
        {
            DialogService.ShowError(ex, "Zapret Manager");
        }
    }

    private async Task CheckTgWsProxyUpdateAsync(bool showNoUpdatesMessage, bool showErrorDialog, bool notifyWhenAvailable)
    {
        var updateInfo = await GetTgWsProxyUpdateInfoAsync(showErrorDialog);
        if (updateInfo is null)
        {
            return;
        }

        if (!updateInfo.IsUpdateAvailable)
        {
            if (showNoUpdatesMessage)
            {
                DialogService.ShowInfo("Новых обновлений TG WS Proxy не найдено.", "Zapret Manager");
            }

            return;
        }

        if (notifyWhenAvailable && !_tgWsProxyUpdateNotificationShownThisSession)
        {
            _tgWsProxyUpdateNotificationShownThisSession = true;
            LastActionText = $"Действие: найдено обновление TG WS Proxy {FormatTgWsProxyVersionLabel(updateInfo.LatestVersion, prefixWithV: true)}";
        }
    }

    private async Task<TgWsProxyReleaseInfo?> GetTgWsProxyUpdateInfoAsync(bool showErrorDialog)
    {
        TgWsProxyReleaseInfo? updateInfo = null;
        var checkedSuccessfully = await RunBusyAsync(async () =>
        {
            LastActionText = "Действие: проверяем обновления TG WS Proxy";
            updateInfo = await _tgWsProxyService.GetReleaseInfoAsync(_tgWsProxyService.GetInstalledVersion(_resolvedTgWsProxyExecutablePath));
            if (updateInfo is not null)
            {
                ApplyTgWsProxyReleaseInfo(updateInfo);
            }
        }, showErrorDialog: showErrorDialog);

        return checkedSuccessfully ? updateInfo : null;
    }

    private void ApplyTgWsProxyReleaseInfo(TgWsProxyReleaseInfo updateInfo)
    {
        _tgWsProxyLatestVersion = updateInfo.LatestVersion;
        _tgWsProxyDownloadUrl = updateInfo.DownloadUrl;
        _tgWsProxyAssetFileName = updateInfo.AssetFileName;
        TgWsProxyHasUpdate = updateInfo.IsUpdateAvailable;

        if (updateInfo.IsUpdateAvailable)
        {
            var publishedSuffix = updateInfo.PublishedAt is null
                ? string.Empty
                : $" ({updateInfo.PublishedAt:dd.MM.yyyy})";
            TgWsProxyUpdateStatus = $"Обновления TG WS Proxy: доступна версия {FormatTgWsProxyVersionLabel(updateInfo.LatestVersion, prefixWithV: true)}{publishedSuffix}";
        }
        else if (!string.IsNullOrWhiteSpace(updateInfo.CurrentVersion))
        {
            TgWsProxyUpdateStatus = $"Обновления TG WS Proxy: установлена актуальная версия {FormatTgWsProxyVersionLabel(updateInfo.CurrentVersion, prefixWithV: true)}";
        }
        else
        {
            TgWsProxyUpdateStatus = $"Обновления TG WS Proxy: доступна версия {FormatTgWsProxyVersionLabel(updateInfo.LatestVersion, prefixWithV: true)} для установки";
        }

        NotifyTgWsProxyPresentationChanged();
    }

    private async Task PromptTgWsProxyUpdateAsync(TgWsProxyReleaseInfo updateInfo)
    {
        var currentVersion = string.IsNullOrWhiteSpace(updateInfo.CurrentVersion)
            ? "неизвестна"
            : FormatTgWsProxyVersionLabel(updateInfo.CurrentVersion, prefixWithV: true);
        var latestVersion = FormatTgWsProxyVersionLabel(updateInfo.LatestVersion, prefixWithV: true);

        var shouldInstall = DialogService.ConfirmCustom(
            $"Найдена новая версия TG WS Proxy: {latestVersion}.{Environment.NewLine}Текущая: {currentVersion}",
            "Zapret Manager",
            primaryButtonText: "Обновить",
            secondaryButtonText: "Позже");

        if (!shouldInstall)
        {
            LastActionText = "Действие: обновление TG WS Proxy отложено";
            return;
        }

        await InstallTgWsProxyAsync();
    }

    private async Task InstallTgWsProxyAsync()
    {
        TgWsProxyReleaseInfo? updateInfo = null;
        var restartedAfterUpdate = false;
        var installed = await RunBusyAsync(async () =>
        {
            LastActionText = "Действие: подготавливаем установку TG WS Proxy";
            var currentExecutablePath = _tgWsProxyService.ResolveExecutablePath(_settings.LastTgWsProxyExecutablePath);
            PersistResolvedTgWsProxyExecutablePath(currentExecutablePath);
            updateInfo = await _tgWsProxyService.GetReleaseInfoAsync(_tgWsProxyService.GetInstalledVersion(currentExecutablePath));
            ApplyTgWsProxyReleaseInfo(updateInfo);

            if (string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
            {
                throw new InvalidOperationException("GitHub не вернул exe-файл TG WS Proxy для скачивания.");
            }

            var downloadedPath = await _tgWsProxyService.DownloadReleaseAsync(
                updateInfo.DownloadUrl,
                updateInfo.LatestVersion);
            var targetExecutablePath = _tgWsProxyService.GetInstallTargetPath(currentExecutablePath);

            restartedAfterUpdate = _tgWsProxyService.IsRunning(currentExecutablePath);
            if (restartedAfterUpdate)
            {
                _tgWsProxyService.Stop(currentExecutablePath, TimeSpan.FromSeconds(6));
                await Task.Delay(400);
            }

            await _tgWsProxyService.InstallDownloadedReleaseAsync(downloadedPath, targetExecutablePath);
            PersistResolvedTgWsProxyExecutablePath(targetExecutablePath);

            if (TryBuildTgWsProxyConfigFromInputs(out var config, out _))
            {
                _tgWsProxyService.SaveConfig(config);
                SynchronizeTgWsProxyAutoStartPreference(config.AutoStart, allowRegistryUpdate: true);
            }
            else
            {
                _tgWsProxyService.EnsureConfigExists();
            }

            if (restartedAfterUpdate)
            {
                _tgWsProxyService.Start(targetExecutablePath);
            }

            RefreshTgWsProxyStatusCore();
            _tgWsProxyLatestVersion = updateInfo.LatestVersion;
            TgWsProxyHasUpdate = false;
            TgWsProxyUpdateStatus = $"Обновления TG WS Proxy: установлена актуальная версия {FormatTgWsProxyVersionLabel(updateInfo.LatestVersion, prefixWithV: true)}";
            LastActionText = $"Действие: TG WS Proxy установлен до версии {FormatTgWsProxyVersionLabel(updateInfo.LatestVersion, prefixWithV: true)}";
        });

        if (installed && updateInfo is not null)
        {
            ShowInlineNotification(restartedAfterUpdate
                ? $"TG WS Proxy обновлён до {FormatTgWsProxyVersionLabel(updateInfo.LatestVersion, prefixWithV: true)} и снова запущен."
                : $"TG WS Proxy установлен: {FormatTgWsProxyVersionLabel(updateInfo.LatestVersion, prefixWithV: true)}.");
        }
    }

    private void GenerateTgWsProxySecret()
    {
        TgWsProxySecret = _tgWsProxyService.GenerateSecret();
        LastActionText = "Действие: сгенерирован новый secret для TG WS Proxy";
    }

    private void CopyTgWsProxyLink()
    {
        if (!TryBuildTgWsProxyConfigFromInputs(out var config, out var errorMessage))
        {
            DialogService.ShowError(errorMessage, "Zapret Manager");
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(BuildTgWsProxyLinkPreview(config));
            LastActionText = "Действие: ссылка TG WS Proxy скопирована";
            ShowInlineNotification("Ссылка TG WS Proxy скопирована.");
        }
        catch (Exception ex)
        {
            DialogService.ShowError(ex, "Zapret Manager");
        }
    }

    private void OpenTgWsProxyLink()
    {
        if (!TryBuildTgWsProxyConfigFromInputs(out var config, out var errorMessage))
        {
            DialogService.ShowError(errorMessage, "Zapret Manager");
            return;
        }

        try
        {
            OpenExternalTarget(BuildTgWsProxyLinkPreview(config));
            LastActionText = "Действие: открываем ссылку TG WS Proxy в Telegram";
        }
        catch (Exception ex)
        {
            DialogService.ShowError(ex, "Zapret Manager");
        }
    }

    private void LaunchOrRestartTgWsProxy()
    {
        if (!IsTgWsProxyInstalled)
        {
            DialogService.ShowInfo("TG WS Proxy.exe пока не найден. Нажмите кнопку установки, чтобы скачать его в управляемую папку.", "Zapret Manager");
            return;
        }

        try
        {
            var executablePath = _resolvedTgWsProxyExecutablePath
                ?? _tgWsProxyService.ResolveExecutablePath(_settings.LastTgWsProxyExecutablePath)
                ?? throw new InvalidOperationException("Не удалось определить путь к TG WS Proxy.");
            PersistResolvedTgWsProxyExecutablePath(executablePath);

            if (_tgWsProxyService.IsRunning(executablePath))
            {
                _tgWsProxyService.Stop(executablePath, TimeSpan.FromSeconds(6));
                Thread.Sleep(350);
            }

            _tgWsProxyService.Start(executablePath);
            RefreshTgWsProxyStatusCore();
            LastActionText = "Действие: TG WS Proxy запущен";
            ShowInlineNotification("TG WS Proxy запущен.");
        }
        catch (Exception ex)
        {
            DialogService.ShowError(ex, "Zapret Manager");
        }
    }

    private void StopTgWsProxy()
    {
        try
        {
            if (!_tgWsProxyService.Stop(_resolvedTgWsProxyExecutablePath, TimeSpan.FromSeconds(6)))
            {
                DialogService.ShowInfo("TG WS Proxy сейчас не запущен.", "Zapret Manager");
                return;
            }

            RefreshTgWsProxyStatusCore();
            LastActionText = "Действие: TG WS Proxy остановлен";
            ShowInlineNotification("TG WS Proxy остановлен.");
        }
        catch (Exception ex)
        {
            DialogService.ShowError(ex, "Zapret Manager");
        }
    }

    private void OpenTgWsProxyLogs()
    {
        var logPath = _tgWsProxyService.LogPath;
        if (!File.Exists(logPath))
        {
            DialogService.ShowInfo("Лог TG WS Proxy пока не создан.", "Zapret Manager");
            return;
        }

        try
        {
            OpenExternalTarget(logPath);
            LastActionText = "Действие: открываем лог TG WS Proxy";
        }
        catch (Exception ex)
        {
            DialogService.ShowError(ex, "Zapret Manager");
        }
    }

    private void OpenTgWsProxyFolder()
    {
        var executablePath = _resolvedTgWsProxyExecutablePath
            ?? _tgWsProxyService.ResolveExecutablePath(_settings.LastTgWsProxyExecutablePath);
        var folderPath = !string.IsNullOrWhiteSpace(executablePath)
            ? Path.GetDirectoryName(executablePath) ?? _tgWsProxyService.ConfigDirectoryPath
            : _tgWsProxyService.ConfigDirectoryPath;

        Directory.CreateDirectory(folderPath);

        try
        {
            OpenExternalTarget("explorer.exe", $"\"{folderPath}\"");
            LastActionText = "Действие: открываем папку TG WS Proxy";
        }
        catch (Exception ex)
        {
            DialogService.ShowError(ex, "Zapret Manager");
        }
    }

    private bool CanDeleteTgWsProxy()
    {
        if (IsBusy || IsProbeRunning)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(_resolvedTgWsProxyExecutablePath)
               || File.Exists(_tgWsProxyService.ConfigPath)
               || Directory.Exists(_tgWsProxyService.ConfigDirectoryPath)
               || Directory.Exists(_tgWsProxyService.ManagedComponentDirectoryPath);
    }

    private async Task DeleteTgWsProxyAsync()
    {
        var executablePath = _resolvedTgWsProxyExecutablePath
            ?? _tgWsProxyService.ResolveExecutablePath(_settings.LastTgWsProxyExecutablePath);
        var hasManagedDirectory = Directory.Exists(_tgWsProxyService.ManagedComponentDirectoryPath);
        var hasConfigDirectory = Directory.Exists(_tgWsProxyService.ConfigDirectoryPath);
        var hasAnythingToDelete = !string.IsNullOrWhiteSpace(executablePath) || hasManagedDirectory || hasConfigDirectory;

        if (!hasAnythingToDelete)
        {
            DialogService.ShowInfo("TG WS Proxy уже удалён или ещё не был установлен.", "Zapret Manager");
            return;
        }

        var confirmed = DialogService.ConfirmCustom(
            "Будут остановлены TG WS Proxy, удалены exe, настройки, лог и автозапуск.\n\nПродолжить?",
            "Zapret Manager",
            primaryButtonText: "Удалить",
            secondaryButtonText: "Отмена");
        if (!confirmed)
        {
            return;
        }

        var deletedExecutable = false;
        var deletedConfig = false;

        var removed = await RunBusyAsync(async () =>
        {
            LastActionText = "Действие: удаляем TG WS Proxy";
            RuntimeStatus = "Удаляем TG WS Proxy...";

            if (_tgWsProxyService.IsRunning(executablePath))
            {
                _tgWsProxyService.Stop(executablePath, TimeSpan.FromSeconds(6));
                await Task.Delay(350);
            }

            try
            {
                _tgWsProxyService.SetAutoStartEnabled(executablePath, enabled: false);
            }
            catch
            {
            }

            if (hasConfigDirectory)
            {
                TryDeleteDirectory(_tgWsProxyService.ConfigDirectoryPath);
                deletedConfig = !Directory.Exists(_tgWsProxyService.ConfigDirectoryPath);
            }

            if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
            {
                TryDeleteFile(executablePath);
                deletedExecutable = !File.Exists(executablePath);
            }

            if (hasManagedDirectory)
            {
                TryDeleteDirectory(_tgWsProxyService.ManagedComponentDirectoryPath);
                deletedExecutable |= !Directory.Exists(_tgWsProxyService.ManagedComponentDirectoryPath);
            }
            else if (!string.IsNullOrWhiteSpace(executablePath))
            {
                var parentDirectory = Path.GetDirectoryName(executablePath);
                if (!string.IsNullOrWhiteSpace(parentDirectory) &&
                    Directory.Exists(parentDirectory) &&
                    !Directory.EnumerateFileSystemEntries(parentDirectory).Any())
                {
                    TryDeleteDirectory(parentDirectory);
                }
            }

            PersistResolvedTgWsProxyExecutablePath(null);
            _tgWsProxyLatestVersion = null;
            _tgWsProxyDownloadUrl = null;
            _tgWsProxyAssetFileName = null;
            TgWsProxyHasUpdate = false;
            LoadTgWsProxyEditor();
            LastActionText = "Действие: TG WS Proxy удалён";
        });

        if (!removed)
        {
            return;
        }

        if (deletedExecutable || deletedConfig)
        {
            ShowInlineNotification("TG WS Proxy удалён. Настройки и автозапуск очищены.");
            return;
        }

        ShowInlineNotification("TG WS Proxy уже был удалён.", isError: true);
    }

    private void OpenTgWsProxyReleasePage()
    {
        try
        {
            OpenExternalTarget(_tgWsProxyService.ReleasePageUrl);
            LastActionText = "Действие: открываем релизы TG WS Proxy";
        }
        catch (Exception ex)
        {
            DialogService.ShowError(ex, "Zapret Manager");
        }
    }

    private void OpenTgWsProxyGuide()
    {
        try
        {
            OpenExternalTarget(_tgWsProxyService.CfProxyGuideUrl);
            LastActionText = "Действие: открываем инструкцию Cloudflare Proxy";
        }
        catch (Exception ex)
        {
            DialogService.ShowError(ex, "Zapret Manager");
        }
    }

    private async Task TestTgWsProxyCfProxyAsync()
    {
        if (!IsTgWsProxyInstalled)
        {
            DialogService.ShowInfo("Сначала установите TG WS Proxy, затем можно проверить CF-прокси.", "Zapret Manager");
            return;
        }

        var customDomain = TgWsProxyCustomDomainEnabled
            ? NormalizeCfProxyTestDomainInput(TgWsProxyCfProxyDomain)
            : null;
        _isTgWsProxyCfProxyTestRunning = true;
        RaiseCommandStates();

        try
        {
            LastActionText = string.IsNullOrWhiteSpace(customDomain)
                ? "Действие: проверяем автоматические домены CF-прокси"
                : $"Действие: проверяем CF-прокси для домена {customDomain}";

            var result = await _tgWsProxyService.TestCfProxyAsync(customDomain);
            var totalCount = result.Results.Count;
            var successCount = result.Results.Count(static item => item.IsSuccess);

            if (successCount > 0)
            {
                var lines = new List<string>
                {
                    $"✓ CF-прокси работает. {successCount} из {totalCount} серверов доступны."
                };

                if (!string.IsNullOrWhiteSpace(result.Domain))
                {
                    lines.Add(result.UsedCustomDomain
                        ? $"Домен: {result.Domain}"
                        : $"Подобран домен: {result.Domain}");
                }

                var failed = result.Results.Where(static item => !item.IsSuccess).ToArray();
                if (failed.Length > 0)
                {
                    lines.Add(string.Empty);
                    lines.Add("Недоступные серверы:");
                    foreach (var item in failed)
                    {
                        lines.Add($"DC {item.DcId}: {item.Message}");
                    }
                }

                DialogService.ShowInfo(string.Join(Environment.NewLine, lines), "CF-прокси: доступен");
                LastActionText = $"Действие: тест CF-прокси завершён, доступны {successCount} из {totalCount} серверов";
                return;
            }

            var errorLines = new List<string>
            {
                result.UsedCustomDomain && !string.IsNullOrWhiteSpace(result.Domain)
                    ? $"✗ Домен {result.Domain} не отвечает для CF-прокси."
                    : "✗ Ни один из автоматических CF-доменов не отвечает.",
                "Возможно, блокировка или проблемы с сетью.",
                string.Empty
            };

            foreach (var item in result.Results)
            {
                errorLines.Add($"DC {item.DcId}: {item.Message}");
            }

            DialogService.ShowInfo(string.Join(Environment.NewLine, errorLines), "CF-прокси: недоступен");
            LastActionText = "Действие: тест CF-прокси не прошёл";
        }
        catch (Exception ex)
        {
            LastActionText = $"Действие: тест CF-прокси завершился ошибкой - {DialogService.GetShortDisplayMessage(ex)}";
            DialogService.ShowError(ex, "Zapret Manager");
        }
        finally
        {
            _isTgWsProxyCfProxyTestRunning = false;
            RaiseCommandStates();
        }
    }

    private static string? NormalizeCfProxyTestDomainInput(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private void PersistResolvedTgWsProxyExecutablePath(string? resolvedPath)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(resolvedPath)
            ? null
            : Path.GetFullPath(resolvedPath);

        _resolvedTgWsProxyExecutablePath = normalizedPath;
        if (string.Equals(_settings.LastTgWsProxyExecutablePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.LastTgWsProxyExecutablePath = normalizedPath;
        _settingsService.Save(_settings);
    }

    private static string BuildSelectedSummaryText(string? summary, bool hasProbeResult)
    {
        if (!hasProbeResult || string.IsNullOrWhiteSpace(summary))
        {
            return "Подробности появятся после проверки.";
        }

        return string.Equals(summary, "✓", StringComparison.Ordinal)
            ? "✓ Все домены доступны."
            : summary;
    }

    private static string BuildSummaryBadgeText(ConfigProbeResult? probeResult)
    {
        return ProbeBadgeHelper.BuildBadgeText(probeResult);
    }

    private static bool HasOnlyDnsIssues(ConfigProbeResult probeResult)
    {
        return ProbeBadgeHelper.HasOnlyDnsIssues(probeResult);
    }

    private static bool TargetHasOnlyDnsIssues(ConnectivityTargetResult result)
    {
        return ProbeBadgeHelper.HasOnlyDnsIssues(result);
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

    public void ClearProbeResults()
    {
        if (IsBusy || IsProbeRunning)
        {
            LastActionText = "Действие: сначала дождитесь завершения проверки";
            return;
        }

        _probeResults.Clear();
        RecommendedConfigText = "Рекомендуемый конфиг: появится после проверки";
        SelectedSummaryText = "Подробности появятся после проверки.";
        RebuildConfigRows();
        LastActionText = "Действие: результаты проверки очищены";
        ShowInlineNotification("Результаты проверки очищены.");
        RaiseCommandStates();
    }

    private void SyncEmbeddedNetworkSettings()
    {
        _isSyncingEmbeddedNetworkSettings = true;
        try
        {
            var profiles = _dnsService.GetPresetDefinitions(
                _settings.CustomDnsPrimary,
                _settings.CustomDnsSecondary,
                _settings.CustomDnsDohTemplate);

            DnsProfileOptions.Clear();
            foreach (var profile in profiles)
            {
                DnsProfileOptions.Add(profile);
            }

            SelectedDnsProfile = DnsProfileOptions.FirstOrDefault(item =>
                string.Equals(item.Key, GetCurrentDnsProfileKey(), StringComparison.OrdinalIgnoreCase))
                ?? DnsProfileOptions.FirstOrDefault();

            if (_installation is null)
            {
                SelectedIpSetMode = IpSetModeOptions.FirstOrDefault(item => string.Equals(item.Value, "loaded", StringComparison.OrdinalIgnoreCase))
                    ?? IpSetModeOptions.FirstOrDefault();
            }
            else
            {
                var ipSetMode = _ipSetService.GetModeValue(_installation);
                SelectedIpSetMode = IpSetModeOptions.FirstOrDefault(item => string.Equals(item.Value, ipSetMode, StringComparison.OrdinalIgnoreCase))
                    ?? IpSetModeOptions.FirstOrDefault();
            }
        }
        finally
        {
            _isSyncingEmbeddedNetworkSettings = false;
            UpdateEmbeddedDnsEditorState();
        }
    }

    private void UpdateEmbeddedDnsEditorState()
    {
        if (_isSyncingEmbeddedNetworkSettings)
        {
            return;
        }

        var profile = SelectedDnsProfile;
        if (profile is null)
        {
            DnsPrimaryAddress = string.Empty;
            DnsSecondaryAddress = string.Empty;
            DnsDohUrl = string.Empty;
            DnsUseDohEnabled = false;
        }
        else if (string.Equals(profile.Key, DnsService.SystemProfileKey, StringComparison.OrdinalIgnoreCase))
        {
            DnsPrimaryAddress = string.Empty;
            DnsSecondaryAddress = string.Empty;
            DnsDohUrl = string.Empty;
            DnsUseDohEnabled = false;
        }
        else if (string.Equals(profile.Key, DnsService.CustomProfileKey, StringComparison.OrdinalIgnoreCase))
        {
            DnsPrimaryAddress = _settings.CustomDnsPrimary ?? string.Empty;
            DnsSecondaryAddress = _settings.CustomDnsSecondary ?? string.Empty;
            DnsDohUrl = _settings.CustomDnsDohTemplate ?? string.Empty;
            DnsUseDohEnabled = _settings.DnsOverHttpsEnabled;
        }
        else
        {
            DnsPrimaryAddress = profile.ServerAddresses.ElementAtOrDefault(0) ?? string.Empty;
            DnsSecondaryAddress = profile.ServerAddresses.ElementAtOrDefault(1) ?? string.Empty;
            DnsDohUrl = profile.DohTemplate ?? string.Empty;
            DnsUseDohEnabled = _settings.DnsOverHttpsEnabled;
        }

        OnPropertyChanged(nameof(IsSystemDnsProfileSelected));
        OnPropertyChanged(nameof(IsCustomDnsProfileSelected));
        OnPropertyChanged(nameof(CanUseDnsOverHttps));
        OnPropertyChanged(nameof(ShowDnsInputs));
        OnPropertyChanged(nameof(ShowDnsDohUrl));
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
            HasUpdate = false;
            _updateLatestVersion = null;
            _updateDownloadUrl = null;
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
            SyncEmbeddedNetworkSettings();
            NotifyZapretPresentationChanged();
            RaiseCommandStates();
            return;
        }

        _updateService.DisableInternalCheckUpdatesForSiblingInstallations(_installation.RootPath);
        HasPreviousVersion = _updateService.HasStoredPreviousVersion(_installation.RootPath);
        NotifyZapretPresentationChanged();

        InstallationPath = _installation.RootPath;
        VersionText = $"Версия: {FormatZapretVersionLabel(_installation.Version)}";

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
        SyncEmbeddedNetworkSettings();
        await RefreshLiveStatusCoreAsync();
    }

    private Task RefreshLiveStatusCoreAsync()
    {
        if (_installation is null)
        {
            return Task.CompletedTask;
        }

        VersionText = $"Версия: {FormatZapretVersionLabel(_installation.Version)}";

        var runningCount = _processService.GetRunningProcessCount(_installation);
        RuntimeStatus = runningCount > 0
            ? $"Запущен winws.exe: {runningCount}"
            : "winws.exe не запущен";

        var service = _serviceManager.GetStatus();
        if (service.IsInstalled)
        {
            RememberRunningServiceProfile(service);
        }
        else
        {
            RememberInstalledServiceProfile(profile: null);
        }

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

    private async Task HandleZapretInstallOrUpdateAsync()
    {
        if (_installation is null)
        {
            await DownloadZapretAsync();
            return;
        }

        if (HasUpdate)
        {
            await ApplyUpdateAsync();
            return;
        }

        await CheckUpdatesAsync(showNoUpdatesMessage: true, promptToInstall: false);
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

    private async Task HandleTgWsProxyInstallOrUpdateAsync()
    {
        if (!IsTgWsProxyInstalled || TgWsProxyHasUpdate)
        {
            await InstallTgWsProxyAsync();
            return;
        }

        await CheckTgWsProxyUpdateAsync(showNoUpdatesMessage: true, showErrorDialog: true, notifyWhenAvailable: false);
    }

    private async Task OpenTargetsEditorAsync()
    {
        if (_installation is null)
        {
            return;
        }

        await OpenListEditorAsync(
            title: "Домены для проверки",
            description: "Редактируйте список доменов для проверки прямо в окне. Файл сохраняется в targets.txt. Активные строки должны иметь формат KeyName = \"https://site.com\" или KeyName = \"PING:1.1.1.1\". Строки с # считаются комментариями.",
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
            case HiddenConfigsAction.RestoreSelected when window.SelectedFilePaths.Count > 0:
                var selectedHiddenPaths = new HashSet<string>(window.SelectedFilePaths, StringComparer.OrdinalIgnoreCase);
                _settings.HiddenConfigPaths.RemoveAll(path => selectedHiddenPaths.Contains(path));
                _settingsService.Save(_settings);
                RebuildConfigRows();
                if (selectedHiddenPaths.Count == 1)
                {
                    LastActionText = "Действие: скрытый конфиг возвращён";
                    ShowInlineNotification("Скрытый конфиг возвращён в основной список.");
                }
                else
                {
                    LastActionText = $"Действие: возвращены {selectedHiddenPaths.Count} скрытых конфигов";
                    ShowInlineNotification($"Возвращены {selectedHiddenPaths.Count} скрытых конфигов.");
                }
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

    private async Task CheckInstalledComponentUpdatesAsync()
    {
        var foundAnyUpdate = false;
        var failedComponents = new List<string>();

        if (IsZapretInstalled)
        {
            var zapretUpdate = await GetZapretUpdateInfoAsync(showErrorDialog: false);
            if (zapretUpdate is null)
            {
                failedComponents.Add("zapret");
            }

            if (zapretUpdate is not null && zapretUpdate.IsUpdateAvailable && !string.IsNullOrWhiteSpace(zapretUpdate.DownloadUrl))
            {
                foundAnyUpdate = true;
                await PromptZapretUpdateAsync(zapretUpdate);
            }
        }

        if (IsTgWsProxyInstalled)
        {
            var tgWsProxyUpdate = await GetTgWsProxyUpdateInfoAsync(showErrorDialog: false);
            if (tgWsProxyUpdate is null)
            {
                failedComponents.Add("TG WS Proxy");
            }

            if (tgWsProxyUpdate is not null && tgWsProxyUpdate.IsUpdateAvailable && !string.IsNullOrWhiteSpace(tgWsProxyUpdate.DownloadUrl))
            {
                foundAnyUpdate = true;
                await PromptTgWsProxyUpdateAsync(tgWsProxyUpdate);
            }
        }

        var managerUpdate = await GetManagerUpdateInfoAsync(showErrorDialog: false);
        if (managerUpdate is null)
        {
            failedComponents.Add("ZapretManager");
        }

        if (managerUpdate is not null && managerUpdate.IsUpdateAvailable && !string.IsNullOrWhiteSpace(managerUpdate.DownloadUrl))
        {
            foundAnyUpdate = true;
            await PromptManagerUpdateAsync(managerUpdate, treatAsAutomaticPrompt: false);
            if (_managerUpdateLaunchRequested)
            {
                return;
            }
        }

        if (failedComponents.Count > 0)
        {
            var componentsText = string.Join(", ", failedComponents);
            LastActionText = $"Действие: не удалось проверить обновления: {componentsText}";
            ShowInlineNotification($"Не все обновления удалось проверить: {componentsText}. Остальные компоненты проверены.", isError: true);
        }

        if (!foundAnyUpdate && failedComponents.Count == 0)
        {
            DialogService.ShowInfo("Новых обновлений не найдено.", "Zapret Manager");
        }
    }

    private async Task CheckManagerUpdateAsync()
    {
        await CheckManagerUpdateAsync(showNoUpdatesMessage: true, promptToInstall: true);
    }

    private async Task HandleManagerInstallOrUpdateAsync()
    {
        if (!HasManagerUpdate)
        {
            await CheckManagerUpdateAsync(showNoUpdatesMessage: true, promptToInstall: false);
            return;
        }

        var updateInfo = _pendingStartupManagerUpdateInfo ?? _lastKnownManagerUpdateInfo;
        if (updateInfo is null || !updateInfo.IsUpdateAvailable || string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
        {
            updateInfo = await GetManagerUpdateInfoAsync(showErrorDialog: true);
            if (updateInfo is null || !updateInfo.IsUpdateAvailable || string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
            {
                return;
            }
        }

        _pendingStartupManagerUpdateInfo = updateInfo;
        await PromptManagerUpdateAsync(updateInfo, treatAsAutomaticPrompt: false);
    }

    private async Task CheckManagerUpdateAsync(bool showNoUpdatesMessage, bool promptToInstall, bool showErrorDialog = true)
    {
        var updateInfo = await GetManagerUpdateInfoAsync(showErrorDialog);

        if (updateInfo is null)
        {
            return;
        }

        if (!updateInfo.IsUpdateAvailable)
        {
            _pendingStartupManagerUpdateInfo = null;
            if (showNoUpdatesMessage)
            {
                DialogService.ShowInfo("Новых обновлений программы не найдено.", "Zapret Manager");
            }
            LastActionText = $"Действие: обновлений программы нет, версия {_managerVersion} актуальна";
            return;
        }

        if (!promptToInstall)
        {
            _pendingStartupManagerUpdateInfo = updateInfo;
            LastActionText = $"Действие: найдено обновление программы {updateInfo.LatestVersion}";
            return;
        }

        _pendingStartupManagerUpdateInfo = null;
        await PromptManagerUpdateAsync(updateInfo, showNoUpdatesMessage);
    }

    private async Task<ManagerUpdateInfo?> GetManagerUpdateInfoAsync(bool showErrorDialog)
    {
        EnsureManagerExecutableAvailable();

        ManagerUpdateInfo? updateInfo = null;
        var succeeded = await RunBusyAsync(async () =>
        {
            LastActionText = "Действие: проверяем обновление программы";
            updateInfo = await _managerUpdateService.GetUpdateInfoAsync(_managerVersion);
            if (updateInfo is not null)
            {
                ApplyManagerUpdateInfo(updateInfo);
            }
        }, showErrorDialog: showErrorDialog);

        return succeeded ? updateInfo : null;
    }

    private void ApplyManagerUpdateInfo(ManagerUpdateInfo updateInfo)
    {
        _lastKnownManagerUpdateInfo = updateInfo;
        HasManagerUpdate = updateInfo.IsUpdateAvailable && !string.IsNullOrWhiteSpace(updateInfo.DownloadUrl);
        ManagerUpdateStatus = HasManagerUpdate
            ? $"Обновления программы: доступна версия {updateInfo.LatestVersion}"
            : $"Обновления программы: установлена актуальная версия {updateInfo.CurrentVersion}";
        NotifyManagerPresentationChanged();
    }

    private async Task PromptManagerUpdateAsync(ManagerUpdateInfo updateInfo, bool treatAsAutomaticPrompt = true)
    {
        if (treatAsAutomaticPrompt && _managerUpdatePromptShownThisSession)
        {
            return;
        }

        var shouldInstall = DialogService.ConfirmCustom(
            $"Найдена новая версия программы: {updateInfo.LatestVersion}.{Environment.NewLine}Текущая: {_managerVersion}",
            "Zapret Manager",
            primaryButtonText: "Обновить",
            secondaryButtonText: "Закрыть");
        _managerUpdatePromptShownThisSession = true;

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
        var restartHostedServiceDuringUpdate = serviceStatus.IsInstalled;
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
        _managerUpdateLaunchRequested = true;
        if (System.Windows.Application.Current?.MainWindow is MainWindow mainWindow)
        {
            mainWindow.ShutdownForManagerUpdate();
            return;
        }

        System.Windows.Application.Current?.Shutdown();
    }

    private async Task UninstallProgramAsync()
    {
        EnsureManagerExecutableAvailable();

        var confirmed = DialogService.ConfirmCustom(
            "Будут удалены ZapretManager, zapret, TG WS Proxy, служба, автозапуск, настройки и логи.\n\nПродолжить?",
            "Zapret Manager",
            primaryButtonText: "Удалить",
            secondaryButtonText: "Отмена");
        if (!confirmed)
        {
            return;
        }

        var currentProcessPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentProcessPath))
        {
            throw new InvalidOperationException("Не удалось определить путь к ZapretManager.");
        }

        currentProcessPath = Path.GetFullPath(currentProcessPath);
        var installationForCleanup = _installation;
        if (installationForCleanup is null && !string.IsNullOrWhiteSpace(_settings.LastInstallationPath))
        {
            installationForCleanup = _discoveryService.TryLoad(_settings.LastInstallationPath);
        }

        var tgWsProxyExecutablePath = _resolvedTgWsProxyExecutablePath
            ?? _tgWsProxyService.ResolveExecutablePath(_settings.LastTgWsProxyExecutablePath);
        var extraCleanupPaths = BuildFullRemovalCleanupPaths(installationForCleanup, tgWsProxyExecutablePath);

        try
        {
            await RunBusyAsync(async () =>
            {
                if (IsProbeRunning)
                {
                    RuntimeStatus = "Останавливаем проверку перед удалением программы...";
                    CancelProbe();
                    await WaitForProbeStopAsync(TimeSpan.FromSeconds(40));
                }

                LastActionText = "Действие: подготавливаем удаление программы";
                RuntimeStatus = "Отключаем автозапуск и останавливаем службу...";
                ClearSuspendedServiceRestore();

                try
                {
                    _startupRegistrationService.SetEnabled(false);
                }
                catch
                {
                }

                _settings.StartWithWindowsEnabled = false;
                _settings.CloseToTrayEnabled = false;
                _settings.MinimizeToTrayEnabled = false;
                _settingsService.Save(_settings);

                await _serviceManager.RemoveAsync();
                await WaitForServiceRemovalAsync(TimeSpan.FromSeconds(20));
                await _processService.StopCheckUpdatesShellsAsync();

                if (installationForCleanup is not null)
                {
                    RuntimeStatus = "Останавливаем активные процессы zapret перед удалением программы...";
                    await _processService.StopAsync(installationForCleanup);
                    await WaitForProcessExitAsync(installationForCleanup, TimeSpan.FromSeconds(15));
                }

                RuntimeStatus = "Останавливаем TG WS Proxy и очищаем его автозапуск...";
                if (_tgWsProxyService.IsRunning(tgWsProxyExecutablePath))
                {
                    _tgWsProxyService.Stop(tgWsProxyExecutablePath, TimeSpan.FromSeconds(6));
                    await Task.Delay(350);
                }

                try
                {
                    _tgWsProxyService.SetAutoStartEnabled(tgWsProxyExecutablePath, enabled: false);
                }
                catch
                {
                }

                try
                {
                    await _repositoryMaintenanceService.RemoveManagedHostsBlockAsync();
                }
                catch
                {
                }

                LastActionText = "Действие: запускаем удаление программы";
                RuntimeStatus = "Подготавливаем полное удаление ZapretManager...";
                await _programRemovalService.LaunchPreparedRemovalAsync(
                    currentProcessPath,
                    Process.GetCurrentProcess().Id,
                    WindowsServiceManager.ServiceName,
                    extraCleanupPaths);
            }, rethrowExceptions: true);
        }
        catch (OperationCanceledException)
        {
            LastActionText = "Действие: удаление программы отменено";
            ShowInlineNotification("Удаление программы отменено.", isError: true);
            return;
        }

        if (System.Windows.Application.Current?.MainWindow is MainWindow mainWindow)
        {
            mainWindow.ShutdownForProgramRemoval();
            return;
        }

        System.Windows.Application.Current?.Shutdown();
    }

    private void OpenManagerFolder()
    {
        var managerDirectory = GetManagerInstallationDirectory();
        Directory.CreateDirectory(managerDirectory);

        try
        {
            OpenExternalTarget("explorer.exe", $"\"{managerDirectory}\"");
            LastActionText = "Действие: открываем папку ZapretManager";
        }
        catch (Exception ex)
        {
            DialogService.ShowError(ex, "Zapret Manager");
        }
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

        await ApplyIpSetModeAsync(dialog.SelectedModeValue);
    }

    private async Task ApplyEmbeddedIpSetModeAsync()
    {
        if (SelectedIpSetMode is null)
        {
            return;
        }

        await ApplyIpSetModeAsync(SelectedIpSetMode.Value);
    }

    private async Task ApplyIpSetModeAsync(string targetMode)
    {
        if (_installation is null)
        {
            ShowInlineNotification("Сначала подключите рабочую папку zapret.", isError: true);
            return;
        }

        await RunBusyAsync(() => ApplyIpSetModeCoreAsync(_installation!, targetMode));
    }

    private async Task ApplyIpSetModeCoreAsync(ZapretInstallation installation, string targetMode, bool showNotification = true)
    {
        var serviceStatus = _serviceManager.GetStatus();
        var shouldRestoreService = serviceStatus.IsRunning || ShouldRestoreSuspendedService(installation);
        var restoreProfile = !serviceStatus.IsRunning && _processService.GetRunningProcessCount(installation) > 0
            ? ResolveRestoreProfile()
            : null;

        _ipSetService.SetMode(installation, targetMode);
        LastActionText = $"Действие: режим IPSet изменён на {GetIpSetModeLabel(targetMode)}";

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
        SelectedIpSetMode = IpSetModeOptions.FirstOrDefault(item => string.Equals(item.Value, targetMode, StringComparison.OrdinalIgnoreCase))
            ?? SelectedIpSetMode;

        if (showNotification)
        {
            ShowInlineNotification($"IPSet: {GetIpSetModeLabel(targetMode)}");
        }
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
            SyncEmbeddedNetworkSettings();
        }
        catch (Exception ex)
        {
            LastActionText = "Действие: не удалось открыть настройки DNS";
            DialogService.ShowError(ex, "Zapret Manager");
        }
    }

    private async Task ApplyEmbeddedDnsSettingsAsync()
    {
        try
        {
            EnsureManagerExecutableAvailable();

            if (SelectedDnsProfile is null)
            {
                return;
            }

            var profileKey = SelectedDnsProfile.Key;
            var primary = NormalizeDnsField(DnsPrimaryAddress);
            var secondary = NormalizeDnsField(DnsSecondaryAddress);
            var dohTemplate = NormalizeDnsField(DnsDohUrl);
            var useDoh = !string.Equals(profileKey, DnsService.SystemProfileKey, StringComparison.OrdinalIgnoreCase) &&
                         DnsUseDohEnabled;

            if (string.Equals(profileKey, DnsService.CustomProfileKey, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(primary) && string.IsNullOrWhiteSpace(secondary))
                {
                    DialogService.ShowError("Укажите хотя бы один IPv4-адрес для пользовательского DNS.");
                    return;
                }

                if ((!string.IsNullOrWhiteSpace(primary) && !IsValidIpv4(primary)) ||
                    (!string.IsNullOrWhiteSpace(secondary) && !IsValidIpv4(secondary)))
                {
                    DialogService.ShowError("Пользовательский DNS должен содержать корректные IPv4-адреса.");
                    return;
                }

                if (useDoh && !IsValidHttpsUrl(dohTemplate))
                {
                    DialogService.ShowError("Для пользовательского DoH укажите корректный HTTPS URL.");
                    return;
                }
            }
            else if (useDoh && !IsValidHttpsUrl(dohTemplate))
            {
                DialogService.ShowError("Для выбранного DNS-профиля не найден корректный DoH URL.");
                return;
            }

            _settings.CustomDnsPrimary = primary;
            _settings.CustomDnsSecondary = secondary;
            _settings.CustomDnsDohTemplate = dohTemplate;
            _settings.DnsOverHttpsEnabled = useDoh;
            _settingsService.Save(_settings);

            await ApplyDnsProfileAsync(profileKey, primary, secondary, useDoh, dohTemplate);
            SyncEmbeddedNetworkSettings();
        }
        catch (Exception ex)
        {
            LastActionText = "Действие: не удалось применить DNS";
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

    public async Task OpenTcpFreezeToolAsync()
    {
        try
        {
            LastActionText = "Действие: открываем инструмент DPI TCP 16-20";
            if (_tcpFreezeWindow is not null)
            {
                try
                {
                    _tcpFreezeWindow.Close();
                }
                catch
                {
                }

                _tcpFreezeWindow = null;
            }

            var window = new TcpFreezeWindow(RunTcpFreezeToolAsync, BuildTcpFreezeContext, UseLightThemeEnabled);
            ConfigureAuxiliaryWindow(window);

            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_tcpFreezeWindow, window))
                {
                    _tcpFreezeWindow = null;
                }

                _openAuxiliaryWindows.Remove(window);
                RestoreOwnerAfterAuxiliaryClose(window);
            };

            _tcpFreezeWindow = window;
            _openAuxiliaryWindows.Add(window);
            await window.ShowAndActivateAsync();
        }
        catch (Exception ex)
        {
            _tcpFreezeWindow = null;
            ShowInlineNotification($"Не удалось открыть TCP 16-20: {ex.Message}", isError: true);
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

    public TcpFreezeWindowContext BuildTcpFreezeContext()
    {
        return new TcpFreezeWindowContext
        {
            Configs = GetVisibleProfiles()
                .Select(profile => new TcpFreezeConfigDescriptor
                {
                    ConfigName = profile.Name,
                    FileName = profile.FileName,
                    FilePath = profile.FilePath
                })
                .ToArray(),
            InitiallySelectedFilePath = SelectedConfig?.FilePath
        };
    }

    public async Task<TcpFreezeRunReport> RunTcpFreezeToolAsync(string? selectedConfigPath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (_installation is null)
        {
            throw new InvalidOperationException("Сборка zapret не выбрана.");
        }

        var installation = _installation;
        var profiles = string.IsNullOrWhiteSpace(selectedConfigPath)
            ? GetVisibleProfiles().ToList()
            : GetVisibleProfiles()
                .Where(profile => string.Equals(profile.FilePath, selectedConfigPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(selectedConfigPath)
                ? "Нет доступных конфигов для проверки."
                : "Выбранный конфиг не найден в текущей сборке.");
        }

        var serviceStatus = _serviceManager.GetStatus();
        var shouldRestoreService = serviceStatus.IsRunning || ShouldRestoreSuspendedService(installation);
        var restoreProfile = !serviceStatus.IsRunning && _processService.GetRunningProcessCount(installation) > 0
            ? ResolveRestoreProfile()
            : null;

        var tcpTotalTargets = 0;
        var tcpProcessedTargets = 0;
        var tcpStartedProfiles = 0;
        string? tcpCurrentProfileName = null;
        var tcpSuiteCountRegex = new Regex(@"suite TCP 16-20:\s+(?<count>\d+)\s+целей", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var tcpConfigHeaderRegex = new Regex(@"^===\s+(?<name>.+)\s+===$", RegexOptions.CultureInvariant);
        var tcpTargetLineRegex = new Regex(@"\[(?<id>[^\]]+)\]\s+(?<host>\S+)\s+->\s+(?<details>.+)$", RegexOptions.CultureInvariant);

        IProgress<string> overlayAwareProgress = new Progress<string>(line =>
        {
            progress.Report(line);

            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var suiteMatch = tcpSuiteCountRegex.Match(line);
            if (suiteMatch.Success && int.TryParse(suiteMatch.Groups["count"].Value, out var suiteCount))
            {
                tcpTotalTargets = suiteCount;
                BusyEtaText = $"Целей для проверки: {suiteCount}.";
                return;
            }

            var headerMatch = tcpConfigHeaderRegex.Match(line);
            if (headerMatch.Success &&
                !string.Equals(headerMatch.Groups["name"].Value, "Сводка", StringComparison.OrdinalIgnoreCase))
            {
                tcpCurrentProfileName = headerMatch.Groups["name"].Value.Trim();
                tcpProcessedTargets = 0;
                tcpStartedProfiles = Math.Min(tcpStartedProfiles + 1, profiles.Count);
                BusyEtaText = profiles.Count > 1
                    ? $"Конфиг {tcpStartedProfiles} из {profiles.Count}: {tcpCurrentProfileName}."
                    : $"Проверяем {tcpCurrentProfileName}.";
                return;
            }

            var targetMatch = tcpTargetLineRegex.Match(line);
            if (targetMatch.Success)
            {
                tcpProcessedTargets++;
                if (!string.IsNullOrWhiteSpace(tcpCurrentProfileName) && tcpTotalTargets > 0)
                {
                    BusyEtaText = $"{tcpCurrentProfileName}: {tcpProcessedTargets} из {tcpTotalTargets} целей.";
                }
            }
        });

        TcpFreezeRunReport? report = null;
        try
        {
            var profileDescription = profiles.Count == 1
                ? $"конфига {profiles[0].Name}"
                : $"{profiles.Count} конфигов";

            await RunBusyAsync(async () =>
            {
                LastActionText = $"Действие: запускаем TCP 16-20 для {profileDescription}";
                RuntimeStatus = "Подготавливаем TCP 16-20 проверку...";
                overlayAwareProgress.Report($"Выбрано для теста: {profileDescription}.");

                if (serviceStatus.IsRunning)
                {
                    RememberRunningServiceProfile(serviceStatus);
                    MarkSuspendedServiceForRestore(installation);
                    overlayAwareProgress.Report("Останавливаем установленную службу перед тестом...");
                    await _serviceManager.StopAsync();
                    await Task.Delay(1000, cancellationToken);
                }
                else if (ShouldRestoreSuspendedService(installation))
                {
                    overlayAwareProgress.Report("Служба уже приостановлена. После теста она будет возвращена.");
                }
                else if (restoreProfile is not null)
                {
                    overlayAwareProgress.Report($"Временно остановим вручную запущенный профиль {restoreProfile.Name}.");
                }

                overlayAwareProgress.Report("Останавливаем связанные процессы текущей сборки...");
                await StopRelatedInstallationsAsync(installation.RootPath, waitForDrivers: false, cancellationToken);
                await Task.Delay(500, cancellationToken);

                RuntimeStatus = "Проверка TCP 16-20 выполняется...";
                report = await _dpiTcpFreezeTestService.RunAsync(installation, profiles, overlayAwareProgress, cancellationToken);
                LastActionText = $"Действие: TCP 16-20 завершён для {profileDescription}";
            }, rethrowExceptions: true, showErrorDialog: false);
        }
        finally
        {
            if (_installation is not null)
            {
                if (shouldRestoreService)
                {
                    overlayAwareProgress.Report("Возвращаем установленную службу...");
                    await RestoreSuspendedServiceIfNeededAsync();
                }
                else if (restoreProfile is not null)
                {
                    overlayAwareProgress.Report($"Возвращаем профиль {restoreProfile.Name}...");
                    await _processService.StartAsync(_installation, restoreProfile);
                    _settings.LastStartedConfigPath = restoreProfile.FilePath;
                    _settingsService.Save(_settings);
                    await Task.Delay(1200);
                }

                await RefreshLiveStatusCoreAsync();
            }
        }

        return report ?? new TcpFreezeRunReport
        {
            ConfigResults = [],
            RecommendedConfigPath = null
        };
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

        await RunBusyAsync(() => UpdateIpSetListCoreAsync(_installation!));
    }

    private async Task UpdateHostsFileAsync()
    {
        await UpdateHostsFileAsync(confirmBeforeApply: true);
    }

    private async Task UpdateIpSetListCoreAsync(ZapretInstallation installation, bool showNotification = true)
    {
        var currentMode = _ipSetService.GetModeValue(installation);
        var shouldApplyImmediately = string.Equals(currentMode, "loaded", StringComparison.OrdinalIgnoreCase);

        RuntimeStatus = "Скачиваем актуальный список IPSet...";
        LastActionText = "Действие: обновляем список IPSet";

        var updateResult = await _repositoryMaintenanceService.UpdateIpSetListAsync(installation);
        await RestartBypassForIpSetChangesAsync(installation, shouldApplyImmediately);
        var activeEntryCount = _repositoryMaintenanceService.GetActiveIpSetEntryCount(installation);
        await RefreshLiveStatusCoreAsync();

        LastActionText = updateResult.AppliedToActiveList
            ? "Действие: список IPSet обновлён и применён"
            : "Действие: список IPSet обновлён";

        if (showNotification)
        {
            ShowInlineNotification(updateResult.AppliedToActiveList
                ? $"IPSet обновлён и применён сразу. Записей: {activeEntryCount}."
                : $"IPSet обновлён. Записей: {updateResult.EntryCount}. Он включится при режиме 'по списку'.");
        }
    }

    private async Task UpdateHostsFileAsync(bool confirmBeforeApply)
    {
        if (confirmBeforeApply)
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
        }

        await RunBusyAsync(() => UpdateHostsFileCoreAsync());
    }

    private async Task UpdateHostsFileCoreAsync(bool showNotification = true)
    {
        RuntimeStatus = "Обновляем системный hosts...";
        LastActionText = "Действие: обновляем hosts для zapret";

        var updateResult = await _repositoryMaintenanceService.UpdateHostsFileAsync();
        var managedHostsEntryCount = _repositoryMaintenanceService.GetManagedHostsEntryCount();
        await RefreshLiveStatusCoreAsync();
        LastActionText = "Действие: hosts для zapret обновлён";
        if (showNotification)
        {
            ShowInlineNotification(
                $"Hosts обновлён: заменено {updateResult.ReplacedEntryCount} старых строк, добавлено {managedHostsEntryCount} актуальных.");
        }
    }

    private async Task ApplyRecommendedUpdateFixesAsync()
    {
        try
        {
            IsBusy = true;

            var installation = _installation;
            if (installation is not null)
            {
                RuntimeStatus = "Применяем исправления для обновления...";
                LastActionText = "Действие: применяем исправления для обновления";

                await _repositoryMaintenanceService.UpdateIpSetListAsync(installation);
                await ApplyIpSetModeCoreAsync(installation, "loaded", showNotification: false);
                await UpdateHostsFileCoreAsync(showNotification: false);

                LastActionText = "Действие: исправления для обновления применены";
                ShowInlineNotification("Исправления применены: IPSet обновлён, режим 'по списку' включён, hosts обновлён.");
                return;
            }

            RuntimeStatus = "Применяем доступные исправления...";
            LastActionText = "Действие: применяем исправления для обновления";
            await UpdateHostsFileCoreAsync(showNotification: false);
            LastActionText = "Действие: доступные исправления применены";
            ShowInlineNotification("Исправления применены: hosts обновлён.");
        }
        catch (Exception ex)
        {
            var displayMessage = DialogService.GetDisplayMessage(ex);
            LastActionText = $"Действие: исправления не применены - {DialogService.GetShortDisplayMessage(displayMessage)}";
            DialogService.ShowError(displayMessage, "Zapret Manager");
        }
        finally
        {
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    private void HideSelectedConfig()
    {
        if (_installation is null)
        {
            return;
        }

        var profilesToHide = GetSelectedProfilesForHide();
        if (profilesToHide.Count == 0)
        {
            return;
        }

        var serviceStatus = _serviceManager.GetStatus();
        var serviceConflicts = profilesToHide
            .Where(profile => serviceStatus.IsInstalled &&
                              string.Equals(serviceStatus.ProfileName, profile.Name, StringComparison.OrdinalIgnoreCase))
            .Select(profile => profile.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (serviceConflicts.Length > 0)
        {
            var conflictLabel = serviceConflicts.Length == 1
                ? $"конфига {serviceConflicts[0]}"
                : $"выбранных конфигов: {string.Join(", ", serviceConflicts)}";
            ShowInlineNotification($"Сначала удалите службу для {conflictLabel}, а потом скрывайте.", isError: true);
            return;
        }

        var hasRunningProfiles = _processService.GetRunningProcessCount(_installation) > 0;
        var activeConflicts = profilesToHide
            .Where(profile => hasRunningProfiles &&
                              string.Equals(_settings.LastStartedConfigPath, profile.FilePath, StringComparison.OrdinalIgnoreCase))
            .Select(profile => profile.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (activeConflicts.Length > 0)
        {
            var conflictLabel = activeConflicts.Length == 1
                ? $"профиль {activeConflicts[0]}"
                : $"выбранные профили: {string.Join(", ", activeConflicts)}";
            ShowInlineNotification($"Сначала остановите {conflictLabel}, а потом скрывайте.", isError: true);
            return;
        }

        if (!_settings.SkipHideConfigConfirmation)
        {
            var confirmationMessage = profilesToHide.Count == 1
                ? $"Скрыть конфиг {profilesToHide[0].Name}? Он исчезнет из основного списка и не будет участвовать в проверке."
                : $"Скрыть выбранные конфиги ({profilesToHide.Count} шт.)? Они исчезнут из основного списка и не будут участвовать в проверке.";
            var confirmation = DialogService.ConfirmWithRemember(
                confirmationMessage,
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

        foreach (var profile in profilesToHide)
        {
            if (!_settings.HiddenConfigPaths.Any(path =>
                    string.Equals(path, profile.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.HiddenConfigPaths.Add(profile.FilePath);
            }

            _probeResults.Remove(profile.Name);
        }

        _settingsService.Save(_settings);
        RebuildConfigRows();

        if (profilesToHide.Count == 1)
        {
            LastActionText = $"Действие: конфиг {profilesToHide[0].Name} скрыт";
            ShowInlineNotification($"Конфиг {profilesToHide[0].Name} скрыт.");
        }
        else
        {
            LastActionText = $"Действие: скрыты {profilesToHide.Count} выбранных конфигов";
            ShowInlineNotification($"Скрыты {profilesToHide.Count} выбранных конфигов.");
        }
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

    private async Task ToggleSelectedServiceAsync()
    {
        if (_installation is null || SelectedConfig is null)
        {
            return;
        }

        if (IsSelectedConfigInstalledAsService)
        {
            await RemoveServiceAsync();
        }
        else
        {
            await InstallServiceAsync();
        }
    }

    private bool IsSelectedConfigInstalledAsServiceCore()
    {
        if (_installation is null || SelectedConfig is null)
        {
            return false;
        }

        var serviceStatus = _serviceManager.GetStatus();
        if (!serviceStatus.IsInstalled)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(serviceStatus.InstallationRootPath) &&
            !PathsEqual(serviceStatus.InstallationRootPath, _installation.RootPath))
        {
            return false;
        }

        var installedProfile = FindProfileByIdentity(_installation, serviceStatus.ProfileName, serviceStatus.ProfileToken);

        return installedProfile is not null &&
               string.Equals(installedProfile.FilePath, SelectedConfig.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeDnsField(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsValidIpv4(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ||
               (System.Net.IPAddress.TryParse(value, out var address) &&
                address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
    }

    private static bool IsValidHttpsUrl(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
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
            RememberInstalledServiceProfile(profile: null, saveImmediately: false);
            _settingsService.Save(_settings);
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

    private async Task RunTestsAsync(
        IReadOnlyList<ConfigProfile> profilesToProbe,
        bool allowDnsSuggestion = true,
        bool isAutomaticMode = false,
        string runDescription = "проверку конфигов")
    {
        if (_installation is null)
        {
            return;
        }

        if (profilesToProbe.Count == 0)
        {
            RecommendedConfigText = "Рекомендуемый конфиг: нет профилей для проверки";
            LastActionText = "Действие: нет профилей для проверки";
            return;
        }

        var shouldRestoreService = false;
        ConfigProfile? restoreProfile = null;
        var shouldOfferDnsSuggestion = false;
        var wasCancelled = false;
        var failedWithException = false;
        var targetArg = string.IsNullOrWhiteSpace(ManualTarget) ? null : ManualTarget.Trim();
        var probeDohTemplate = ResolveCurrentProbeDohTemplate();

        try
        {
            IsBusy = true;
            IsProbeRunning = true;
            _probeCancellation = new CancellationTokenSource();
            _probeResults.Clear();
            RebuildConfigRows();
            RecommendedConfigText = "Рекомендуемый конфиг: идёт проверка...";
            RuntimeStatus = "Готовим проверку конфигов...";
            LastActionText = $"Действие: запускаем {runDescription}";

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

            BeginProbeProgress(profilesToProbe.Count, shouldRestoreService || restoreProfile is not null);
            for (var index = 0; index < profilesToProbe.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var profile = profilesToProbe[index];
                RuntimeStatus = $"Проверяем {profile.Name} ({index + 1}/{profilesToProbe.Count})";
                LastActionText = $"Действие: проверяем конфиг {profile.Name} ({index + 1}/{profilesToProbe.Count})";
                MarkProbeProfileStarted(index);

                var result = await _connectivityTestService.ProbeConfigAsync(_installation, profile, targetArg, probeDohTemplate, cancellationToken);
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
            RuntimeStatus = profilesToProbe.Count == 1
                ? "Проверка выбранного конфига завершена"
                : "Проверка конфигов завершена";
            BusyEtaText = shouldRestoreService || restoreProfile is not null
                ? "Осталось примерно: меньше 10 сек."
                : string.Empty;
            BusyProgressValue = 100;
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            RuntimeStatus = profilesToProbe.Count == 1
                ? "Проверка выбранного конфига остановлена пользователем"
                : "Проверка конфигов остановлена пользователем";
            RecommendedConfigText = "Рекомендуемый конфиг: проверка остановлена";
            LastActionText = profilesToProbe.Count == 1
                ? "Действие: проверка выбранного конфига отменена"
                : "Действие: проверка конфигов отменена";
        }
        catch (Exception ex)
        {
            failedWithException = true;
            LastActionText = "Действие: ошибка проверки конфигов";
            DialogService.ShowError(ex, "Zapret Manager");
        }
        finally
        {
            SaveProbeProfileEstimate();

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
            _installation is not null &&
            ShouldDiagnoseDnsIssue())
        {
            var dnsDiagnosis = await DiagnoseDnsIssueAsync(_installation, targetArg);
            if (!string.IsNullOrWhiteSpace(dnsDiagnosis))
            {
                if (allowDnsSuggestion &&
                    shouldOfferDnsSuggestion &&
                    await TrySuggestDnsAndRerunAsync(isAutomaticMode, dnsDiagnosis))
                {
                    return;
                }

                if (!string.Equals(GetCurrentDnsProfileKey(), DnsService.SystemProfileKey, StringComparison.OrdinalIgnoreCase))
                {
                    ShowInlineNotification(
                        $"{dnsDiagnosis} Проверка использует системный DNS/DoH, поэтому результат может отличаться от браузера с Secure DNS.",
                        isError: true,
                        durationMs: 8200);
                }
            }
        }
    }

    private async Task RunAutomaticInstallAsync()
    {
        if (IsProbeRunning)
        {
            LastActionText = "Действие: нельзя запускать автоматический режим во время проверки";
            return;
        }

        var shouldRunAutomaticInstall = DialogService.ConfirmCustom(
            "Автоматический режим проверит или подключит сборку zapret, подготовит окружение, запустит проверку конфигов и при хорошем результате сразу установит лучший конфиг как службу.",
            "Автоматический режим");
        if (!shouldRunAutomaticInstall)
        {
            LastActionText = "Действие: автоматический режим отменён пользователем";
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
                UpdateOperationResult? installResult = null;
                var managedComponentsRootPath = GetManagedComponentsRootPath();
                await RunBusyAsync(async () =>
                {
                    UpdateStatus = "Обновления: скачиваем свежую сборку для автоматического режима...";
                    RuntimeStatus = "Автоматический режим: скачиваем zapret...";
                    LastActionText = "Действие: автоматический режим скачивает свежий zapret";
                    installResult = await _updateService.InstallFreshAsync(managedComponentsRootPath);
                    UpdateStatus = $"Обновления: установлена версия {installResult.InstalledVersion}";
                });

                if (installResult is null)
                {
                    return;
                }

                var installation = _discoveryService.TryLoad(installResult.ActiveRootPath)
                                  ?? throw new InvalidOperationException("Свежая сборка скачалась, но её не удалось подключить.");

                await SelectInstallationAsync(installation, $"Действие: автоматический режим подключил {installResult.ActiveRootPath}");
                ShowInlineNotification($"Автоматический режим установил zapret в: {installResult.ActiveRootPath}");
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

        await RunTestsAsync(GetVisibleProfiles().ToList(), allowDnsSuggestion: true, isAutomaticMode: true);

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

    private async Task RunAutomaticTgWsProxyModeAsync()
    {
        if (!TryBuildTgWsProxyConfigFromInputs(out var config, out var errorMessage))
        {
            DialogService.ShowError(errorMessage, "Zapret Manager");
            return;
        }

        var shouldRunAutomaticTgMode = DialogService.ConfirmCustom(
            "Автоматический режим TG WS Proxy при необходимости обновит или установит прокси, сохранит текущие настройки, запустит его и откроет Telegram с готовой ссылкой на подключение.",
            "Автоматический режим TG WS Proxy");
        if (!shouldRunAutomaticTgMode)
        {
            LastActionText = "Действие: автоматический режим TG WS Proxy отменён пользователем";
            return;
        }

        config.AutoStart = true;
        TgWsProxyAutoStartEnabled = true;

        TgWsProxyReleaseInfo? updateInfo = null;
        string? executablePath = null;
        var completed = await RunBusyAsync(async () =>
        {
            LastActionText = "Действие: запускаем автоматический режим TG WS Proxy";

            executablePath = _tgWsProxyService.ResolveExecutablePath(_settings.LastTgWsProxyExecutablePath);
            PersistResolvedTgWsProxyExecutablePath(executablePath);

            var needsInstallOrUpdate = string.IsNullOrWhiteSpace(executablePath) || TgWsProxyHasUpdate;
            if (needsInstallOrUpdate)
            {
                updateInfo = await _tgWsProxyService.GetReleaseInfoAsync(_tgWsProxyService.GetInstalledVersion(executablePath));
                ApplyTgWsProxyReleaseInfo(updateInfo);

                if (string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
                {
                    throw new InvalidOperationException("GitHub не вернул exe-файл TG WS Proxy для скачивания.");
                }

                var downloadedPath = await _tgWsProxyService.DownloadReleaseAsync(
                    updateInfo.DownloadUrl,
                    updateInfo.LatestVersion);
                var targetExecutablePath = _tgWsProxyService.GetInstallTargetPath(executablePath);
                var wasRunningBeforeInstall = _tgWsProxyService.IsRunning(executablePath);

                if (wasRunningBeforeInstall)
                {
                    _tgWsProxyService.Stop(executablePath, TimeSpan.FromSeconds(6));
                    await Task.Delay(400);
                }

                await _tgWsProxyService.InstallDownloadedReleaseAsync(downloadedPath, targetExecutablePath);
                executablePath = targetExecutablePath;
                PersistResolvedTgWsProxyExecutablePath(targetExecutablePath);

                _tgWsProxyLatestVersion = updateInfo.LatestVersion;
                TgWsProxyHasUpdate = false;
                TgWsProxyUpdateStatus = $"Обновления TG WS Proxy: установлена актуальная версия {FormatTgWsProxyVersionLabel(updateInfo.LatestVersion, prefixWithV: true)}";
            }

            _tgWsProxyService.SaveConfig(config);
            SynchronizeTgWsProxyAutoStartPreference(config.AutoStart, allowRegistryUpdate: true);

            executablePath ??= _tgWsProxyService.ResolveExecutablePath(_settings.LastTgWsProxyExecutablePath);
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("Не удалось подготовить TG WS Proxy для автоматического режима.");
            }

            if (_tgWsProxyService.IsRunning(executablePath))
            {
                _tgWsProxyService.Stop(executablePath, TimeSpan.FromSeconds(6));
                await Task.Delay(350);
            }

            _tgWsProxyService.Start(executablePath);
            RefreshTgWsProxyStatusCore();
            LastActionText = updateInfo is null
                ? "Действие: автоматический режим TG WS Proxy сохранил настройки и запустил прокси"
                : $"Действие: автоматический режим TG WS Proxy обновил прокси до {FormatTgWsProxyVersionLabel(updateInfo.LatestVersion, prefixWithV: true)} и запустил его";
        });

        if (!completed)
        {
            return;
        }

        try
        {
            OpenExternalTarget(BuildTgWsProxyLinkPreview(config));
            LastActionText = "Действие: автоматический режим TG WS Proxy открыл Telegram для добавления прокси";
            ShowInlineNotification(
                updateInfo is null
                    ? "TG WS Proxy настроен, запущен и передан в Telegram."
                    : $"TG WS Proxy обновлён до {FormatTgWsProxyVersionLabel(updateInfo.LatestVersion, prefixWithV: true)}, запущен и передан в Telegram.");
        }
        catch (Exception ex)
        {
            DialogService.ShowError(ex, "Zapret Manager");
        }
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

    private bool ShouldDiagnoseDnsIssue()
    {
        if (_probeResults.Count == 0)
        {
            return false;
        }

        return _probeResults.Values.All(result =>
            result.PrimaryTotalCount > 0 &&
            result.PrimaryFailedTargetNames.Count > 0);
    }

    private bool ShouldOfferDnsSuggestion()
    {
        if (!ShouldDiagnoseDnsIssue() ||
            !string.Equals(GetCurrentDnsProfileKey(), DnsService.SystemProfileKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private async Task<bool> TrySuggestDnsAndRerunAsync(bool isAutomaticMode, string diagnosis)
    {
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
        await RunTestsAsync(GetVisibleProfiles().ToList(), allowDnsSuggestion: false, isAutomaticMode);
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

        _ = RunTestsAsync(GetVisibleProfiles().ToList(), allowDnsSuggestion: true, isAutomaticMode: false, runDescription: "проверку конфигов");
    }

    private void ToggleSelectedProbe()
    {
        if (IsProbeRunning || _installation is null)
        {
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

        var selectedProfiles = GetSelectedProfilesForProbe();
        if (selectedProfiles.Count == 0)
        {
            return;
        }

        var runDescription = selectedProfiles.Count == 1
            ? $"проверку конфига {selectedProfiles[0].Name}"
            : $"проверку {selectedProfiles.Count} выбранных конфигов";

        _ = RunTestsAsync(selectedProfiles, allowDnsSuggestion: true, isAutomaticMode: false, runDescription: runDescription);
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

    private IReadOnlyList<ConfigProfile> GetSelectedProfilesForProbe()
    {
        var visibleProfiles = GetVisibleProfiles().ToList();
        if (visibleProfiles.Count == 0)
        {
            return [];
        }

        if (_selectedConfigPaths.Count > 0)
        {
            var selectedProfiles = visibleProfiles
                .Where(item => _selectedConfigPaths.Contains(item.FilePath))
                .ToList();

            if (selectedProfiles.Count > 0)
            {
                return selectedProfiles;
            }
        }

        return SelectedConfig is null ? [] : [SelectedConfig];
    }

    private IReadOnlyList<ConfigProfile> GetSelectedProfilesForHide()
    {
        return GetSelectedProfilesForProbe();
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
                SummaryBadgeText = BuildSummaryBadgeText(probeResult),
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

    private async Task CheckUpdatesAsync(bool showNoUpdatesMessage, bool promptToInstall, bool showErrorDialog = true)
    {
        if (_installation is null)
        {
            return;
        }

        var updateInfo = await GetZapretUpdateInfoAsync(showErrorDialog);
        if (updateInfo is null)
        {
            return;
        }

        if (!updateInfo.IsUpdateAvailable)
        {
            _pendingStartupZapretUpdateInfo = null;
            if (showNoUpdatesMessage)
            {
                DialogService.ShowInfo("Новых обновлений не найдено.", "Zapret Manager");
            }

            return;
        }

        if (!promptToInstall)
        {
            _pendingStartupZapretUpdateInfo = updateInfo;
            return;
        }

        _pendingStartupZapretUpdateInfo = null;
        await PromptZapretUpdateAsync(updateInfo);
    }

    private async Task<bool> RefreshUpdateAvailabilityAsync(bool showNoUpdatesMessage, bool showErrorDialog = true)
    {
        var updateInfo = await GetZapretUpdateInfoAsync(showErrorDialog);
        if (updateInfo is null)
        {
            return false;
        }

        if (!updateInfo.IsUpdateAvailable && showNoUpdatesMessage)
        {
            DialogService.ShowInfo("Новых обновлений не найдено.", "Zapret Manager");
        }

        return updateInfo.IsUpdateAvailable;
    }

    private async Task<UpdateInfo?> GetZapretUpdateInfoAsync(bool showErrorDialog)
    {
        if (_installation is null)
        {
            return null;
        }

        var installationVersion = _installation.Version;
        UpdateInfo? updateInfo = null;
        var succeeded = await RunBusyAsync(async () =>
        {
            UpdateStatus = "Обновления: проверяем...";
            LastActionText = "Действие: проверяем обновления";
            updateInfo = await _updateService.GetUpdateInfoAsync(installationVersion);
            _updateDownloadUrl = updateInfo.DownloadUrl;
            _updateLatestVersion = updateInfo.LatestVersion;
            HasUpdate = updateInfo.IsUpdateAvailable && !string.IsNullOrWhiteSpace(updateInfo.DownloadUrl);

            if (HasUpdate)
            {
                UpdateStatus = $"Обновления: доступна версия {updateInfo.LatestVersion}";
                LastActionText = $"Действие: найдено обновление {updateInfo.LatestVersion}";
            }
            else
            {
                UpdateStatus = $"Обновления: актуальная версия {installationVersion}";
                LastActionText = $"Действие: обновлений нет, версия {installationVersion} актуальна";
            }

            NotifyZapretPresentationChanged();
        }, showErrorDialog: showErrorDialog);

        return succeeded ? updateInfo : null;
    }

    private async Task PromptZapretUpdateAsync(UpdateInfo updateInfo)
    {
        if (_installation is null)
        {
            return;
        }

        _updateDownloadUrl = updateInfo.DownloadUrl;
        _updateLatestVersion = updateInfo.LatestVersion;
        HasUpdate = updateInfo.IsUpdateAvailable && !string.IsNullOrWhiteSpace(updateInfo.DownloadUrl);

        if (!HasUpdate || string.IsNullOrWhiteSpace(_updateLatestVersion))
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
            await CheckUpdatesAsync(showNoUpdatesMessage: false, promptToInstall: false);
        }

        if (updateResult is null)
        {
            return;
        }

        var previousVersionNote = updateResult.PreviousVersionWasBusy
            ? string.IsNullOrWhiteSpace(updateResult.PreviousVersionBusyProcessSummary)
                ? "Старая папка была занята другим процессом, поэтому новая сборка установлена рядом без сохранения версии для отката."
                : $"Старая папка была занята процессом: {updateResult.PreviousVersionBusyProcessSummary}. Новая сборка установлена рядом без сохранения версии для отката."
            : string.IsNullOrWhiteSpace(updateResult.BackupRootPath)
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
        var currentServiceStatus = _serviceManager.GetStatus();
        var shouldRestoreInstalledService = currentServiceStatus.IsInstalled;
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
        var installedServiceProfileName = currentServiceStatus.ProfileName
            ?? installedServiceProfile?.Name
            ?? _settings.LastInstalledServiceProfileName;
        var installedServiceProfileFileName = currentServiceStatus.ProfileToken
            ?? installedServiceProfile?.FileName
            ?? _settings.LastInstalledServiceProfileFileName;

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

                if (shouldRestoreInstalledService)
                {
                    var restoredServiceProfile = FindProfileByIdentity(
                        _installation,
                        installedServiceProfileName,
                        installedServiceProfileFileName);

                    if (restoredServiceProfile is not null)
                    {
                        RuntimeStatus = $"Откат: возвращаем службу для {restoredServiceProfile.Name}...";
                        await _serviceManager.InstallAsync(_installation, restoredServiceProfile);
                        _settings.LastStartedConfigPath = restoredServiceProfile.FilePath;
                        RememberInstalledServiceProfile(restoredServiceProfile, saveImmediately: false);
                        _settingsService.Save(_settings);
                        await Task.Delay(1200);
                    }
                    else
                    {
                        LastActionText = "Действие: откат завершён, но профиль службы не найден в восстановленной версии";
                    }
                }
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
            await CheckUpdatesAsync(showNoUpdatesMessage: false, promptToInstall: false);
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
            ? "Все домены из targets.txt"
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
            return "Домены из targets.txt, пресеты YouTube/Discord/Cloudflare или ваш набор доменов.";
        }

        var targets = _connectivityTestService.LoadTargets(_installation, null);
        var mainCount = targets.Count(item => !item.IsDiagnosticOnly);
        var diagnosticCount = targets.Count(item => item.IsDiagnosticOnly);

        return mainCount > 0
            ? $"Доменов в списке: {mainCount}. Ping-диагностика: {diagnosticCount}. Пресеты: YouTube, Discord, Cloudflare."
            : "Файл targets.txt пуст или не найден. Используйте быстрые пресеты или свой список доменов.";
    }

    private bool IsSelectedConfigRunningCore()
    {
        if (_installation is null || SelectedConfig is null || IsBusy)
        {
            return false;
        }

        if (!string.Equals(_settings.LastStartedConfigPath, SelectedConfig.FilePath, StringComparison.OrdinalIgnoreCase))
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

    private async Task<bool> RunBusyAsync(Func<Task> action, bool rethrowExceptions = false, bool showErrorDialog = true)
    {
        try
        {
            IsBusy = true;
            await action();
            return true;
        }
        catch (Exception ex)
        {
            var displayMessage = DialogService.GetDisplayMessage(ex);
            LastActionText = $"Действие: ошибка - {displayMessage}";
            if (rethrowExceptions)
            {
                throw;
            }

            if (showErrorDialog)
            {
                if (NetworkErrorTranslator.ContainsGitHubRoutingHint(displayMessage))
                {
                    var choice = DialogService.ChooseErrorCustom(
                        displayMessage,
                        "Zapret Manager",
                        primaryButtonText: "Применить исправления",
                        secondaryButtonText: "Закрыть");

                    if (choice == DialogService.DialogChoice.Primary)
                    {
                        await ApplyRecommendedUpdateFixesAsync();
                    }
                }
                else
                {
                    DialogService.ShowError(displayMessage, "Zapret Manager");
                }
            }
        }
        finally
        {
            IsBusy = false;
            RaiseCommandStates();
        }

        return false;
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

    private string? ResolveCurrentProbeDohTemplate()
    {
        try
        {
            var profiles = _dnsService.GetPresetDefinitions(
                _settings.CustomDnsPrimary,
                _settings.CustomDnsSecondary,
                _settings.CustomDnsDohTemplate);
            var currentStatus = _dnsService.GetCurrentStatus();
            var matchedKey = _dnsService.MatchPresetKey(currentStatus, _settings.CustomDnsPrimary, _settings.CustomDnsSecondary);
            var profile = !string.IsNullOrWhiteSpace(matchedKey)
                ? profiles.FirstOrDefault(item => string.Equals(item.Key, matchedKey, StringComparison.OrdinalIgnoreCase))
                : null;

            profile ??= profiles.FirstOrDefault(item =>
                string.Equals(item.Key, GetCurrentDnsProfileKey(), StringComparison.OrdinalIgnoreCase));

            return string.IsNullOrWhiteSpace(profile?.DohTemplate)
                ? null
                : profile.DohTemplate;
        }
        catch
        {
            return null;
        }
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
        HandleZapretInstallOrUpdateCommand.NotifyCanExecuteChanged();
        CheckInstalledComponentUpdatesCommand.NotifyCanExecuteChanged();
        HomeCheckAllCommand.NotifyCanExecuteChanged();
        OpenTargetsFileCommand.NotifyCanExecuteChanged();
        OpenIncludedDomainsEditorCommand.NotifyCanExecuteChanged();
        OpenExcludedDomainsEditorCommand.NotifyCanExecuteChanged();
        OpenHostsEditorCommand.NotifyCanExecuteChanged();
        OpenUserSubnetsEditorCommand.NotifyCanExecuteChanged();
        OpenHiddenConfigsCommand.NotifyCanExecuteChanged();
        OpenIpSetModeCommand.NotifyCanExecuteChanged();
        OpenDnsSettingsCommand.NotifyCanExecuteChanged();
        OpenDiagnosticsCommand.NotifyCanExecuteChanged();
        OpenTcpFreezeToolCommand.NotifyCanExecuteChanged();
        OpenAboutCommand.NotifyCanExecuteChanged();
        SaveTgWsProxyConfigCommand.NotifyCanExecuteChanged();
        CheckTgWsProxyUpdateCommand.NotifyCanExecuteChanged();
        InstallTgWsProxyCommand.NotifyCanExecuteChanged();
        ResetTgWsProxyEditorCommand.NotifyCanExecuteChanged();
        AddTgWsProxyToTelegramCommand.NotifyCanExecuteChanged();
        TestTgWsProxyCfProxyCommand.NotifyCanExecuteChanged();
        CheckManagerUpdateCommand.NotifyCanExecuteChanged();
        UpdateIpSetListCommand.NotifyCanExecuteChanged();
        UpdateHostsFileCommand.NotifyCanExecuteChanged();
        ClearDiscordCacheCommand.NotifyCanExecuteChanged();
        GenerateTgWsProxySecretCommand.NotifyCanExecuteChanged();
        CopyTgWsProxyLinkCommand.NotifyCanExecuteChanged();
        OpenTgWsProxyLinkCommand.NotifyCanExecuteChanged();
        LaunchTgWsProxyCommand.NotifyCanExecuteChanged();
        StopTgWsProxyCommand.NotifyCanExecuteChanged();
        ToggleTgWsProxyAutoStartCommand.NotifyCanExecuteChanged();
        OpenTgWsProxyLogsCommand.NotifyCanExecuteChanged();
        OpenTgWsProxyFolderCommand.NotifyCanExecuteChanged();
        DeleteTgWsProxyCommand.NotifyCanExecuteChanged();
        OpenTgWsProxyReleasePageCommand.NotifyCanExecuteChanged();
        OpenTgWsProxyGuideCommand.NotifyCanExecuteChanged();
        HandleTgWsProxyInstallOrUpdateCommand.NotifyCanExecuteChanged();
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
        AutoConfigureTgWsProxyCommand.NotifyCanExecuteChanged();
        InstallServiceCommand.NotifyCanExecuteChanged();
        RemoveServiceCommand.NotifyCanExecuteChanged();
        ToggleSelectedServiceCommand.NotifyCanExecuteChanged();
        RunTestsCommand.NotifyCanExecuteChanged();
        RunSelectedTestCommand.NotifyCanExecuteChanged();
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        ApplyUpdateCommand.NotifyCanExecuteChanged();
        RestorePreviousVersionCommand.NotifyCanExecuteChanged();
        UninstallProgramCommand.NotifyCanExecuteChanged();
        HandleManagerInstallOrUpdateCommand.NotifyCanExecuteChanged();
        OpenManagerFolderCommand.NotifyCanExecuteChanged();
        ApplyGameModeCommand.NotifyCanExecuteChanged();
        ApplyDnsSettingsCommand.NotifyCanExecuteChanged();
        ApplyIpSetModeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsSelectedConfigRunning));
        OnPropertyChanged(nameof(IsSelectedConfigInstalledAsService));
        OnPropertyChanged(nameof(SelectedServiceActionText));
        NotifyHomeDashboardChanged();
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
            await CheckUpdatesAsync(showNoUpdatesMessage: false, promptToInstall: false);
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

    private static bool PathsEqual(string? leftPath, string? rightPath)
    {
        if (string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
        {
            return false;
        }

        var normalizedLeft = Path.GetFullPath(leftPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRight = Path.GetFullPath(rightPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
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
            await _processService.StopProcessesUsingInstallationAsync(relatedInstallation);
        }

        foreach (var relatedInstallation in relatedInstallations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForInstallationReleaseAsync(relatedInstallation, TimeSpan.FromSeconds(10));
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

    private async Task WaitForInstallationReleaseAsync(ZapretInstallation installation, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            if (_processService.GetRunningProcessCount(installation) == 0 &&
                _processService.GetProcessCountUsingInstallation(installation) == 0)
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
            throw new TimeoutException("Системная административная операция зависла и была остановлена.");
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string GetManagedComponentsRootPath()
    {
        var managedTgWsProxyDirectory = _tgWsProxyService.ManagedComponentDirectoryPath;
        return Directory.GetParent(managedTgWsProxyDirectory)?.FullName
               ?? Path.Combine(
                   Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                   "ZapretManager",
                   "Components");
    }

    private List<string> BuildFullRemovalCleanupPaths(ZapretInstallation? installationForCleanup, string? tgWsProxyExecutablePath)
    {
        var paths = new List<string>();

        if (installationForCleanup is not null && Directory.Exists(installationForCleanup.RootPath))
        {
            paths.Add(installationForCleanup.RootPath);
        }
        else if (!string.IsNullOrWhiteSpace(_settings.LastInstallationPath) && Directory.Exists(_settings.LastInstallationPath))
        {
            paths.Add(Path.GetFullPath(_settings.LastInstallationPath));
        }

        if (Directory.Exists(_tgWsProxyService.ManagedComponentDirectoryPath))
        {
            paths.Add(_tgWsProxyService.ManagedComponentDirectoryPath);
        }

        if (Directory.Exists(_tgWsProxyService.ConfigDirectoryPath))
        {
            paths.Add(_tgWsProxyService.ConfigDirectoryPath);
        }

        if (!string.IsNullOrWhiteSpace(tgWsProxyExecutablePath) && File.Exists(tgWsProxyExecutablePath))
        {
            paths.Add(Path.GetFullPath(tgWsProxyExecutablePath));
        }

        return paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static string GetManagerInstallationDirectory()
    {
        var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        var directory = string.IsNullOrWhiteSpace(processPath)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(Path.GetFullPath(processPath));
        return (directory ?? AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
        _probeInitialProfileEstimate = ResolveInitialProbeProfileEstimate();
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
            averageProfileDuration = BlendProbeProfileEstimate(_probeInitialProfileEstimate, observedAverage, _probeProgressCompletedProfiles);
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

        if (_probeProgressCompletedProfiles < 2)
        {
            BusyEtaText = "Оцениваем оставшееся время...";
            return;
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

    private TimeSpan ResolveInitialProbeProfileEstimate()
    {
        if (_settings.ProbeAverageProfileSeconds is > 0)
        {
            return ClampProbeProfileEstimate(TimeSpan.FromSeconds(_settings.ProbeAverageProfileSeconds.Value));
        }

        return ClampProbeProfileEstimate(_connectivityTestService.GetEstimatedProfileProbeDuration());
    }

    private static TimeSpan ClampProbeProfileEstimate(TimeSpan duration)
    {
        if (duration < MinProbeProfileEstimate)
        {
            return MinProbeProfileEstimate;
        }

        if (duration > MaxProbeProfileEstimate)
        {
            return MaxProbeProfileEstimate;
        }

        return duration;
    }

    private static TimeSpan BlendProbeProfileEstimate(TimeSpan seedEstimate, TimeSpan observedAverage, int completedProfiles)
    {
        var clampedSeed = ClampProbeProfileEstimate(seedEstimate);
        var clampedObserved = ClampProbeProfileEstimate(observedAverage);
        var seedWeight = Math.Max(0, 3 - completedProfiles);
        var totalWeight = completedProfiles + seedWeight;
        if (totalWeight <= 0)
        {
            return clampedSeed;
        }

        var blendedTicks = ((clampedSeed.Ticks * seedWeight) + (clampedObserved.Ticks * completedProfiles)) / totalWeight;
        return ClampProbeProfileEstimate(TimeSpan.FromTicks(blendedTicks));
    }

    private void SaveProbeProfileEstimate()
    {
        if (_probeProgressCompletedProfiles < 2 || _probeStopwatch.Elapsed <= TimeSpan.Zero)
        {
            return;
        }

        var observedAverage = TimeSpan.FromTicks(_probeStopwatch.Elapsed.Ticks / _probeProgressCompletedProfiles);
        var clampedAverage = ClampProbeProfileEstimate(observedAverage);
        var roundedSeconds = Math.Round(clampedAverage.TotalSeconds, 1);
        if (_settings.ProbeAverageProfileSeconds.HasValue &&
            Math.Abs(_settings.ProbeAverageProfileSeconds.Value - roundedSeconds) < 0.1d)
        {
            return;
        }

        try
        {
            _settings.ProbeAverageProfileSeconds = roundedSeconds;
            _settingsService.Save(_settings);
        }
        catch
        {
        }
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

        var nextExpected = previous - elapsed;
        if (nextExpected < TimeSpan.Zero)
        {
            nextExpected = TimeSpan.Zero;
        }

        var smoothed = remaining < nextExpected
            ? remaining
            : nextExpected;

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

    private static void OpenExternalTarget(string fileName, string? arguments = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            startInfo.Arguments = arguments;
        }

        Process.Start(startInfo);
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
