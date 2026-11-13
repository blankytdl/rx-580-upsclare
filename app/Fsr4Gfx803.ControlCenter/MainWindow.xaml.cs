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
    private void ProfileNav_Click(object sender, RoutedEventArgs e) => ShowPage("profile");
    private void DiagnosticsNav_Click(object sender, RoutedEventArgs e) => ShowPage("diagnostics");

    private void ShowPage(string page)
    {
        DashboardPage.Visibility = page == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        InjectPage.Visibility = page == "inject" ? Visibility.Visible : Visibility.Collapsed;
        ProfilePage.Visibility = page == "profile" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = page == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        DashboardNav.Background = page == "dashboard" ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 38, 76)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 53, 74));
        InjectNav.Background = page == "inject" ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 38, 76)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 53, 74));
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
        ShaderSetsText.Text = profile.ShaderSetsDirectory;
        ShaderCacheText.Text = profile.ShaderCacheDirectory;
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
            GpuProfiling = GpuProfileCheck.IsChecked == true,
            ShaderDebugLog = DebugCheck.IsChecked == true,
            DumpSpirv = DumpSpirvCheck.IsChecked == true,
            ExpandSignedDot = ExpandSdotCheck.IsChecked == true,
            ShaderSetsDirectory = ShaderSetsText.Text.Trim(),
            ShaderCacheDirectory = ShaderCacheText.Text.Trim()
        };
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
