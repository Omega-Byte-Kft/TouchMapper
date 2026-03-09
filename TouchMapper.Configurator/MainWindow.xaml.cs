using System.Windows;
using System.Windows.Controls;
using TouchMapper.Configurator.Pages;

namespace TouchMapper.Configurator;

public partial class MainWindow : Window
{
    private readonly WizardState _state = new();
    private readonly List<(string Title, string Subtitle, Func<UserControl> Factory)> _pages;
    private int _currentIndex = -1;

    public MainWindow()
    {
        InitializeComponent();

        _pages =
        [
            ("Welcome", "Touch-to-display mapping configurator",
                () => new WelcomePage(OnScanReady)),
            ("Assign Bindings", "Bind each touch path to its display",
                () => new BindingPage(_state, OnBindingChanged)),
            ("Apply & Install", "Apply mapping and install background service",
                () => new ApplyPage(_state)),
        ];

        NavigateTo(0);
    }

    private void NavigateTo(int index)
    {
        _currentIndex = index;
        var (title, subtitle, factory) = _pages[index];
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;

        PageContent.Content = factory();

        BackButton.IsEnabled = index > 0;
        NextButton.Visibility = index < _pages.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.Visibility = index == _pages.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.IsEnabled = false;
    }

    private void OnScanReady() => NavigateTo(1);

    private void OnBindingChanged(bool allAssigned)
    {
        NextButton.IsEnabled = allAssigned;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentIndex > 0) NavigateTo(_currentIndex - 1);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentIndex < _pages.Count - 1) NavigateTo(_currentIndex + 1);
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e) => Close();
}
