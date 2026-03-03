# TouchMapper

**TouchMapper** is a Windows utility that fixes touch-to-display mapping after sleep, USB reconnect, or dock/undock cycles. Windows often loses track of which touch digitizer belongs to which monitor — TouchMapper detects your hardware topology once and keeps it correct automatically via a background service.

## The Problem

When multiple touch-enabled monitors are connected (especially through USB hubs or daisy chains), Windows frequently assigns touch input to the wrong display after:

- Waking from sleep or hibernation
- Unplugging and reconnecting USB cables
- Docking or undocking a laptop
- Rebooting with monitors in a different power-on order

Manually fixing this through *Tablet PC Settings* every time is tedious and error-prone.

## Hardware Requirement

> **Important:** For automatic topology detection, the touch monitors must be **USB daisy-chained** — each monitor's USB output connects to the next monitor's USB input, forming a single chain from the PC. TouchMapper uses this chain structure (increasing USB hop counts) to reliably identify which touch digitizer belongs to which display, even after reconnection.
>
> Standalone (non-chained) touch devices are also supported via direct USB path matching, but the daisy-chain topology is the primary and most reliable detection method.

## How It Works

TouchMapper consists of two components:

1. **Configurator** — A WPF wizard that scans your hardware, detects the monitor and touch device topology, and lets you define the correct mapping.
2. **Background Service** — A Windows service that monitors for device changes and automatically re-applies the correct mapping.

### Technical Details

**Detection:**

- Monitors are detected via WMI (`WmiMonitorID`) and identified by their EDID serial number.
- Touch devices are enumerated from the HID registry (`HKLM\SYSTEM\CurrentControlSet\Enum\HID`) using HID Usage Page 0x0D (Digitizers). USB location paths and hop counts are resolved by walking the device tree via the CfgMgr32 API.
- Supports USB HID touch screens, I2C-HID laptop panels, and USB composite digitizers.

**Topology Analysis:**

- Touch devices sharing a common USB hub ancestor are grouped using a Union-Find algorithm and sorted by hop count. This allows TouchMapper to understand daisy-chained setups (e.g., multiple touch monitors connected through a single USB cable chain).

**Mapping:**

- Mappings are written to the Windows Wisp Pen Digimon registry (`HKLM\SOFTWARE\Microsoft\Wisp\Pen\Digimon`), which is the native mechanism Windows uses to associate HID digitizers with displays.
- After writing the registry entries, HID devices are restarted via `pnputil` to force Windows to pick up the new mapping.
- Two mapping modes are supported:
  - **Topology Groups** — For daisy-chained touch monitors sharing a USB path.
  - **Direct Mappings** — For standalone touch devices matched by USB location path.

**Service:**

- The background service watches for device change events via WMI. When monitors or touch devices are added or removed (but not on every touch input), it re-applies the saved mapping.
- A topology fingerprint (hash of all monitor and touch device identifiers) prevents unnecessary re-application.

**Configuration:**

- Stored as JSON at `%ProgramData%\TouchMapper\config.json`, accessible by both the Configurator (running as admin) and the service (running as SYSTEM).

## Requirements

- Windows 10 or Windows 11
- .NET 8.0 Runtime (Desktop)
- Administrator privileges (required for registry writes and service installation)

## Building

Clone the repository and build with the .NET SDK:

```bash
git clone https://github.com/user/TouchMapper.git
cd TouchMapper
dotnet build
```

The solution contains three projects:

| Project | Type | Output |
|---------|------|--------|
| `TouchMapper.Core` | Class Library | Detection, mapping, and configuration logic |
| `TouchMapper.Configurator` | WPF Application | Setup wizard with auto-detect and testing |
| `TouchMapper.Service` | Worker Service | Background service for automatic re-mapping |

All projects target `net8.0-windows`.

## Usage

### Initial Setup

1. **Run the Configurator** as administrator (`TouchMapper.Configurator.exe`).
2. Click **Start Scan** — the wizard detects all active monitors and touch devices.
3. Review the detected **USB topology** (chained and standalone devices).
4. On the **Binding** page, assign each touch device to its monitor:
   - Use **Auto-Detect Bindings** to walk through each display — just touch the screen when prompted.
   - Or manually select from the dropdown menus.
   - Use **Debug Touch** to see raw HID device paths in real time.
5. **Name** your monitors for easy identification.
6. Click **Apply** to save the configuration and install the background service.

### After Setup

The TouchMapper service runs automatically at boot. Whenever you reconnect monitors or wake from sleep, it detects the topology change and re-applies the correct touch mapping within a few seconds.

### Logs

- Configurator debug logs: `%LocalAppData%\TouchMapper\`
- Service logs: `%ProgramData%\TouchMapper\service.log`

## Project Structure

```
TouchMapper/
  TouchMapper.Core/           Shared library
    Config/                     ConfigStore (JSON persistence)
    Detection/                  EdidDetector, TouchDetector
    Mapping/                    MappingEngine, TopologyAnalyzer, WispManager
    Models/                     TouchMapperConfig, MonitorInfo, TouchDeviceInfo
  TouchMapper.Configurator/   WPF setup wizard
    Pages/                      WelcomePage, ScanPage, BindingPage, NamingPage, ApplyPage
    AutoDetectWindow.xaml       Fullscreen overlay for touch identification
    TestOverlayWindow.xaml      Test mapping verification overlay
  TouchMapper.Service/         Windows background service
    TouchMapperWorker.cs        Device change monitoring and auto-remapping
```

## License

This project is licensed under the [MIT License](LICENSE).

## Credits

Developed by **[Omega Byte Kft.](https://www.omegabyte.hu)**

This software was built with the assistance of **Claude AI** (Anthropic).
