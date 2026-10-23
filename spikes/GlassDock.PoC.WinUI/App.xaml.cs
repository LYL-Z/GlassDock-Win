using Microsoft.UI.Xaml;

namespace GlassDock.PoC.WinUI;

public partial class App : Application
{
    private Window? _window;
    private Window? _compositionBackgroundWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var compositionProbeMode = Environment.GetEnvironmentVariable("GLASSDOCK_M0_COMPOSITION_PROBE_MODE");
        if (string.Equals(compositionProbeMode, "control", StringComparison.Ordinal))
        {
            _window = new CompositionBlurTestWindow(includeBlurSurface: true, "independent-control");
            _window.Activate();
            return;
        }

        if (string.Equals(compositionProbeMode, "dock", StringComparison.Ordinal))
        {
            _compositionBackgroundWindow = new CompositionBlurTestWindow(includeBlurSurface: false, "controlled-background");
            _compositionBackgroundWindow.Activate();
            var compositionDock = new MainWindow();
            _window = compositionDock;
            compositionDock.ShowWithoutActivation();
            return;
        }

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
