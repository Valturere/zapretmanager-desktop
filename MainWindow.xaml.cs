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
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using ZapretManager.Models;
using ZapretManager.Services;
using ZapretManager.ViewModels;
using System.Windows.Shell;

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
    private HwndSource? _hwndSource;
    private DateTime _toolTipSuppressedUntilUtc = DateTime.MinValue;

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
        DataContextChanged += MainWindow_DataContextChanged;

        AddHandler(ToolTipOpeningEvent, new ToolTipEventHandler(AnyToolTip_Opening));
        AddHandler(ToolTipClosingEvent, new ToolTipEventHandler(AnyToolTip_Closing));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startHidden)
        {
            HideToTray();
        }

        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.InitializeAsync();
            await viewModel.RefreshStatusAsync();
            ApplyTheme(viewModel.UseLightThemeEnabled);
            _statusTimer.Start();
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
        foreach (var element in EnumerateVisualTree(root).OfType<FrameworkElement>())
        {
            if (element.ToolTip is null)
            {
                continue;
            }

            if (element.ToolTip is System.Windows.Controls.ToolTip toolTip)
            {
                toolTip.StaysOpen = false;
                continue;
            }

            element.ToolTip = new System.Windows.Controls.ToolTip
            {
                Content = element.ToolTip,
                StaysOpen = false
            };
        }
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
        openItem.Click += (_, _) => Dispatcher.Invoke(ShowMainWindow);
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

    private void ShowMainWindow()
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

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void ConfigGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        ApplyConfigGridSorting(e.Column.SortMemberPath);
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
        SetBrushColor("AppBgBrush", useLightTheme ? "#E8F1F8" : "#06111C");
        SetBrushColor("CardBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("CardBrush2", useLightTheme ? "#EFF6FC" : "#0D1C2C");
        SetBrushColor("CardEdgeBrush", useLightTheme ? "#9AB7D3" : "#214A70");
        SetBrushColor("AccentBrush", useLightTheme ? "#43BF86" : "#47C78B");
        SetBrushColor("AccentHoverBrush", useLightTheme ? "#58CF96" : "#5DD89B");
        SetBrushColor("ActionBrush", useLightTheme ? "#DCE9F5" : "#173454");
        SetBrushColor("ActionHoverBrush", useLightTheme ? "#CFE1F1" : "#21476F");
        SetBrushColor("TextMutedBrush", useLightTheme ? "#4F6B88" : "#9AB2CD");
        SetBrushColor("MainTextBrush", useLightTheme ? "#183049" : "#FFFFFF");
        SetBrushColor("TitleBarBrush", useLightTheme ? "#DDEAF5" : "#081725");
        SetBrushColor("InputBrush", useLightTheme ? "#FFFFFF" : "#0A1725");
        SetBrushColor("InputBorderBrush", useLightTheme ? "#87A8C8" : "#2D5379");
        SetBrushColor("DisabledBrush", useLightTheme ? "#D4E2EE" : "#13283F");
        SetBrushColor("DisabledBorderBrush", useLightTheme ? "#AAC0D6" : "#264766");
        SetBrushColor("DisabledTextBrush", useLightTheme ? "#6E839A" : "#8EA6C2");
        SetBrushColor("SelectionBrush", useLightTheme ? "#BCD0E3" : "#35506A");
        SetBrushColor("InnerCardBrush", useLightTheme ? "#F4F8FC" : "#0C1A28");
        SetBrushColor("InnerCardBorderBrush", useLightTheme ? "#9CB7CF" : "#2C5478");
        SetBrushColor("TooltipBackgroundBrush", useLightTheme ? "#F7FBFF" : "#102235");
        SetBrushColor("TooltipBorderBrush", useLightTheme ? "#87A8C8" : "#31597F");
        SetBrushColor("TooltipTextBrush", useLightTheme ? "#183049" : "#EAF3FC");
        SetBrushColor("ScrollTrackBrush", useLightTheme ? "#E4EEF7" : "#102235");
        SetBrushColor("ScrollThumbBrush", useLightTheme ? "#B7C9DB" : "#35506A");
        SetBrushColor("ScrollThumbHoverBrush", useLightTheme ? "#9FB8D0" : "#466481");
        SetBrushColor("SummarySuccessBadgeBrush", useLightTheme ? "#D9EDE3" : "#27423D");
        SetBrushColor("SummarySuccessBadgeBorderBrush", useLightTheme ? "#A8C4B7" : "#6C9184");
        SetBrushColor("SummarySuccessBadgeIconBrush", useLightTheme ? "#2E6350" : "#FFFFFF");
        SetBrushColor("SummaryPartialBadgeBrush", useLightTheme ? "#EEE4D3" : "#4A422E");
        SetBrushColor("SummaryPartialBadgeBorderBrush", useLightTheme ? "#C7B18A" : "#A89566");
        SetBrushColor("SummaryPartialBadgeIconBrush", useLightTheme ? "#755B2F" : "#FFFFFF");
        SetBrushColor("SummaryFailureBadgeBrush", useLightTheme ? "#EEDDE3" : "#4C3941");
        SetBrushColor("SummaryFailureBadgeBorderBrush", useLightTheme ? "#C8A8B2" : "#B28A97");
        SetBrushColor("SummaryFailureBadgeIconBrush", useLightTheme ? "#764754" : "#FFFFFF");

        ApplyWindowFrame(useLightTheme);
        ApplyThemeToOpenWindows(useLightTheme);
    }

    public bool CurrentUseLightTheme => _currentUseLightTheme;

    private void SetBrushColor(string resourceKey, string hex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

        if (Resources[resourceKey] is SolidColorBrush brush)
        {
            if (brush.Color == color)
            {
                return;
            }

            if (brush.IsFrozen)
            {
                var clone = brush.CloneCurrentValue();
                clone.Color = color;
                Resources[resourceKey] = clone;
                return;
            }

            brush.Color = color;
            return;
        }

        Resources[resourceKey] = new SolidColorBrush(color);
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
                ? ColorToColorRef(0xDD, 0xEA, 0xF5)
                : ColorToColorRef(0x08, 0x17, 0x25);
            _ = DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(uint));

            var captionColor = useLightTheme
                ? ColorToColorRef(0xDD, 0xEA, 0xF5)
                : ColorToColorRef(0x08, 0x17, 0x25);
            _ = DwmSetWindowAttribute(handle, 35, ref captionColor, sizeof(uint));

            var textColor = useLightTheme
                ? ColorToColorRef(0x18, 0x30, 0x49)
                : ColorToColorRef(0xFF, 0xFF, 0xFF);
            _ = DwmSetWindowAttribute(handle, 36, ref textColor, sizeof(uint));
        }
        catch
        {
        }
    }

    private void ApplyThemeToOpenWindows(bool useLightTheme)
    {
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
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
            WindowRootBorder.CornerRadius = new CornerRadius(0);
            WindowRootGrid.Margin = new Thickness(8);
            return;
        }

        chrome.CornerRadius = new CornerRadius(14);
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
