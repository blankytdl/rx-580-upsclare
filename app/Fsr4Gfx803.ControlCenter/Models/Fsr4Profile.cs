namespace Fsr4Gfx803.ControlCenter.Models;

public sealed class Fsr4Profile
{
    public string Name { get; set; } = "RX 580 - Balanced";
    public string GameExecutable { get; set; } = "";
    public string RuntimeDll { get; set; } = "";
    public string UpscalerDll { get; set; } = "hybrid";
    public string Tier { get; set; } = "balanced";
    public string CustomShaderSet { get; set; } = "";
    public bool AutoTune { get; set; }
    public int AutoTuneSamples { get; set; } = 60;
    public bool GpuProfiling { get; set; }
    public int ProfileMinimumModuleBytes { get; set; } = 40000;
    public int ProfileIntervalSeconds { get; set; } = 5;
    public bool ProfileEachShader { get; set; }
    public bool ShaderDebugLog { get; set; }
    public bool DumpSpirv { get; set; }
    public bool ExpandSignedDot { get; set; } = true;
    public string ShaderSetsDirectory { get; set; } = "";
    public string VariantsDirectory { get; set; } = "";
    public string ShaderCacheDirectory { get; set; } = "";
    public string DumpDirectory { get; set; } = "";
    public string TuneCacheFile { get; set; } = "";
}
