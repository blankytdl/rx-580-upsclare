using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fsr4Gfx803.ControlCenter.Services;

public sealed record BridgeInstallResult(string AddonDirectory, string ConfigPath, string OverrideDirectory, string Message);
public sealed record BridgeRemoveResult(bool RemovedAddon, bool RemovedConfig, bool RestoredAddonBackup, bool RestoredConfigBackup, string Message);

public static class ReshadeBridgeManager
{
    private const string AddonFileName = "FSR4-GFX803.addon64";
    private const string ConfigFileName = "FSR4-GFX803.ini";
    private const string ManifestFileName = ".fsr4-gfx803.install.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed class InstallManifest
    {
        public string GameExecutable { get; set; } = "";
        public string AddonDirectory { get; set; } = "";
        public string AddonPath { get; set; } = "";
        public string AddonSha256 { get; set; } = "";
        public string? AddonBackupPath { get; set; }
        public string ConfigPath { get; set; } = "";
        public string ConfigSha256 { get; set; } = "";
        public string? ConfigBackupPath { get; set; }
        public DateTime InstalledUtc { get; set; }
    }

    public static string GetSuggestedAddonDirectory(string gameExecutable)
    {
        if (string.IsNullOrWhiteSpace(gameExecutable))
            return "";
        string fullExe = Path.GetFullPath(gameExecutable);
        string gameDirectory = Path.GetDirectoryName(fullExe) ?? "";
        string reshadeIni = Path.Combine(gameDirectory, "ReShade.ini");
        string? configured = ReadIniValue(reshadeIni, "ADDON", "AddonPath");
        if (string.IsNullOrWhiteSpace(configured))
            return gameDirectory;
        string candidate = configured.Trim().Trim('"');
        return Path.GetFullPath(Path.IsPathRooted(candidate) ? candidate : Path.Combine(gameDirectory, candidate));
    }

    public static bool IsReshadePresent(string gameExecutable, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(gameExecutable) || !File.Exists(gameExecutable))
        {
            reason = "Select an existing game executable.";
            return false;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(gameExecutable))!;
        string ini = Path.Combine(directory, "ReShade.ini");
        string[] loaders = ["dxgi.dll", "d3d12.dll", "d3d11.dll", "d3d9.dll", "opengl32.dll", "dinput8.dll"];
        bool hasLoader = loaders.Any(name => File.Exists(Path.Combine(directory, name)));
        bool hasIni = File.Exists(ini);

        if (!hasLoader && !hasIni)
        {
            reason = "ReShade was not detected beside this game's executable. Install ReShade with full add-on support first.";
            return false;
        }
        if (!hasIni)
        {
            reason = "A possible ReShade loader was found, but ReShade.ini is missing. Confirm the ReShade installation and add-on search path.";
            return false;
        }
        if (!hasLoader)
        {
            reason = "ReShade.ini was found, but no common ReShade proxy DLL was detected beside the selected executable. The game may use a different proxy location.";
            return false;
        }

