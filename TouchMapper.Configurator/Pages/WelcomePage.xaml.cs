using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace TouchMapper.Configurator.Pages;

public partial class WelcomePage : UserControl
{
    private readonly Action _onStart;

    public WelcomePage(Action onStart)
    {
        InitializeComponent();
        _onStart = onStart;
    }

    private void StartScan_Click(object sender, System.Windows.RoutedEventArgs e)
        => _onStart();

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
