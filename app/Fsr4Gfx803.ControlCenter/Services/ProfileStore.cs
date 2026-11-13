using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fsr4Gfx803.ControlCenter.Models;

namespace Fsr4Gfx803.ControlCenter.Services;

public static class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FSR4GFX803");

    public static string ProfilePath => Path.Combine(DataDirectory, "profile.json");
    public static string LogPath => Path.Combine(DataDirectory, "control-center.log");

    public static Fsr4Profile Load()
    {
        try
        {
            if (!File.Exists(ProfilePath))
                return new Fsr4Profile();
            return JsonSerializer.Deserialize<Fsr4Profile>(File.ReadAllText(ProfilePath), Options) ?? new Fsr4Profile();
        }
        catch
        {
            return new Fsr4Profile();
        }
    }

    public static void Save(Fsr4Profile profile)
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(ProfilePath, JsonSerializer.Serialize(profile, Options));
    }
}
