using System.Threading;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using SoundSwitchLite.Services;

namespace SoundSwitchLite;

public partial class App : Application
{
    private Mutex? _mutex;
    private TaskbarIcon? _trayIcon;

    public static AudioDeviceService AudioDeviceService { get; private set; } = null!;
    public static HotkeyService HotkeyService { get; private set; } = null!;
    public static SettingsService SettingsService { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single-instance enforcement
        _mutex = new Mutex(true, "SoundSwitchLite_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("SoundSwitch Lite is already running.", "SoundSwitch Lite",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        AudioDeviceService = new AudioDeviceService();
        HotkeyService = new HotkeyService();
        SettingsService = new SettingsService();

        // Set up tray icon
        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.TrayLeftMouseUp += (_, _) => ToggleMainWindow();

        var contextMenu = new System.Windows.Controls.ContextMenu();

        var showItem = new System.Windows.Controls.MenuItem { Header = "Show" };
        showItem.Click += (_, _) => ShowMainWindow();
        contextMenu.Items.Add(showItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApp();
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;

        // Create the main window. If started with --minimized, keep it hidden (tray only).
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        if (!e.Args.Contains("--minimized"))
            mainWindow.Show();
    }

    private void ShowMainWindow()
    {
        if (MainWindow != null)
        {
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }
    }

    private void ToggleMainWindow()
    {
        if (MainWindow == null) return;
        if (MainWindow.IsVisible)
            MainWindow.Hide();
        else
            ShowMainWindow();
    }

    public void ExitApp()
    {
        HotkeyService.UnregisterAll();
        AudioDeviceService.Dispose();
        _trayIcon?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}