        reason = "ReShade files detected. Full add-on support still needs to be selected in the ReShade installer.";
        return true;
    }

    public static BridgeInstallResult Install(
        string gameExecutable,
        string addonDirectory,
        string overrideDirectory,
        bool dumpShaders,
        bool verboseLog)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The ReShade bridge installer works only on Windows.");
        if (string.IsNullOrWhiteSpace(gameExecutable) || !File.Exists(gameExecutable))
            throw new FileNotFoundException("Choose an existing game executable.", gameExecutable);

        if (!IsReshadePresent(gameExecutable, out string detection))
            throw new InvalidOperationException(detection);

        string addonSource = Path.Combine(AppContext.BaseDirectory, "addons", AddonFileName);
        if (!File.Exists(addonSource))
            throw new FileNotFoundException("The packaged ReShade add-on was not found. Extract the complete portable ZIP, including its addons folder.", addonSource);

        string targetDirectory = string.IsNullOrWhiteSpace(addonDirectory)
            ? GetSuggestedAddonDirectory(gameExecutable)
            : Path.GetFullPath(addonDirectory.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(targetDirectory))
            throw new InvalidOperationException("Could not resolve the ReShade add-on search directory.");

        Directory.CreateDirectory(targetDirectory);

        string addonTarget = Path.Combine(targetDirectory, AddonFileName);
        string configTarget = Path.Combine(targetDirectory, ConfigFileName);
        string manifestTarget = Path.Combine(targetDirectory, ManifestFileName);
        if (File.Exists(manifestTarget))
            throw new InvalidOperationException("This add-on directory already has an FSR4 GFX803 install manifest. Use Remove / restore before reinstalling.");

        string? addonBackup = BackupIfExists(addonTarget);
        string? configBackup = BackupIfExists(configTarget);

        string resolvedOverrideDirectory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(targetDirectory, "fsr4-overrides")
            : Path.GetFullPath(overrideDirectory.Trim().Trim('"'));
        Directory.CreateDirectory(resolvedOverrideDirectory);

        string dumpDirectory = Path.Combine(targetDirectory, "fsr4-overrides", "dump");
        string configText = string.Join(Environment.NewLine,
        [
            "; FSR4 GFX803 ReShade bridge configuration",
            "; ReShade must be installed with full add-on support. Restart the game after changing these options.",
            "[FSR4GFX803]",
            "Enabled=1",
            $"DumpShaders={(dumpShaders ? 1 : 0)}",
            $"VerboseLog={(verboseLog ? 1 : 0)}",
            "OnlyFsr4Shaders=1",
            $"OverrideDirectory={resolvedOverrideDirectory}",
            $"DumpDirectory={dumpDirectory}",
            ""
        ]);

        try
        {
            File.Copy(addonSource, addonTarget, true);
            File.WriteAllText(configTarget, configText, new UTF8Encoding(false));

            var manifest = new InstallManifest
            {
                GameExecutable = Path.GetFullPath(gameExecutable),
                AddonDirectory = targetDirectory,
                AddonPath = addonTarget,
                AddonSha256 = Sha256(addonTarget),
                AddonBackupPath = addonBackup,
                ConfigPath = configTarget,
                ConfigSha256 = Sha256(configTarget),
                ConfigBackupPath = configBackup,
                InstalledUtc = DateTime.UtcNow
            };
            File.WriteAllText(manifestTarget, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
        }
        catch
        {
            TryDelete(addonTarget);
            TryDelete(configTarget);
            RestoreBackup(addonTarget, addonBackup);
            RestoreBackup(configTarget, configBackup);
            throw;
        }

        return new BridgeInstallResult(
            targetDirectory,
            configTarget,
            resolvedOverrideDirectory,
            $"Installed the ReShade add-on. {detection} Restart the game for pipeline interception to start.");
    }

    public static BridgeRemoveResult RemoveAndRestore(string addonDirectory)
    {
        if (string.IsNullOrWhiteSpace(addonDirectory))
            throw new InvalidOperationException("Choose the ReShade add-on directory first.");

        string directory = Path.GetFullPath(addonDirectory.Trim().Trim('"'));
        string manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("No FSR4 GFX803 install manifest was found in this add-on directory.", manifestPath);

        InstallManifest manifest = JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidDataException("The FSR4 GFX803 install manifest is empty or invalid.");

        bool removedAddon = RemoveOnlyIfUnchanged(manifest.AddonPath, manifest.AddonSha256);
        bool restoredAddon = removedAddon && RestoreBackup(manifest.AddonPath, manifest.AddonBackupPath);
        bool removedConfig = RemoveOnlyIfUnchanged(manifest.ConfigPath, manifest.ConfigSha256);
        bool restoredConfig = removedConfig && RestoreBackup(manifest.ConfigPath, manifest.ConfigBackupPath);

        // Keep the manifest when files were changed after installation, so the user can inspect
        // the conflict and retry after resolving it rather than silently discarding the record.
        if (removedAddon && removedConfig)
            File.Delete(manifestPath);

        string message = removedAddon && removedConfig
            ? "The bridge add-on and its configuration were removed; previous files, when present, were restored."
            : "One or more installed files were changed after setup. Those files were preserved; check the install manifest before manual cleanup.";

        return new BridgeRemoveResult(removedAddon, removedConfig, restoredAddon, restoredConfig, message);
    }

    private static string? ReadIniValue(string path, string section, string key)
    {
        if (!File.Exists(path))
            return null;
        string currentSection = "";
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }
            if (!string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                continue;
            int pos = line.IndexOf('=');
            if (pos > 0 && string.Equals(line[..pos].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return line[(pos + 1)..].Trim();
        }
        return null;
    }

    private static string? BackupIfExists(string path)
    {
        if (!File.Exists(path))
            return null;
        string backup = path + ".fsr4-gfx803-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        File.Copy(path, backup, false);
        return backup;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool RemoveOnlyIfUnchanged(string path, string expectedHash)
    {
        if (!File.Exists(path))
            return true;
        if (!string.Equals(Sha256(path), expectedHash, StringComparison.OrdinalIgnoreCase))
            return false;
        File.Delete(path);
        return true;
    }

    private static bool RestoreBackup(string target, string? backup)
    {
        if (string.IsNullOrWhiteSpace(backup) || !File.Exists(backup))
            return false;
        if (File.Exists(target))
            return false;
        File.Move(backup, target);
        return true;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
