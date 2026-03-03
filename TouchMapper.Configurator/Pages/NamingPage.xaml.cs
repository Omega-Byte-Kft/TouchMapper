using System.Windows;
using System.Windows.Controls;
using TouchMapper.Core.Models;

namespace TouchMapper.Configurator.Pages;

public partial class NamingPage : UserControl
{
    private readonly WizardState _state;
    private readonly Action _onNamingDone;

    // Stores TextBox references to read values on commit
    private readonly List<(TextBox NameBox, TopologyGroup? Group, MonitorProfile? Child, DirectMapping? Direct)> _fields = [];

    public NamingPage(WizardState state, Action onNamingDone)
    {
        InitializeComponent();
        _state = state;
        _onNamingDone = onNamingDone;
        Loaded += NamingPage_Loaded;
    }

    private void NamingPage_Loaded(object sender, RoutedEventArgs e)
    {
        _fields.Clear();
        MonitorList.Children.Clear();

        var hasTopology = _state.Config.TopologyGroups.Count > 0;
        var hasDirect = _state.Config.DirectMappings.Count > 0;

        if (!hasTopology && !hasDirect)
        {
            MonitorList.Children.Add(new TextBlock
            {
                Text = "No devices configured. Go back and re-run the scan.",
                Foreground = System.Windows.Media.Brushes.Red
            });
            return;
        }

        // Topology groups
        if (hasTopology)
        {
            foreach (var group in _state.Config.TopologyGroups)
            {
                AddMonitorRow(group, isAnchor: true, name: group.AnchorFriendlyName, child: null, direct: null);
                foreach (var child in group.Children)
                    AddMonitorRow(group, isAnchor: false, name: child.FriendlyName, child: child, direct: null);
            }
        }

        // Direct mappings
        if (hasDirect)
        {
            if (hasTopology)
            {
                MonitorList.Children.Add(new TextBlock
                {
                    Text = "Individual Devices",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 12, 0, 6)
                });
            }

            foreach (var dm in _state.Config.DirectMappings)
                AddDirectMappingRow(dm);
        }

        // Enable Next immediately — names are optional
        _onNamingDone();
    }

    private void AddMonitorRow(TopologyGroup group, bool isAnchor, string name, MonitorProfile? child, DirectMapping? direct)
    {
        var edid = isAnchor ? group.AnchorEdidSerial : child!.EdidSerial;
        var role = isAnchor ? "★ Anchor" : "  Child";

        var row = new Border
        {
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

        var roleLabel = new TextBlock
        {
            Text = role,
            FontWeight = isAnchor ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(roleLabel, 0);

        var edidLabel = new TextBlock
        {
            Text = edid,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(edidLabel, 1);

        var nameBox = new TextBox
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 2, 4, 2)
        };
        Grid.SetColumn(nameBox, 2);
        nameBox.TextChanged += (_, _) => CommitNames();

        grid.Children.Add(roleLabel);
        grid.Children.Add(edidLabel);
        grid.Children.Add(nameBox);
        row.Child = grid;
        MonitorList.Children.Add(row);

        _fields.Add((nameBox, group, child, direct));
    }

    private void AddDirectMappingRow(DirectMapping dm)
    {
        var row = new Border
        {
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

        var roleLabel = new TextBlock
        {
            Text = "◆ Direct",
            FontWeight = FontWeights.Normal,
            Foreground = System.Windows.Media.Brushes.DarkSeaGreen,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(roleLabel, 0);

        var edidLabel = new TextBlock
        {
            Text = dm.MonitorEdidSerial,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(edidLabel, 1);

        var nameBox = new TextBox
        {
            Text = dm.MonitorFriendlyName,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 2, 4, 2)
        };
        Grid.SetColumn(nameBox, 2);
        nameBox.TextChanged += (_, _) => CommitNames();

        grid.Children.Add(roleLabel);
        grid.Children.Add(edidLabel);
        grid.Children.Add(nameBox);
        row.Child = grid;
        MonitorList.Children.Add(row);

        _fields.Add((nameBox, null, null, dm));
    }

    private void CommitNames()
    {
        foreach (var (box, group, child, direct) in _fields)
        {
            if (direct is not null)
            {
                direct.MonitorFriendlyName = box.Text;
            }
            else if (child is null && group is not null)
            {
                group.AnchorFriendlyName = box.Text;
            }
            else if (child is not null)
            {
                child.FriendlyName = box.Text;
            }
        }
    }
}
