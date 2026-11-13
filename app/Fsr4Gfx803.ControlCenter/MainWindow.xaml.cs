using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Fsr4Gfx803.ControlCenter.Models;
using Fsr4Gfx803.ControlCenter.Services;
using Microsoft.Win32;

namespace Fsr4Gfx803.ControlCenter;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ProcessRow> _processes = new();
    private Fsr4Profile _profile = new();

    public MainWindow()
    {
        InitializeComponent();
        ProcessGrid.ItemsSource = _processes;
        LoadProfileIntoUi(ProfileStore.Load());
        RefreshProcesses();
        AppendLog("Control Center started.");
        AppendLog("Notice: generic DLL loading is implemented; the Windows FSR4 shader runtime is not.");
    }

    private void DashboardNav_Click(object sender, RoutedEventArgs e) => ShowPage("dashboard");
    private void InjectNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPage("inject");
        RefreshProcesses();
    }
    private void BridgeNav_Click(object sender, RoutedEventArgs e) => ShowPage("bridge");
    private void ProfileNav_Click(object sender, RoutedEventArgs e) => ShowPage("profile");
    private void DiagnosticsNav_Click(object sender, RoutedEventArgs e) => ShowPage("diagnostics");

    private void ShowPage(string page)
    {
        DashboardPage.Visibility = page == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        InjectPage.Visibility = page == "inject" ? Visibility.Visible : Visibility.Collapsed;
        BridgePage.Visibility = page == "bridge" ? Visibility.Visible : Visibility.Collapsed;
        ProfilePage.Visibility = page == "profile" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = page == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        DashboardNav.Background = page == "dashboard" ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 38, 76)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 53, 74));
        InjectNav.Background = page == "inject" ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 38, 76)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 53, 74));
        BridgeNav.Background = page == "bridge" ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 38, 76)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 53, 74));
        ProfileNav.Background = page == "profile" ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 38, 76)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 53, 74));
        DiagnosticsNav.Background = page == "diagnostics" ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 38, 76)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 53, 74));
    }

    private void RefreshProcesses_Click(object sender, RoutedEventArgs e) => RefreshProcesses();

    private void RefreshProcesses()
    {
        _processes.Clear();
        foreach (Process process in Process.GetProcesses().OrderBy(p => SafeName(p), StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                    continue;
                string name = process.ProcessName;
                string title = process.MainWindowTitle;
                _processes.Add(new ProcessRow(name, process.Id, title));
            }
            catch
            {
                // Some system and protected processes deliberately deny metadata access.
            }
            finally
            {
                process.Dispose();
            }
        }
        AppendLog($"Process list refreshed: {_processes.Count} processes.");
    }

    private static string SafeName(Process process)
    {
        try { return process.ProcessName; }
        catch { return ""; }
    }

    private void BrowseGameExe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the game's executable",
            Filter = "Windows executables (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        GameExeText.Text = dialog.FileName;
        AddonDirectoryText.Text = ReshadeBridgeManager.GetSuggestedAddonDirectory(dialog.FileName);
        if (string.IsNullOrWhiteSpace(OverrideDirectoryText.Text))
            OverrideDirectoryText.Text = Path.Combine(AddonDirectoryText.Text, "fsr4-overrides");

        if (ReshadeBridgeManager.IsReshadePresent(dialog.FileName, out string reason))
            BridgeStatusText.Text = reason;
        else
            BridgeStatusText.Text = reason;
    }

    private void BrowseAddonDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select ReShade's add-on search directory" };
        if (dialog.ShowDialog(this) == true)
        {
            AddonDirectoryText.Text = dialog.FolderName;
            if (string.IsNullOrWhiteSpace(OverrideDirectoryText.Text))
                OverrideDirectoryText.Text = Path.Combine(dialog.FolderName, "fsr4-overrides");
        }
    }

    private void BrowseOverrideDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the folder containing DXIL shader overrides" };
        if (dialog.ShowDialog(this) == true)
            OverrideDirectoryText.Text = dialog.FolderName;
    }

    private void InstallBridge_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = ReshadeBridgeManager.Install(
                GameExeText.Text.Trim().Trim('"'),
                AddonDirectoryText.Text.Trim().Trim('"'),
                OverrideDirectoryText.Text.Trim().Trim('"'),
                DumpShadersCheck.IsChecked == true,
                VerboseBridgeLogCheck.IsChecked == true);
            AddonDirectoryText.Text = result.AddonDirectory;
            OverrideDirectoryText.Text = result.OverrideDirectory;
            BridgeStatusText.Text = result.Message + Environment.NewLine +
                "Add-on: " + Path.Combine(result.AddonDirectory, "FSR4-GFX803.addon64") +
                Environment.NewLine + "Config: " + result.ConfigPath;
            AppendLog("ReShade bridge installed for " + GameExeText.Text);
            MessageBox.Show(this, result.Message, "ReShade bridge installed",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            BridgeStatusText.Text = ex.Message;
            AppendLog("Bridge installation failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Bridge installation failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveBridge_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string directory = AddonDirectoryText.Text.Trim().Trim('"');
            var result = ReshadeBridgeManager.RemoveAndRestore(directory);
            BridgeStatusText.Text = result.Message;
            AppendLog("ReShade bridge removal: " + result.Message);
            MessageBox.Show(this, result.Message, "ReShade bridge removal",
                MessageBoxButton.OK, result.RemovedAddon && result.RemovedConfig ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            BridgeStatusText.Text = ex.Message;
            AppendLog("Bridge removal failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Bridge removal failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenShaderDumpFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string addonDirectory = AddonDirectoryText.Text.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(addonDirectory))
                throw new InvalidOperationException("Select a game executable or ReShade add-on directory first.");
            string dump = Path.Combine(addonDirectory, "fsr4-overrides", "dump");
            Directory.CreateDirectory(dump);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true,
                ArgumentList = { dump }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot open shader dump folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BuildDxilOverrides_Click(object sender, RoutedEventArgs e)
    {
        string addonDirectory = AddonDirectoryText.Text.Trim().Trim('"');
        string overrideDirectory = OverrideDirectoryText.Text.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(addonDirectory) || string.IsNullOrWhiteSpace(overrideDirectory))
        {
            MessageBox.Show(this, "Select the game and override directory first.", "Missing bridge paths",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string configPath = Path.Combine(addonDirectory, "FSR4-GFX803.ini");
        if (!File.Exists(configPath))
        {
            MessageBox.Show(this, "Install the ReShade bridge for this game first.", "Bridge not installed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string configuredOverride = ReadIniValue(configPath, "FSR4GFX803", "OverrideDirectory") ?? "";
        string resolvedConfigured = Path.GetFullPath(Path.IsPathRooted(configuredOverride)
            ? configuredOverride
            : Path.Combine(addonDirectory, configuredOverride));
        if (!PathsEqual(resolvedConfigured, overrideDirectory))
        {
            MessageBox.Show(this,
                "The override directory in this page does not match the folder configured in FSR4-GFX803.ini. Remove and reinstall the bridge with the selected override directory before building.",
                "Override directory mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string dumpDirectory = Path.Combine(addonDirectory, "fsr4-overrides", "dump");
        if (!Directory.Exists(dumpDirectory) || !Directory.EnumerateFiles(dumpDirectory, "*.dxil", SearchOption.TopDirectoryOnly).Any())
        {
            MessageBox.Show(this,
                "No shader dumps were found. Enable shader dumping, launch the game, select AMD FSR 4, and play until FSR4 builds its compute pipelines. Then close the game and try again.",
                "No FSR4 shader dumps", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string toolsRoot = Path.Combine(AppContext.BaseDirectory, "tools");
        string dxcDirectory = Path.Combine(toolsRoot, "dxc");
        string dxc = Path.Combine(dxcDirectory, "dxc.exe");
        string dxcompiler = Path.Combine(dxcDirectory, "dxcompiler.dll");
        string assembler = Path.Combine(dxcDirectory, "dxilasm-win.exe");
        string script = Path.Combine(toolsRoot, "windows", "build-fsr4-gfx803-dxil-overrides.ps1");
        foreach (string required in new[] { dxc, dxcompiler, assembler, script })
        {
            if (!File.Exists(required))
            {
                MessageBox.Show(this, "The portable package is missing a DXIL tool required for transformation:\n" + required,
                    "DXIL tool missing", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        Directory.CreateDirectory(overrideDirectory);
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = ProfileStore.DataDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("-InputDirectory");
        start.ArgumentList.Add(dumpDirectory);
        start.ArgumentList.Add("-OutputDirectory");
        start.ArgumentList.Add(overrideDirectory);
        start.ArgumentList.Add("-DxcPath");
        start.ArgumentList.Add(dxc);
        start.ArgumentList.Add("-DxCompilerPath");
        start.ArgumentList.Add(dxcompiler);
        start.ArgumentList.Add("-AssemblerPath");
        start.ArgumentList.Add(assembler);

        AppendLog("Building GFX803 DXIL replacements from " + dumpDirectory);
        BridgeStatusText.Text = "Expanding packed-int8 dot products and validating DXIL. Do not close the app…";
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell could not be started.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string outText = await stdout;
            string errText = await stderr;
            if (!string.IsNullOrWhiteSpace(outText)) AppendLog(outText.TrimEnd());
            if (!string.IsNullOrWhiteSpace(errText)) AppendLog(errText.TrimEnd());
            if (process.ExitCode == 0)
            {
                BridgeStatusText.Text = "DXIL replacements built and accepted by Microsoft's DXC validator. Restart the game to load them. This does not yet prove AMD's GFX803 ISA selection or performance.";
                MessageBox.Show(this, BridgeStatusText.Text + "\n\nOutput folder:\n" + overrideDirectory,
                    "DXIL replacements built", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                BridgeStatusText.Text = "DXIL transformation failed. See the ReShade bridge diagnostics and PowerShell output.";
                MessageBox.Show(this, "The shader transformation failed with exit code " + process.ExitCode +
                    ". Inspect the messages in Diagnostics and the generated tool logs.",
                    "DXIL transformation failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            AppendLog("DXIL replacement build exited with code " + process.ExitCode);
        }
        catch (Exception ex)
        {
            BridgeStatusText.Text = ex.Message;
            AppendLog("DXIL transformation error: " + ex.Message);
            MessageBox.Show(this, ex.Message, "DXIL transformation error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? ReadIniValue(string path, string section, string key)
    {
        if (!File.Exists(path)) return null;
        string current = "";
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1].Trim();
                continue;
            }
            if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;
            int equals = line.IndexOf('=');
            if (equals > 0 && string.Equals(line[..equals].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return line[(equals + 1)..].Trim();
        }
        return null;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
            string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void BrowseDll_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select an x64 DLL to load into the selected process",
            Filter = "Dynamic-link libraries (*.dll)|*.dll|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            DllPathText.Text = dialog.FileName;
    }

    private async void Inject_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessGrid.SelectedItem is not ProcessRow selected)
        {
            MessageBox.Show(this, "Select a running process first.", "No process selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string dll = DllPathText.Text.Trim().Trim('"');
        if (!File.Exists(dll))
        {
            MessageBox.Show(this, "Select an existing DLL file.", "DLL not found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Load this DLL into {selected.Name} (PID {selected.Id})?\n\n{dll}\n\nOnly do this with trusted DLLs and offline/single-player games. This does not activate FSR4 unless the selected DLL contains a compatible FSR4 runtime.",
            "Confirm DLL attach",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        AppendLog($"Attaching {Path.GetFileName(dll)} to {selected.Name} ({selected.Id})...");
        try
        {
            var result = await Task.Run(() => NativeInjector.InjectDll(selected.Id, dll));
            AppendLog((result.Success ? "SUCCESS: " : "FAILED: ") + result.Message);
            MessageBox.Show(this, result.Message, result.Success ? "DLL loaded" : "Attach failed",
                MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Attach failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadProfileIntoUi(Fsr4Profile profile)
    {
        _profile = profile;
        SetCombo(DllVariantCombo, profile.UpscalerDll);
        SetCombo(TierCombo, profile.Tier);
        CustomSetText.Text = profile.CustomShaderSet;
        AutoTuneCheck.IsChecked = profile.AutoTune;
        GpuProfileCheck.IsChecked = profile.GpuProfiling;
        DebugCheck.IsChecked = profile.ShaderDebugLog;
        DumpSpirvCheck.IsChecked = profile.DumpSpirv;
        ExpandSdotCheck.IsChecked = profile.ExpandSignedDot;
        ProfileEachCheck.IsChecked = profile.ProfileEachShader;
        AutoTuneSamplesText.Text = profile.AutoTuneSamples.ToString();
        ProfileMinBytesText.Text = profile.ProfileMinimumModuleBytes.ToString();
        ProfileIntervalText.Text = profile.ProfileIntervalSeconds.ToString();
        ShaderSetsText.Text = profile.ShaderSetsDirectory;
        VariantsDirectoryText.Text = profile.VariantsDirectory;
        ShaderCacheText.Text = profile.ShaderCacheDirectory;
        DumpDirectoryText.Text = profile.DumpDirectory;
        TuneCacheText.Text = profile.TuneCacheFile;
        DllPathText.Text = profile.RuntimeDll;
        FooterProfileName.Text = profile.Name;
        ProfileStatusText.Text = $"Loaded: {ProfileStore.ProfilePath}";
    }

    private static void SetCombo(System.Windows.Controls.ComboBox combo, string value)
    {
        foreach (var item in combo.Items)
        {
            if (item is System.Windows.Controls.ComboBoxItem option &&
                string.Equals(option.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private Fsr4Profile ReadProfileFromUi()
    {
        string dllVariant = (DllVariantCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "hybrid";
        string tier = (TierCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "balanced";
        return new Fsr4Profile
        {
            Name = $"{dllVariant} - {tier}",
            RuntimeDll = DllPathText.Text.Trim(),
            UpscalerDll = dllVariant,
            Tier = tier,
            CustomShaderSet = CustomSetText.Text.Trim(),
            AutoTune = AutoTuneCheck.IsChecked == true,
            AutoTuneSamples = ParsePositiveInt(AutoTuneSamplesText.Text, 60, 10000),
            GpuProfiling = GpuProfileCheck.IsChecked == true,
            ProfileMinimumModuleBytes = ParsePositiveInt(ProfileMinBytesText.Text, 40000, 10000000),
            ProfileIntervalSeconds = ParsePositiveInt(ProfileIntervalText.Text, 5, 3600),
            ProfileEachShader = ProfileEachCheck.IsChecked == true,
            ShaderDebugLog = DebugCheck.IsChecked == true,
            DumpSpirv = DumpSpirvCheck.IsChecked == true,
            ExpandSignedDot = ExpandSdotCheck.IsChecked == true,
            ShaderSetsDirectory = ShaderSetsText.Text.Trim(),
            VariantsDirectory = VariantsDirectoryText.Text.Trim(),
            ShaderCacheDirectory = ShaderCacheText.Text.Trim(),
            DumpDirectory = DumpDirectoryText.Text.Trim(),
            TuneCacheFile = TuneCacheText.Text.Trim()
        };
    }

    private static int ParsePositiveInt(string value, int fallback, int maximum)
    {
        if (!int.TryParse(value, out int parsed) || parsed < 1)
            return fallback;
        return Math.Min(parsed, maximum);
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _profile = ReadProfileFromUi();
            ProfileStore.Save(_profile);
            FooterProfileName.Text = _profile.Name;
            ProfileStatusText.Text = $"Saved to {ProfileStore.ProfilePath}";
            AppendLog($"Profile saved: {_profile.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save profile", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        LoadProfileIntoUi(ProfileStore.Load());
        AppendLog("Profile loaded from local app-data.");
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export FSR4 GFX803 profile",
                FileName = "fsr4-gfx803-profile.json",
                Filter = "JSON profile (*.json)|*.json"
            };
            if (dialog.ShowDialog(this) == true)
            {
                var profile = ReadProfileFromUi();
                File.WriteAllText(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(profile,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                ProfileStatusText.Text = $"Exported: {dialog.FileName}";
                AppendLog($"Profile exported: {dialog.FileName}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BrowseRga_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select AMD Radeon GPU Analyzer",
            Filter = "Radeon GPU Analyzer (rga.exe)|rga.exe|Executable (*.exe)|*.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            RgaPathText.Text = dialog.FileName;
    }

    private async void RunProbe_Click(object sender, RoutedEventArgs e)
    {
        string? script = FindProbeScript();
        if (script is null)
        {
            AppendLog("ERROR: run-rga-gfx803-probe.ps1 was not found beside the app.");
            MessageBox.Show(this, "The package is missing tools/windows/run-rga-gfx803-probe.ps1. Download the complete build ZIP, not only the EXE.",
                "Probe script missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string output = Path.Combine(ProfileStore.DataDirectory, "diagnostics", "rga-gfx803");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = ProfileStore.DataDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("-OutputDirectory");
        start.ArgumentList.Add(output);
        string rga = RgaPathText.Text.Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(rga))
        {
            start.ArgumentList.Add("-RgaPath");
            start.ArgumentList.Add(rga);
        }

        AppendLog("Starting the GFX803 DX12 ISA probe...");
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell could not be started.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string outText = await stdout;
            string errText = await stderr;
            if (!string.IsNullOrWhiteSpace(outText)) AppendLog(outText.TrimEnd());
            if (!string.IsNullOrWhiteSpace(errText)) AppendLog(errText.TrimEnd());
            AppendLog($"Probe process exited with code {process.ExitCode}. Output: {output}");
            if (process.ExitCode == 0)
                MessageBox.Show(this, $"Probe finished. Inspect summary.json and the .isa files in:\n{output}", "Probe completed", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(this, $"Probe failed with exit code {process.ExitCode}. See the log output and RGA log files in:\n{output}", "Probe failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Could not run probe", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? FindProbeScript()
    {
        string packaged = Path.Combine(AppContext.BaseDirectory, "tools", "windows", "run-rga-gfx803-probe.ps1");
        if (File.Exists(packaged)) return packaged;

        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "tools", "windows", "run-rga-gfx803-probe.ps1");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        return null;
    }

    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(ProfileStore.DataDirectory, "diagnostics");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ProfileStore.DataDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ProfileStore.LogPath}\"") { UseShellExecute = true });
    }

    private void AppendLog(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendLog(message));
            return;
        }
        LogTextBox.AppendText(line + Environment.NewLine);
        LogTextBox.ScrollToEnd();
        try
        {
            Directory.CreateDirectory(ProfileStore.DataDirectory);
            File.AppendAllText(ProfileStore.LogPath, line + Environment.NewLine);
        }
        catch
        {
            // The on-screen log remains available if writing to disk is blocked.
        }
    }

    private sealed record ProcessRow(string Name, int Id, string WindowTitle);
}
