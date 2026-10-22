using GlassDock.App;
using System.Windows;

namespace GlassDock.PoC.Wpf;

public partial class App : System.Windows.Application
{
    private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        DockLogger.Error("Unhandled WPF UI exception.", e.Exception);
        System.Windows.MessageBox.Show("The Dock encountered an error. Details were written to the local app log.",
            "GlassDock-Win", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
