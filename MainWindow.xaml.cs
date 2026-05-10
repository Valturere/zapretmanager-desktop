using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Reflection;
using System.Linq;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using ZapretManager.Models;
using ZapretManager.Services;
using ZapretManager.ViewModels;
using System.Windows.Shell;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace ZapretManager;

public partial class MainWindow : Window
{
    private readonly bool _startHidden;
    private bool _currentUseLightTheme;
    private readonly DispatcherTimer _statusTimer = new()
    {
        Interval = TimeSpan.FromSeconds(3)
    };
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private bool _isTrayMenuOpen;
    private bool _isExiting;
    private FrameworkElement? _activeToolTipOwner;
    private readonly DispatcherTimer _toolTipResumeTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(650)
    };
    private MainViewModel? _observedViewModel;
    private string? _lastTrayBalloonText;
    private DateTime _lastTrayBalloonShownUtc = DateTime.MinValue;
    private ListSortDirection _configSortDirection = ListSortDirection.Ascending;
    private readonly ObservableCollection<TcpFreezeConfigTableRow> _tcpRows = [];
    private readonly Dictionary<string, TcpFreezeConfigResult> _tcpResultMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex TcpSuiteCountRegex = new(@"suite TCP 16-20:\s+(?<count>\d+)\s+целей", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TcpConfigHeaderRegex = new(@"^===\s+(?<name>.+)\s+===$", RegexOptions.Compiled);
    private static readonly Regex TcpTargetLineRegex = new(@"\[(?<id>[^\]]+)\]\s+(?<host>\S+)\s+->\s+(?<details>.+)$", RegexOptions.Compiled);
    private CancellationTokenSource? _tcpRunCancellation;
    private TcpFreezeDetailsWindow? _embeddedTcpDetailsWindow;
    private HwndSource? _hwndSource;
    private DateTime _toolTipSuppressedUntilUtc = DateTime.MinValue;
    private bool _autoApplySelectorsReady;
    private int _tcpRunTotalTargets;
    private int _tcpRunProcessedTargets;
    private int _tcpRunStartedConfigs;
    private int _tcpRunTotalConfigs;
    private string? _tcpRunCurrentConfigName;

    public MainWindow(bool startHidden = false, bool useLightTheme = false)
    {
        InitializeComponent();
        _startHidden = startHidden;
        _currentUseLightTheme = useLightTheme;
        ApplyTheme(useLightTheme);
        NormalizeToolTips(this);
        _statusTimer.Tick += StatusTimer_Tick;

        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Closed += TrayMenu_Closed;
        _toolTipResumeTimer.Tick += TooltipResumeTimer_Tick;

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Zapret Manager",
            Visible = true
        };
        _notifyIcon.MouseUp += NotifyIcon_MouseUp;
        _notifyIcon.BalloonTipClicked += NotifyIcon_BalloonTipClicked;
        DataContextChanged += MainWindow_DataContextChanged;

        AddHandler(ToolTipOpeningEvent, new ToolTipEventHandler(AnyToolTip_Opening));
        AddHandler(ToolTipClosingEvent, new ToolTipEventHandler(AnyToolTip_Closing));
    }

    private void WindowRootGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var focusableAncestor = FindFocusableAncestor(source);
        if (focusableAncestor is not null && !ReferenceEquals(focusableAncestor, WindowRootGrid))
        {
            return;
        }

        Keyboard.Focus(WindowRootGrid);
    }

    private void ManualTargetTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(viewModel.ManualTarget))
        {
            viewModel.ManualTarget = string.Empty;
        }
    }

    private static UIElement? FindFocusableAncestor(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is UIElement element && element.Focusable)
            {
                return element;
            }

            current = GetParentObject(current);
        }

        return null;
    }

    private static DependencyObject? GetParentObject(DependencyObject current)
    {
        if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(current);
        }

        return LogicalTreeHelper.GetParent(current);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startHidden)
        {
            HideToTray();
        }

        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.InitializeAsync(_startHidden);
            await viewModel.RefreshStatusAsync();
            ApplyTheme(viewModel.UseLightThemeEnabled);
            _autoApplySelectorsReady = true;
            _statusTimer.Start();
        }
    }

    private void EmbeddedIpSetModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_autoApplySelectorsReady || sender is not System.Windows.Controls.ComboBox comboBox)
        {
            return;
        }

        if (!comboBox.IsKeyboardFocusWithin && !comboBox.IsDropDownOpen)
        {
            return;
        }

        if (DataContext is MainViewModel viewModel && viewModel.ApplyIpSetModeCommand.CanExecute(null))
        {
            viewModel.ApplyIpSetModeCommand.Execute(null);
        }
    }

    private void EmbeddedGameModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_autoApplySelectorsReady || sender is not System.Windows.Controls.ComboBox comboBox)
        {
            return;
        }

        if (!comboBox.IsKeyboardFocusWithin && !comboBox.IsDropDownOpen)
        {
            return;
        }

        if (DataContext is MainViewModel viewModel && viewModel.ApplyGameModeCommand.CanExecute(null))
        {
            viewModel.ApplyGameModeCommand.Execute(null);
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
        UpdateWindowChromeForState();
        ApplyWindowFrame(_currentUseLightTheme);
    }

    private async void StatusTimer_Tick(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.RefreshStatusAsync();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            RestoreFromMaximizedDrag(e, sender as IInputElement ?? WindowRootBorder);
            return;
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.MinimizeToTrayEnabled)
        {
            HideToTray();
            return;
        }

        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.CloseToTrayEnabled)
        {
            HideToTray();
            return;
        }

        ShutdownApplication();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        UpdateWindowChromeForState();

        if (WindowState == WindowState.Minimized &&
            DataContext is MainViewModel viewModel &&
            viewModel.MinimizeToTrayEnabled)
        {
            HideToTray();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            DetachViewModel();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            return;
        }

        if (DataContext is MainViewModel viewModel && viewModel.CloseToTrayEnabled)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void RestoreFromMaximizedDrag(MouseButtonEventArgs e, IInputElement dragSurface)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var dragPoint = e.GetPosition(dragSurface);
        var mousePosition = PointToScreen(dragPoint);
        var surfaceWidth = dragSurface is FrameworkElement element && element.ActualWidth > 0
            ? element.ActualWidth
            : ActualWidth;
        var widthRatio = surfaceWidth > 0 ? dragPoint.X / surfaceWidth : 0.5;
        var restoreWidth = RestoreBounds.Width > MinWidth ? RestoreBounds.Width : Width;
        var restoreHeight = RestoreBounds.Height > MinHeight ? RestoreBounds.Height : Height;
        var dragOffsetY = Math.Clamp(dragPoint.Y, 10, 28);

        WindowState = WindowState.Normal;
        Left = mousePosition.X - (restoreWidth * widthRatio);
        Top = Math.Max(0, mousePosition.Y - dragOffsetY);
        UpdateLayout();

        DragMove();
    }

    private void Window_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _toolTipSuppressedUntilUtc = DateTime.UtcNow.AddMilliseconds(650);

        CloseOpenToolTips(this);

        if (_activeToolTipOwner?.ToolTip is System.Windows.Controls.ToolTip toolTip && toolTip.IsOpen)
        {
            toolTip.IsOpen = false;
        }

        if (_activeToolTipOwner is not null)
        {
            _activeToolTipOwner = null;
        }

        ToolTipService.SetIsEnabled(this, false);
        _toolTipResumeTimer.Stop();
        _toolTipResumeTimer.Start();
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!viewModel.CloseAuxiliaryWindows())
        {
            return;
        }

        e.Handled = true;
        Keyboard.ClearFocus();
    }

    private void AnyToolTip_Opening(object sender, ToolTipEventArgs e)
    {
        if (DateTime.UtcNow < _toolTipSuppressedUntilUtc)
        {
            e.Handled = true;
            return;
        }

        _activeToolTipOwner = FindToolTipOwner(e.OriginalSource as DependencyObject);
    }

    private void AnyToolTip_Closing(object sender, ToolTipEventArgs e)
    {
        _activeToolTipOwner = null;
    }

    private void TooltipResumeTimer_Tick(object? sender, EventArgs e)
    {
        _toolTipResumeTimer.Stop();
        ToolTipService.SetIsEnabled(this, true);
    }

    private static void CloseOpenToolTips(DependencyObject root)
    {
        foreach (var element in EnumerateVisualTree(root).OfType<FrameworkElement>())
        {
            if (element.ToolTip is System.Windows.Controls.ToolTip toolTip && toolTip.IsOpen)
            {
                toolTip.IsOpen = false;
            }
        }
    }

    private static FrameworkElement? FindToolTipOwner(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.ToolTip is not null)
            {
                return element;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void NormalizeToolTips(DependencyObject root)
    {
        var sharedToolTipStyle = System.Windows.Application.Current?.TryFindResource(typeof(System.Windows.Controls.ToolTip)) as Style;

        foreach (var element in EnumerateVisualTree(root).OfType<FrameworkElement>())
        {
            if (element.ToolTip is null)
            {
                continue;
            }

            if (element.ToolTip is System.Windows.Controls.ToolTip toolTip)
            {
                toolTip.Style ??= sharedToolTipStyle;
                toolTip.MaxWidth = 260;
                if (toolTip.Content is string text)
                {
                    toolTip.Content = CreateWrappedToolTipTextBlock(text);
                }
                continue;
            }

            element.ToolTip = new System.Windows.Controls.ToolTip
            {
                Content = CreateWrappedToolTipTextBlock(element.ToolTip.ToString() ?? string.Empty),
                Style = sharedToolTipStyle,
                MaxWidth = 260
            };
        }
    }

    private static TextBlock CreateWrappedToolTipTextBlock(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 240,
            LineHeight = 15
        };
    }

    private static IEnumerable<DependencyObject> EnumerateVisualTree(DependencyObject root)
    {
        yield return root;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            foreach (var child in EnumerateVisualTree(VisualTreeHelper.GetChild(root, index)))
            {
                yield return child;
            }
        }
    }

    private void NotifyIcon_MouseUp(object? sender, Forms.MouseEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                if (_trayMenu.Visible)
                {
                    _trayMenu.Close();
                    return;
                }

                ShowMainWindow();
                return;
            }

            if (e.Button != Forms.MouseButtons.Right)
            {
                return;
            }

            if (_isTrayMenuOpen || _trayMenu.Visible)
            {
                _trayMenu.Close();
                return;
            }

            RebuildTrayMenu();
            _isTrayMenuOpen = true;
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                SetForegroundWindow(handle);
            }

            _trayMenu.Show(Forms.Control.MousePosition);
        }, DispatcherPriority.ApplicationIdle);
    }

    private void NotifyIcon_BalloonTipClicked(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            ShowMainWindow(showPendingStartupPrompt: false);

            if (DataContext is MainViewModel viewModel && viewModel.HasPendingStartupUpdatePrompt())
            {
                await Task.Delay(220);
                if (IsVisible)
                {
                    await viewModel.PresentPendingStartupUpdatePromptsAsync();
                }
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        if (e.NewValue is MainViewModel viewModel)
        {
            _observedViewModel = viewModel;
            _observedViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_observedViewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.UseLightThemeEnabled) &&
            _currentUseLightTheme != _observedViewModel.UseLightThemeEnabled)
        {
            ApplyTheme(_observedViewModel.UseLightThemeEnabled);
        }

        if (e.PropertyName == nameof(MainViewModel.InlineNotificationText) ||
            e.PropertyName == nameof(MainViewModel.IsInlineNotificationVisible) ||
            e.PropertyName == nameof(MainViewModel.IsInlineNotificationError))
        {
            TryShowTrayBalloon(_observedViewModel);
        }
    }

    private void DetachViewModel()
    {
        if (_observedViewModel is null)
        {
            return;
        }

        _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _observedViewModel = null;
    }

    private void TryShowTrayBalloon(MainViewModel viewModel)
    {
        if (viewModel.IsInlineNotificationVisible != true ||
            string.IsNullOrWhiteSpace(viewModel.InlineNotificationText) ||
            IsVisible)
        {
            return;
        }

        if (string.Equals(_lastTrayBalloonText, viewModel.InlineNotificationText, StringComparison.Ordinal) &&
            DateTime.UtcNow - _lastTrayBalloonShownUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastTrayBalloonText = viewModel.InlineNotificationText;
        _lastTrayBalloonShownUtc = DateTime.UtcNow;
        _notifyIcon.BalloonTipTitle = "Zapret Manager";
        _notifyIcon.BalloonTipText = viewModel.InlineNotificationText;
        _notifyIcon.BalloonTipIcon = viewModel.IsInlineNotificationError ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(3500);
    }

    private void TrayMenu_Closed(object? sender, Forms.ToolStripDropDownClosedEventArgs e)
    {
        _isTrayMenuOpen = false;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            PostMessage(handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void RebuildTrayMenu()
    {
        _trayMenu.Items.Clear();
        var viewModel = DataContext as MainViewModel;
        var serviceStatus = new Services.WindowsServiceManager().GetStatus();

        var openItem = new Forms.ToolStripMenuItem("Открыть");
        openItem.Click += (_, _) => Dispatcher.Invoke(() => ShowMainWindow());
        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());

        var serviceMenuText = serviceStatus.IsInstalled
            ? string.IsNullOrWhiteSpace(serviceStatus.ProfileName)
                ? "Удалить службу"
                : $"Удалить службу: {serviceStatus.ProfileName}"
            : viewModel?.GetTrayInstallServiceText() ?? "Установить службу";

        var serviceItem = new Forms.ToolStripMenuItem(serviceMenuText)
        {
            Enabled = serviceStatus.IsInstalled
                ? viewModel is { IsBusy: false, IsProbeRunning: false }
                : viewModel?.CanInstallServiceFromTray() == true
        };

        serviceItem.Click += async (_, _) =>
        {
            if (viewModel is not null)
            {
                if (serviceStatus.IsInstalled)
                {
                    await viewModel.RemoveServiceFromTrayAsync();
                }
                else
                {
                    await viewModel.InstallSelectedServiceFromTrayAsync();
                }
            }
        };
        _trayMenu.Items.Add(serviceItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());

        var currentDnsProfileKey = viewModel?.GetCurrentDnsProfileKey();
        var dnsMenuItem = new Forms.ToolStripMenuItem("DNS")
        {
            Enabled = viewModel is not null
        };
        var dnsSettingsItem = new Forms.ToolStripMenuItem("Настроить...")
        {
            Enabled = viewModel is { IsBusy: false, IsProbeRunning: false }
        };
        dnsSettingsItem.Click += async (_, _) =>
        {
            if (viewModel is not null)
            {
                await viewModel.OpenDnsSettingsAsync();
            }
        };
        dnsMenuItem.DropDownItems.Add(dnsSettingsItem);
        dnsMenuItem.DropDownItems.Add(new Forms.ToolStripSeparator());
        AddDnsMenuItem(dnsMenuItem, "Системный (DHCP)", DnsService.SystemProfileKey, currentDnsProfileKey, viewModel);
        AddDnsMenuItem(dnsMenuItem, "XBOX DNS", DnsService.XboxProfileKey, currentDnsProfileKey, viewModel);
        AddDnsMenuItem(dnsMenuItem, "Cloudflare DNS", DnsService.CloudflareProfileKey, currentDnsProfileKey, viewModel);
        AddDnsMenuItem(dnsMenuItem, "Google DNS", DnsService.GoogleProfileKey, currentDnsProfileKey, viewModel);
        AddDnsMenuItem(dnsMenuItem, "Quad9 DNS", DnsService.Quad9ProfileKey, currentDnsProfileKey, viewModel);
        AddDnsMenuItem(
            dnsMenuItem,
            viewModel?.GetTrayCustomDnsLabel() ?? "Пользовательский DNS",
            DnsService.CustomProfileKey,
            currentDnsProfileKey,
            viewModel,
            enabled: viewModel?.HasCustomDnsConfigured() == true);
        _trayMenu.Items.Add(dnsMenuItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());

        var isGameModeEnabled = viewModel?.IsGameModeEnabled() == true;
        var gameModeItem = new Forms.ToolStripMenuItem(isGameModeEnabled ? "Выключить игровой режим" : "Включить игровой режим")
        {
            Enabled = viewModel is { IsBusy: false, IsProbeRunning: false }
        };

        gameModeItem.Click += async (_, _) =>
        {
            if (viewModel is not null)
            {
                await viewModel.ToggleGameModeFromTrayAsync(!isGameModeEnabled);
            }
        };
        _trayMenu.Items.Add(gameModeItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => Dispatcher.Invoke(ShutdownApplication);
        _trayMenu.Items.Add(exitItem);
    }

    private static void AddDnsMenuItem(
        Forms.ToolStripMenuItem parent,
        string text,
        string profileKey,
        string? currentProfileKey,
        MainViewModel? viewModel,
        bool enabled = true)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            Enabled = enabled && viewModel is { IsBusy: false, IsProbeRunning: false },
            Checked = string.Equals(currentProfileKey, profileKey, StringComparison.OrdinalIgnoreCase)
        };

        item.Click += async (_, _) =>
        {
            if (viewModel is not null)
            {
                await viewModel.ApplyDnsProfileFromTrayAsync(profileKey);
            }
        };

        parent.DropDownItems.Add(item);
    }

    private void ShowMainWindow(bool showPendingStartupPrompt = true)
    {
        ShowInTaskbar = true;
        ShowActivated = true;
        Show();
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, SW_RESTORE);
            BringWindowToTop(handle);
            SetForegroundWindow(handle);
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        if (showPendingStartupPrompt &&
            DataContext is MainViewModel viewModel &&
            viewModel.HasPendingStartupUpdatePrompt())
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(160);
                if (IsVisible)
                {
                    await viewModel.PresentPendingStartupUpdatePromptsAsync();
                }
            }).Task.Unwrap();
        }
    }

    public void BringToFrontFromExternal()
    {
        ShowMainWindow();
    }

    private void MenuHostButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu is null)
        {
            return;
        }

        if (button.ContextMenu.IsOpen)
        {
            button.ContextMenu.IsOpen = false;
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        if (button.ContextMenu.Placement is PlacementMode.Mouse or PlacementMode.MousePoint)
        {
            button.ContextMenu.Placement = PlacementMode.Bottom;
        }

        button.ContextMenu.IsOpen = true;
    }

    private void MenuHostButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu is null)
        {
            return;
        }

        e.Handled = true;

        if (button.ContextMenu.IsOpen)
        {
            button.ContextMenu.IsOpen = false;
            return;
        }

        button.Focus();
        button.ContextMenu.PlacementTarget = button;
        if (button.ContextMenu.Placement is PlacementMode.Mouse or PlacementMode.MousePoint)
        {
            button.ContextMenu.Placement = PlacementMode.Bottom;
        }

        button.ContextMenu.IsOpen = true;
    }

    private void HomeRecommendedActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        switch (viewModel.HomeRecommendedActionKind)
        {
            case "install-zapret":
                if (viewModel.HandleZapretInstallOrUpdateCommand.CanExecute(null))
                {
                    viewModel.HandleZapretInstallOrUpdateCommand.Execute(null);
                }
                break;
            case "test-configs":
                if (viewModel.RunTestsCommand.CanExecute(null))
                {
                    viewModel.RunTestsCommand.Execute(null);
                }
                break;
            case "review-configs":
                SelectSidebarTab(ConfigsTabItem);
                break;
            case "start-recommended":
                if (viewModel.StartCommand.CanExecute(null))
                {
                    viewModel.StartCommand.Execute(null);
                }
                break;
        }
    }

    private void HomeNavigateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button)
        {
            return;
        }

        var target = button.CommandParameter as string ?? button.Tag as string;
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        switch (target)
        {
            case "configs":
                SelectSidebarTab(ConfigsTabItem);
                break;
            case "telegram":
                SelectSidebarTab(TelegramProxyTabItem);
                break;
            case "install":
                SelectSidebarTab(InstallUpdatesTabItem);
                break;
        }
    }

    private void SelectSidebarTab(TabItem? tabItem)
    {
        if (tabItem is null)
        {
            return;
        }

        MainSidebarTabControl.SelectedItem = tabItem;
        tabItem.Focus();
    }

    private void ConfigGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        ApplyConfigGridSorting(e.Column.SortMemberPath);
    }

    private void ConfigGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.DataGrid { SelectedItems: IList selectedItems } ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.UpdateSelectedConfigRows(selectedItems.OfType<ConfigTableRow>());
    }

    private void ConfigHeaderSortButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string sortMemberPath } ||
            string.IsNullOrWhiteSpace(sortMemberPath))
        {
            return;
        }

        ApplyConfigGridSorting(sortMemberPath);
    }

    private void ApplyConfigGridSorting(string? sortMemberPath)
    {
        if (ConfigGrid.ItemsSource is null || string.IsNullOrWhiteSpace(sortMemberPath))
        {
            return;
        }

        var view = CollectionViewSource.GetDefaultView(ConfigGrid.ItemsSource);
        if (view is not ListCollectionView listView)
        {
            return;
        }

        DataGridColumn? targetColumn = null;
        foreach (var column in ConfigGrid.Columns)
        {
            if (string.Equals(column.SortMemberPath, sortMemberPath, StringComparison.Ordinal))
            {
                targetColumn = column;
            }
            else
            {
                column.SortDirection = null;
            }
        }

        if (targetColumn is null)
        {
            return;
        }

        var nextDirection = targetColumn.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        listView.SortDescriptions.Clear();
        listView.CustomSort = null;

        if (string.Equals(sortMemberPath, "ConfigName", StringComparison.Ordinal))
        {
            _configSortDirection = nextDirection;
            listView.CustomSort = new ConfigNameNaturalComparer(_configSortDirection);
            targetColumn.SortDirection = _configSortDirection;
            return;
        }

        _configSortDirection = nextDirection;
        listView.SortDescriptions.Add(new SortDescription(sortMemberPath, nextDirection));
        targetColumn.SortDirection = nextDirection;
    }

    private void ProbeDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: ConfigTableRow row } ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.OpenProbeDetails(row);
    }

    private void RegularModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetConfigMode(useTcpMode: false);
    }

    private void TcpModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetConfigMode(useTcpMode: true);
        RefreshTcpRows();
    }

    private void SetConfigMode(bool useTcpMode)
    {
        RegularModeButton.Style = (Style)FindResource(useTcpMode ? "SegmentedButtonStyle" : "SegmentedSelectedButtonStyle");
        TcpModeButton.Style = (Style)FindResource(useTcpMode ? "SegmentedSelectedButtonStyle" : "SegmentedButtonStyle");
        RegularTargetsPanel.Visibility = useTcpMode ? Visibility.Collapsed : Visibility.Visible;
        RegularResultPanel.Visibility = Visibility.Visible;
        RegularConfigGridPanel.Visibility = useTcpMode ? Visibility.Collapsed : Visibility.Visible;
        TcpActionPanel.Visibility = useTcpMode ? Visibility.Visible : Visibility.Collapsed;
        TcpConfigGridPanel.Visibility = useTcpMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshTcpRows()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var selectedPath = (TcpConfigGrid.SelectedItem as TcpFreezeConfigTableRow)?.FilePath;
        var context = viewModel.BuildTcpFreezeContext();

        _tcpRows.Clear();
        foreach (var descriptor in context.Configs)
        {
            _tcpRows.Add(BuildTcpRow(descriptor, _tcpResultMap.GetValueOrDefault(descriptor.FilePath)));
        }

        TcpConfigGrid.ItemsSource = _tcpRows;
        var selected = _tcpRows.FirstOrDefault(row => string.Equals(row.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
                       ?? _tcpRows.FirstOrDefault(row => string.Equals(row.FilePath, context.InitiallySelectedFilePath, StringComparison.OrdinalIgnoreCase))
                       ?? _tcpRows.FirstOrDefault();
        TcpConfigGrid.SelectedItem = selected;
        TcpRunSelectedButton.IsEnabled = _tcpRunCancellation is null && selected is not null;
        TcpRunAllButton.IsEnabled = _tcpRunCancellation is null && _tcpRows.Count > 0;
    }

    private async void TcpRunSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (TcpConfigGrid.SelectedItem is not TcpFreezeConfigTableRow row)
        {
            return;
        }

        await RunEmbeddedTcpFreezeAsync(row.FilePath);
    }

    private async void TcpRunAllButton_Click(object sender, RoutedEventArgs e)
    {
        await RunEmbeddedTcpFreezeAsync(null);
    }

    private async Task RunEmbeddedTcpFreezeAsync(string? selectedConfigPath)
    {
        if (_tcpRunCancellation is not null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _tcpRunCancellation = new CancellationTokenSource();
        ResetEmbeddedTcpProgressState(string.IsNullOrWhiteSpace(selectedConfigPath) ? _tcpRows.Count : 1);
        TcpRunSelectedButton.IsEnabled = false;
        TcpRunAllButton.IsEnabled = false;
        TcpStatusTextBlock.Text = string.IsNullOrWhiteSpace(selectedConfigPath)
            ? "TCP 16-20 выполняется для всех видимых конфигов..."
            : "TCP 16-20 выполняется для выбранного конфига...";

        try
        {
            var progress = new Progress<string>(line =>
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    TcpStatusTextBlock.Text = TryBuildEmbeddedTcpProgressText(line) ?? line;
                }
            });

            var report = await viewModel.RunTcpFreezeToolAsync(selectedConfigPath, progress, _tcpRunCancellation.Token);
            _tcpResultMap.Clear();
            foreach (var result in report.ConfigResults)
            {
                _tcpResultMap[result.FilePath] = result;
            }

            RefreshTcpRows();

            var recommended = !string.IsNullOrWhiteSpace(report.RecommendedConfigPath)
                ? _tcpRows.FirstOrDefault(row => string.Equals(row.FilePath, report.RecommendedConfigPath, StringComparison.OrdinalIgnoreCase))
                : null;

            TcpRecommendedTextBlock.Text = recommended is null
                ? "Лучший конфиг не определён."
                : $"Лучший конфиг: {recommended.ConfigName}";
            TcpStatusTextBlock.Text = "Проверка TCP 16-20 завершена.";
        }
        catch (OperationCanceledException)
        {
            TcpStatusTextBlock.Text = "Проверка TCP 16-20 отменена.";
        }
        catch (Exception ex)
        {
            TcpStatusTextBlock.Text = "Ошибка TCP 16-20: " + DialogService.GetDisplayMessage(ex);
        }
        finally
        {
            _tcpRunCancellation.Dispose();
            _tcpRunCancellation = null;
            RefreshTcpRows();
        }
    }

    private void ResetEmbeddedTcpProgressState(int totalConfigs)
    {
        _tcpRunTotalTargets = 0;
        _tcpRunProcessedTargets = 0;
        _tcpRunStartedConfigs = 0;
        _tcpRunTotalConfigs = Math.Max(1, totalConfigs);
        _tcpRunCurrentConfigName = null;
    }

    private string? TryBuildEmbeddedTcpProgressText(string line)
    {
        var suiteMatch = TcpSuiteCountRegex.Match(line);
        if (suiteMatch.Success && int.TryParse(suiteMatch.Groups["count"].Value, out var totalTargets))
        {
            _tcpRunTotalTargets = totalTargets;
            return $"Подготовлены цели TCP 16-20: {totalTargets}.";
        }

        var configHeaderMatch = TcpConfigHeaderRegex.Match(line);
        if (configHeaderMatch.Success &&
            !string.Equals(configHeaderMatch.Groups["name"].Value, "Сводка", StringComparison.OrdinalIgnoreCase))
        {
            _tcpRunCurrentConfigName = configHeaderMatch.Groups["name"].Value.Trim();
            _tcpRunProcessedTargets = 0;
            _tcpRunStartedConfigs = Math.Min(_tcpRunStartedConfigs + 1, _tcpRunTotalConfigs);
            return _tcpRunTotalConfigs > 1
                ? $"Проверяем конфиг {_tcpRunStartedConfigs} из {_tcpRunTotalConfigs}: {_tcpRunCurrentConfigName}."
                : $"Проверяем {_tcpRunCurrentConfigName}.";
        }

        var targetLineMatch = TcpTargetLineRegex.Match(line);
        if (targetLineMatch.Success)
        {
            _tcpRunProcessedTargets++;
            if (!string.IsNullOrWhiteSpace(_tcpRunCurrentConfigName) && _tcpRunTotalTargets > 0)
            {
                return _tcpRunTotalConfigs > 1
                    ? $"{_tcpRunCurrentConfigName}: {_tcpRunProcessedTargets} из {_tcpRunTotalTargets} целей."
                    : $"Проверяем {_tcpRunCurrentConfigName}: {_tcpRunProcessedTargets} из {_tcpRunTotalTargets} целей.";
            }
        }

        return null;
    }

    private void TcpDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: TcpFreezeConfigTableRow row } ||
            !_tcpResultMap.TryGetValue(row.FilePath, out var result))
        {
            return;
        }

        try
        {
            _embeddedTcpDetailsWindow?.Close();
        }
        catch
        {
        }

        var window = new TcpFreezeDetailsWindow(result, _currentUseLightTheme)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_embeddedTcpDetailsWindow, window))
            {
                _embeddedTcpDetailsWindow = null;
            }
        };

        _embeddedTcpDetailsWindow = window;
        window.Show();
        window.Activate();
    }

    private static TcpFreezeConfigTableRow BuildTcpRow(TcpFreezeConfigDescriptor descriptor, TcpFreezeConfigResult? result)
    {
        return new TcpFreezeConfigTableRow
        {
            ConfigName = descriptor.ConfigName,
            FileName = descriptor.FileName,
            FilePath = descriptor.FilePath,
            OkCount = result?.OkCount,
            BlockedCount = result?.BlockedCount,
            FailCount = result?.FailCount,
            UnsupportedCount = result?.UnsupportedCount,
            SummaryText = BuildTcpSummaryText(result),
            HasResult = result is not null
        };
    }

    private static string BuildTcpSummaryText(TcpFreezeConfigResult? result)
    {
        if (result is null)
        {
            return string.Empty;
        }

        if (result.BlockedCount > 0 && result.BlockedTargets.Count > 0)
        {
            return $"Подозрение на freeze: {string.Join(", ", result.BlockedTargets.Take(2))}";
        }

        if (result.FailCount > 0)
        {
            return "Есть ошибки соединения";
        }

        if (result.UnsupportedCount > 0)
        {
            return "Есть неподдерживаемые проверки";
        }

        return "Подозрений на freeze не найдено";
    }

    private void ResetProbeResultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.ClearProbeResults();
        _tcpResultMap.Clear();
        RefreshTcpRows();
        TcpStatusTextBlock.Text = "Выберите конфиг в таблице и запустите проверку TCP 16-20.";
        TcpRecommendedTextBlock.Text = "Лучший конфиг появится после проверки.";
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void ShutdownApplication()
    {
        _isExiting = true;
        _statusTimer.Stop();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    public void ShutdownForManagerUpdate()
    {
        ShutdownApplication();
    }

    public void ShutdownForProgramRemoval()
    {
        ShutdownApplication();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.UseLightThemeEnabled = !viewModel.UseLightThemeEnabled;
        ApplyTheme(viewModel.UseLightThemeEnabled);
    }

    private void ApplyTheme(bool useLightTheme)
    {
        _currentUseLightTheme = useLightTheme;
        SetBrushColor("AppBgBrush", useLightTheme ? "#F4F7FB" : "#08111D");
        SetBrushColor("CardBrush", useLightTheme ? "#FFFFFF" : "#0E1828");
        SetBrushColor("CardBrush2", useLightTheme ? "#F8FBFF" : "#0B1322");
        SetBrushColor("CardEdgeBrush", useLightTheme ? "#DCE7F2" : "#22344E");
        SetBrushColor("AccentBrush", useLightTheme ? "#2B70F7" : "#2F6BFF");
        SetBrushColor("AccentHoverBrush", useLightTheme ? "#1F5EDD" : "#4A82FF");
        SetBrushColor("AccentBorderBrush", useLightTheme ? "#5C90FF" : "#6E9CFF");
        SetBrushColor("SuccessBrush", useLightTheme ? "#20A86D" : "#1E9E68");
        SetBrushColor("SuccessHoverBrush", useLightTheme ? "#15965F" : "#27B777");
        SetBrushColor("SuccessBorderBrush", useLightTheme ? "#58C695" : "#4AD497");
        SetBrushColor("DangerBrush", useLightTheme ? "#D55454" : "#A94141");
        SetBrushColor("DangerHoverBrush", useLightTheme ? "#C74444" : "#C04D4D");
        SetBrushColor("DangerBorderBrush", useLightTheme ? "#E48787" : "#DB7777");
        SetBrushColor("ActionBrush", useLightTheme ? "#F6F9FD" : "#111C2D");
        SetBrushColor("ActionHoverBrush", useLightTheme ? "#EDF3FA" : "#172536");
        SetBrushColor("ActionBorderBrush", useLightTheme ? "#D8E2EF" : "#263952");
        SetBrushColor("TextMutedBrush", useLightTheme ? "#516784" : "#90A3BF");
        SetBrushColor("MainTextBrush", useLightTheme ? "#0E1B2B" : "#F4F8FF");
        SetBrushColor("TitleBarBrush", useLightTheme ? "#FAFCFF" : "#0A1423");
        SetBrushColor("InputBrush", useLightTheme ? "#FFFFFF" : "#0C1626");
        SetBrushColor("InputBorderBrush", useLightTheme ? "#D8E3F0" : "#24364F");
        SetBrushColor("DisabledBrush", useLightTheme ? "#EEF3F8" : "#101827");
        SetBrushColor("DisabledBorderBrush", useLightTheme ? "#D6E0EC" : "#1A2A3E");
        SetBrushColor("DisabledTextBrush", useLightTheme ? "#7489A3" : "#61748D");
        SetBrushColor("SelectionBrush", useLightTheme ? "#E8F0FF" : "#142848");
        SetBrushColor("InnerCardBrush", useLightTheme ? "#FBFDFF" : "#0A1523");
        SetBrushColor("InnerCardBorderBrush", useLightTheme ? "#E4EDF7" : "#1D3048");
        SetBrushColor("TooltipBackgroundBrush", useLightTheme ? "#FFFFFF" : "#0C1625");
        SetBrushColor("TooltipBorderBrush", useLightTheme ? "#D5E0EB" : "#304560");
        SetBrushColor("TooltipTextBrush", useLightTheme ? "#112034" : "#F3F7FE");
        SetBrushColor("ScrollTrackBrush", useLightTheme ? "#EEF3F8" : "#101A2A");
        SetBrushColor("ScrollThumbBrush", useLightTheme ? "#B1C0D2" : "#3D526D");
        SetBrushColor("ScrollThumbHoverBrush", useLightTheme ? "#94A8C0" : "#587392");
        SetBrushColor("ProbeBadgeSuccessBackgroundBrush", useLightTheme ? "#E6F6EF" : "#15382C");
        SetBrushColor("ProbeBadgeSuccessBorderBrush", useLightTheme ? "#9AD4B9" : "#2A8761");
        SetBrushColor("ProbeBadgeSuccessForegroundBrush", useLightTheme ? "#247B53" : "#77E0AF");
        SetBrushColor("ProbeBadgePartialBackgroundBrush", useLightTheme ? "#FFF2DA" : "#43361C");
        SetBrushColor("ProbeBadgePartialBorderBrush", useLightTheme ? "#E3BE79" : "#A88035");
        SetBrushColor("ProbeBadgePartialForegroundBrush", useLightTheme ? "#A56E14" : "#F3C76C");
        SetBrushColor("ProbeBadgeFailureBackgroundBrush", useLightTheme ? "#FCEAEA" : "#3E2023");
        SetBrushColor("ProbeBadgeFailureBorderBrush", useLightTheme ? "#E4A2A7" : "#A54A57");
        SetBrushColor("ProbeBadgeFailureForegroundBrush", useLightTheme ? "#B24B58" : "#F0A4AD");
        SetBrushColor("ProbeBadgeNeutralBackgroundBrush", useLightTheme ? "#F2F6FB" : "#101B2D");
        SetBrushColor("ProbeBadgeNeutralBorderBrush", useLightTheme ? "#C7D6E6" : "#314A66");
        SetBrushColor("ProbeBadgeNeutralForegroundBrush", useLightTheme ? "#667A95" : "#9AB0CB");
        SetBrushColor("SummarySuccessBadgeBrush", useLightTheme ? "#E6F6EF" : "#15382C");
        SetBrushColor("SummarySuccessBadgeBorderBrush", useLightTheme ? "#9AD4B9" : "#2A8761");
        SetBrushColor("SummarySuccessBadgeIconBrush", useLightTheme ? "#247B53" : "#77E0AF");
        SetBrushColor("SummaryPartialBadgeBrush", useLightTheme ? "#FFF2DA" : "#43361C");
        SetBrushColor("SummaryPartialBadgeBorderBrush", useLightTheme ? "#E3BE79" : "#A88035");
        SetBrushColor("SummaryPartialBadgeIconBrush", useLightTheme ? "#A56E14" : "#F3C76C");
        SetBrushColor("SummaryFailureBadgeBrush", useLightTheme ? "#FCEAEA" : "#3E2023");
        SetBrushColor("SummaryFailureBadgeBorderBrush", useLightTheme ? "#E4A2A7" : "#A54A57");
        SetBrushColor("SummaryFailureBadgeIconBrush", useLightTheme ? "#B24B58" : "#F0A4AD");
        SetBrushColor("InstallAvailableBadgeBrush", useLightTheme ? "#EAF1FF" : "#152746");
        SetBrushColor("InstallAvailableBadgeBorderBrush", useLightTheme ? "#BBD0FF" : "#2C4F8B");
        SetBrushColor("InstallAvailableBadgeForegroundBrush", useLightTheme ? "#3E66C4" : "#8EB8FF");
        SetBrushColor("SoftAccentBrush", useLightTheme ? "#E8F0FF" : "#16233A");
        SetBrushColor("SoftAccentBorderBrush", useLightTheme ? "#7EA3E6" : "#4C6EA9");
        SetBrushColor("SoftAccentForegroundBrush", useLightTheme ? "#244E97" : "#B7CCFF");
        SetBrushColor("SoftAccentHoverBrush", useLightTheme ? "#DDE8FD" : "#1B2B46");
        SetBrushColor("RunningStatusBadgeBrush", useLightTheme ? "#E8F7EF" : "#17382A");
        SetBrushColor("RunningStatusBadgeBorderBrush", useLightTheme ? "#9AD1B4" : "#2A7C58");
        SetBrushColor("RunningStatusBadgeForegroundBrush", useLightTheme ? "#1C7A4C" : "#6AE5A8");
        SetBrushColor("StoppedStatusBadgeBrush", useLightTheme ? "#F9ECEF" : "#22161B");
        SetBrushColor("StoppedStatusBadgeBorderBrush", useLightTheme ? "#D89BA7" : "#5E2D39");
        SetBrushColor("StoppedStatusBadgeForegroundBrush", useLightTheme ? "#A34A59" : "#E8A4AE");
        SetBrushColor("SoftDangerBrush", useLightTheme ? "#FDF0F2" : "#22161B");
        SetBrushColor("SoftDangerBorderBrush", useLightTheme ? "#E6B8C0" : "#5E2D39");
        SetBrushColor("SoftDangerForegroundBrush", useLightTheme ? "#B24B58" : "#F08A99");
        SetBrushColor("SoftDangerHoverBrush", useLightTheme ? "#FAE4E8" : "#2B1B21");

        ApplyWindowFrame(useLightTheme);
        ApplyThemeToOpenWindows(useLightTheme);
    }

    public bool CurrentUseLightTheme => _currentUseLightTheme;

    private void SetBrushColor(string resourceKey, string hex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        UpdateBrushColor(Resources, resourceKey, color);

        if (resourceKey.StartsWith("Tooltip", StringComparison.Ordinal))
        {
            UpdateBrushColor(System.Windows.Application.Current?.Resources, resourceKey, color);
        }
    }

    private static void UpdateBrushColor(ResourceDictionary? resources, string resourceKey, System.Windows.Media.Color color)
    {
        if (resources is null)
        {
            return;
        }

        if (resources[resourceKey] is SolidColorBrush brush)
        {
            if (brush.Color == color)
            {
                return;
            }

            if (brush.IsFrozen)
            {
                var clone = brush.CloneCurrentValue();
                clone.Color = color;
                resources[resourceKey] = clone;
                return;
            }

            brush.Color = color;
            return;
        }

        resources[resourceKey] = new SolidColorBrush(color);
    }

    private void ApplyWindowFrame(bool useLightTheme)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var enabled = 1;
            _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));

            var cornerPreference = 2; // DWMWCP_ROUND
            _ = DwmSetWindowAttribute(handle, 33, ref cornerPreference, sizeof(int));

            var borderColor = useLightTheme
                ? ColorToColorRef(0xFA, 0xFC, 0xFF)
                : ColorToColorRef(0x0A, 0x14, 0x23);
            _ = DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(uint));

            var captionColor = useLightTheme
                ? ColorToColorRef(0xFA, 0xFC, 0xFF)
                : ColorToColorRef(0x0A, 0x14, 0x23);
            _ = DwmSetWindowAttribute(handle, 35, ref captionColor, sizeof(uint));

            var textColor = useLightTheme
                ? ColorToColorRef(0x0E, 0x1B, 0x2B)
                : ColorToColorRef(0xF4, 0xF8, 0xFF);
            _ = DwmSetWindowAttribute(handle, 36, ref textColor, sizeof(uint));
        }
        catch
        {
        }
    }

    private void ApplyThemeToOpenWindows(bool useLightTheme)
    {
        var windows = System.Windows.Application.Current?.Windows;
        if (windows is null)
        {
            return;
        }

        foreach (Window? window in windows)
        {
            if (window is null)
            {
                continue;
            }

            if (ReferenceEquals(window, this))
            {
                continue;
            }

            try
            {
                var method = window.GetType().GetMethod(
                    "ApplyTheme",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: [typeof(bool)],
                    modifiers: null);

                method?.Invoke(window, [useLightTheme]);
            }
            catch
            {
            }
        }
    }

    private static uint ColorToColorRef(byte red, byte green, byte blue)
    {
        return (uint)(red | (green << 8) | (blue << 16));
    }

    private void UpdateWindowChromeForState()
    {
        if (WindowChrome.GetWindowChrome(this) is not WindowChrome chrome)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            chrome.CornerRadius = new CornerRadius(0);
            chrome.ResizeBorderThickness = new Thickness(0);
            WindowRootBorder.CornerRadius = new CornerRadius(0);
            WindowRootGrid.Margin = new Thickness(8);
            return;
        }

        chrome.CornerRadius = new CornerRadius(14);
        chrome.ResizeBorderThickness = new Thickness(0, 0, 0, 8);
        WindowRootBorder.CornerRadius = new CornerRadius(14);
        WindowRootGrid.Margin = new Thickness(0);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;

        if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MONITORINFO();
        monitorInfo.cbSize = Marshal.SizeOf<MONITORINFO>();
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var workArea = monitorInfo.rcWork;
        var monitorArea = monitorInfo.rcMonitor;

        mmi.ptMaxPosition.x = workArea.left - monitorArea.left;
        mmi.ptMaxPosition.y = workArea.top - monitorArea.top;
        mmi.ptMaxSize.x = workArea.right - workArea.left;
        mmi.ptMaxSize.y = workArea.bottom - workArea.top;

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private sealed class ConfigNameNaturalComparer(ListSortDirection direction) : IComparer
    {
        private readonly int _multiplier = direction == ListSortDirection.Descending ? -1 : 1;

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is not Models.ConfigTableRow left || y is not Models.ConfigTableRow right)
            {
                return 0;
            }

            var result = CompareNatural(left.ConfigName, right.ConfigName);
            if (result == 0)
            {
                result = CompareNatural(left.FileName, right.FileName);
            }

            return result * _multiplier;
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

    private static Drawing.Icon LoadTrayIcon()
    {
        var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var icon = Drawing.Icon.ExtractAssociatedIcon(processPath);
            if (icon is not null)
            {
                return icon;
            }
        }

        return Drawing.SystemIcons.Application;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint attributeValue, int attributeSize);

    private const int WM_NULL = 0x0000;
    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

}
