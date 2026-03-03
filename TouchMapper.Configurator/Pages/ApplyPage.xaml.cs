using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TouchMapper.Core.Config;
using TouchMapper.Core.Detection;
using TouchMapper.Core.Mapping;

namespace TouchMapper.Configurator.Pages;

public partial class ApplyPage : UserControl
{
    private readonly WizardState _state;
    private static readonly string ServiceInstallDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     "TouchMapper");

    public ApplyPage(WizardState state)
    {
        InitializeComponent();
        _state = state;
        Loaded += (_, _) => BuildSummary();
    }

    private void BuildSummary()
    {
        var lines = new List<string>();

        var cfg = _state.Config;
        if (cfg.TopologyGroups.Count > 0)
        {
            lines.Add($"Chained groups: {cfg.TopologyGroups.Count}");
            foreach (var g in cfg.TopologyGroups)
            {
                var names = new List<string> { g.AnchorFriendlyName + " (anchor)" };
                names.AddRange(g.Children.Select(c => c.FriendlyName));
                lines.Add($"  - {string.Join(", ", names)}");
            }
        }

        if (cfg.DirectMappings.Count > 0)
        {
            lines.Add($"Individual mappings: {cfg.DirectMappings.Count}");
            foreach (var dm in cfg.DirectMappings)
                lines.Add($"  - {dm.MonitorFriendlyName} ({dm.MonitorEdidSerial})");
        }

        if (lines.Count == 0)
            lines.Add("No mappings configured.");

        SummaryText.Text = string.Join("\n", lines);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        ApplyButton.IsEnabled = false;
        ApplyStatus.Text = "...";

        try
        {
            // Save config first
            ConfigStore.Save(_state.Config);

            // Compute and apply mappings
            await Task.Run(() =>
            {
                var monitors = EdidDetector.GetActiveMonitors();
                var touches  = TouchDetector.GetActiveTouchDevices();
                var entries  = MappingEngine.ComputeEntries(_state.Config, monitors, touches);
                WispManager.ApplyMappings(entries);
            });

            ApplyStatus.Text = "✔";
            ApplyStatus.Foreground = System.Windows.Media.Brushes.Green;
            AppendDetail("Mapping applied successfully.");
        }
        catch (Exception ex)
        {
            ApplyStatus.Text = "✘";
            ApplyStatus.Foreground = System.Windows.Media.Brushes.Red;
            AppendDetail($"Apply failed: {ex.Message}");
            ApplyButton.IsEnabled = true;
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        InstallStatus.Text = "...";

        try
        {
            await Task.Run(() => InstallService());
            InstallStatus.Text = "✔";
            InstallStatus.Foreground = System.Windows.Media.Brushes.Green;
            AppendDetail("Service installed and started.");
        }
        catch (Exception ex)
        {
            InstallStatus.Text = "✘";
            InstallStatus.Foreground = System.Windows.Media.Brushes.Red;
            AppendDetail($"Install failed: {ex.Message}");
            InstallButton.IsEnabled = true;
        }
    }

    private static void InstallService()
    {
        // Locate the service build output directory.
        // Strategy: walk up from Configurator's bin dir to the solution root,
        // then find the Service project's matching output.
        var serviceOutputDir = FindServiceOutputDir();

        if (serviceOutputDir is null || !File.Exists(Path.Combine(serviceOutputDir, "TouchMapper.Service.exe")))
            throw new FileNotFoundException(
                "TouchMapper.Service build output not found.\n" +
                "Build the TouchMapper.Service project before installing.");

        // Stop existing service before overwriting files
        RunSc("stop TouchMapper");
        RunSc("delete TouchMapper");

        Directory.CreateDirectory(ServiceInstallDir);

        // Copy all files from service output (DLLs, configs, exe, etc.)
        foreach (var file in Directory.GetFiles(serviceOutputDir))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            // Skip PDB files — not needed at runtime
            if (ext == ".pdb") continue;
            File.Copy(file, Path.Combine(ServiceInstallDir, Path.GetFileName(file)), overwrite: true);
        }

        // Copy runtimes subdirectory (native/platform-specific DLLs)
        var runtimesDir = Path.Combine(serviceOutputDir, "runtimes");
        if (Directory.Exists(runtimesDir))
            CopyDirectory(runtimesDir, Path.Combine(ServiceInstallDir, "runtimes"));

        var destExe = Path.Combine(ServiceInstallDir, "TouchMapper.Service.exe");

        // Register and start the service
        RunSc($"create TouchMapper binPath= \"{destExe}\" start= auto displayname= \"TouchMapper\"");
        RunSc("start TouchMapper");
    }

    private static string? FindServiceOutputDir()
    {
        // Walk up from Configurator base dir to find solution root (contains TouchMapper.sln)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TouchMapper.sln")))
            {
                // Found solution root — look for service output in matching configuration
                // Configurator path: .../TouchMapper.Configurator/bin/{Config}/{TFM}/
                // Extract config and TFM from our own path
                var configDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                var parts = configDir.Split(Path.DirectorySeparatorChar);
                // Find "bin" in path, then config is next, TFM is after
                for (int i = parts.Length - 1; i >= 2; i--)
                {
                    if (string.Equals(parts[i - 2], "bin", StringComparison.OrdinalIgnoreCase))
                    {
                        var config = parts[i - 1]; // e.g. "Debug"
                        var tfm = parts[i];         // e.g. "net8.0-windows"
                        var candidate = Path.Combine(dir.FullName, "TouchMapper.Service", "bin", config, tfm);
                        if (Directory.Exists(candidate))
                            return candidate;
                        break;
                    }
                }

                // Fallback: search for any service output with the exe
                var serviceProject = Path.Combine(dir.FullName, "TouchMapper.Service");
                if (Directory.Exists(serviceProject))
                {
                    foreach (var d in Directory.GetDirectories(serviceProject, "*", SearchOption.AllDirectories))
                    {
                        if (File.Exists(Path.Combine(d, "TouchMapper.Service.exe")) &&
                            File.Exists(Path.Combine(d, "TouchMapper.Service.dll")))
                            return d;
                    }
                }
                break;
            }
            dir = dir.Parent;
        }

        // Last resort: check next to Configurator
        var local = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(local, "TouchMapper.Service.exe")) &&
            File.Exists(Path.Combine(local, "TouchMapper.Service.dll")))
            return local;

        return null;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (var sub in Directory.GetDirectories(sourceDir))
            CopyDirectory(sub, Path.Combine(destDir, Path.GetFileName(sub)));
    }

    private static void RunSc(string args)
    {
        var psi = new ProcessStartInfo("sc.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi) ?? throw new Exception($"Failed to start sc.exe {args}");
        proc.WaitForExit(10_000);
        // sc stop/delete are allowed to fail (service may not exist yet)
    }

    private void AppendDetail(string line)
    {
        Dispatcher.Invoke(() =>
        {
            DetailText.Text = string.IsNullOrEmpty(DetailText.Text)
                ? line
                : DetailText.Text + "\n" + line;
        });
    }
}
