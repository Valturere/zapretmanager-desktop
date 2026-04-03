using System.Windows;
using ZapretManager.Services;
using ZapretManager.ViewModels;
using System.Text;
using System.Threading;

namespace ZapretManager;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\ZapretManager.Instance";
    private const string ActivateEventName = @"Local\ZapretManager.Activate";

    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateEventRegistration;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (e.Args.Length > 0)
        {
            try
            {
                var exitCode = await AdminTaskDispatcher.TryRunAsync(e.Args);
                if (exitCode.HasValue)
                {
                    Shutdown(exitCode.Value);
                    return;
                }
            }
            catch (Exception ex)
            {
                DialogService.ShowError(ex.Message, "Zapret Manager");
                Shutdown(1);
                return;
            }
        }

        if (!EnsureSingleInstance())
        {
            Shutdown();
            return;
        }

        var startHidden = e.Args.Any(arg => string.Equals(arg, "--start-hidden", StringComparison.OrdinalIgnoreCase));
        var viewModel = new MainViewModel();

        var window = new MainWindow(startHidden, viewModel.UseLightThemeEnabled)
        {
            DataContext = viewModel
        };

        if (startHidden)
        {
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.WindowState = WindowState.Minimized;
        }

        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateEventRegistration?.Unregister(null);
        _activateEvent?.Dispose();

        if (_ownsInstanceMutex && _instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private bool EnsureSingleInstance()
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        _ownsInstanceMutex = createdNew;
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        if (!createdNew)
        {
            _activateEvent.Set();
            return false;
        }

        _activateEventRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow mainWindow)
                {
                    mainWindow.BringToFrontFromExternal();
                }
            }),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        return true;
    }
}
