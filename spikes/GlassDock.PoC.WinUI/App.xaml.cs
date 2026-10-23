using Microsoft.UI.Xaml;

namespace GlassDock.PoC.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainWindow = new MainWindow();
        _window = mainWindow;
        if (string.Equals(Environment.GetEnvironmentVariable("GLASSDOCK_M0_NONACTIVATING_START"), "1",
                StringComparison.Ordinal))
        {
            mainWindow.ShowWithoutActivation();
            return;
        }

        _window.Activate();
        mainWindow.QueueTopmostReassertion();
    }
}
