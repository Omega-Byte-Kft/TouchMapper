using System.Windows;
using System.Windows.Controls;

namespace TouchMapper.Configurator.Pages;

public partial class TopologyPage : UserControl
{
    private readonly WizardState _state;

    public TopologyPage(WizardState state)
    {
        InitializeComponent();
        _state = state;
        Loaded += TopologyPage_Loaded;
    }

    private void TopologyPage_Loaded(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel();

        if (_state.LiveTouchGroups.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No touch topology groups detected. Check USB connections.",
                Foreground = System.Windows.Media.Brushes.Red
            });
        }

        for (var gi = 0; gi < _state.LiveTouchGroups.Count; gi++)
        {
            var group = _state.LiveTouchGroups[gi];

            var groupBox = new Border
            {
                BorderBrush = System.Windows.Media.Brushes.CornflowerBlue,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var groupPanel = new StackPanel();
            groupPanel.Children.Add(new TextBlock
            {
                Text = $"Topology Group {gi + 1}",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            for (var di = 0; di < group.Count; di++)
            {
                var device = group[di];
                var role = di == 0 ? "Anchor (fewest hops)" : $"Child {di} (+{di} hop(s) via anchor hub)";
                var text = $"  {(di == 0 ? "★" : "└")} {role}\n" +
                           $"     Hops: {device.HopCount}  |  {device.PnpInstanceId}";
                groupPanel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 2)
                });
            }

            groupBox.Child = groupPanel;
            panel.Children.Add(groupBox);
        }

        GroupsList.ItemsSource = null;
        GroupsList.Items.Clear();
        GroupsList.Items.Add(panel);
    }
}
