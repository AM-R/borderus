using System.Windows;
using Borderus.Models;
using Borderus.Services;

namespace Borderus;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        BorderSettings settings = SettingsStore.Load();
        settings.Language = LocalizationService.Normalize(settings.Language);
        LocalizationService.Apply(settings.Language);
        _singleInstance = new Mutex(true, @"Local\Borderus.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show(LocalizationService.Text("AlreadyRunning"), "Borderus",
                MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }
        StartupService.Synchronize(settings.StartWithWindows);
        _mainWindow = new MainWindow(settings);
        MainWindow = _mainWindow;
        if (!e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase)) _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
