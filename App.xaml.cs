using System.Windows;

namespace Borderus;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, @"Local\Borderus.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show("Borderus уже запущен и находится в системном трее.", "Borderus",
                MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
