using System.Windows;
using ZapretManager.Services;
using ZapretManager.ViewModels;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using System.ServiceProcess;
using System.Security.Cryptography;
using Forms = System.Windows.Forms;

namespace ZapretManager;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexNamePrefix = @"Local\ZapretManager.Instance.";
    private const string ActivateEventNamePrefix = @"Local\ZapretManager.Activate.";
    private const string AdminTaskLogEnvironmentVariable = "ZAPRETMANAGER_ADMIN_TASK_LOG";

    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateEventRegistration;

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 &&
            string.Equals(e.Args[0], "--service-host", StringComparison.OrdinalIgnoreCase))
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ZapretWindowsService.RunFromArguments(e.Args);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        Forms.Application.ThreadException += Application_ThreadException;

        if (e.Args.Length > 0)
        {
            var adminTaskLogPath = Environment.GetEnvironmentVariable(AdminTaskLogEnvironmentVariable);
            try
            {
                var exitCode = await AdminTaskDispatcher.TryRunAsync(e.Args);
                TryWriteAdminTaskLog(adminTaskLogPath, exitCode.HasValue ? $"EXIT={exitCode.Value}" : "NO_MATCH");
                if (exitCode.HasValue)
                {
                    Shutdown(exitCode.Value);
                    return;
                }
            }
            catch (Exception ex)
            {
                TryWriteAdminTaskLog(adminTaskLogPath, ex.ToString());
                DialogService.ShowError(ex, "Zapret Manager");
                Shutdown(1);
                return;
            }
        }

        if (!EnsureSingleInstance())
        {
            Shutdown();
            return;
        }

        try
        {
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
        catch (Exception ex)
        {
            DialogService.ShowError(ex, "Zapret Manager");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        Forms.Application.ThreadException -= Application_ThreadException;
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
        var instanceKey = ResolveInstanceKey();
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexNamePrefix + instanceKey, out var createdNew);
        _ownsInstanceMutex = createdNew;
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventNamePrefix + instanceKey);

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

    private static string ResolveInstanceKey()
    {
        var processPath = Environment.ProcessPath;
        var pathSeed = !string.IsNullOrWhiteSpace(processPath)
            ? processPath
            : Path.Combine(AppContext.BaseDirectory, "ZapretManager.exe");
        var fullPath = Path.GetFullPath(pathSeed).ToUpperInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath));
        return Convert.ToHexString(bytes[..8]);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DialogService.ShowError(e.Exception, "Zapret Manager");
        e.Handled = true;
    }

    private void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
    {
        DialogService.ShowError(e.Exception, "Zapret Manager");
    }

    private static void TryWriteAdminTaskLog(string? path, string content)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content);
        }
        catch
        {
        }
    }

}
