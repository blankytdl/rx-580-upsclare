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
    public bool GpuProfiling { get; set; }
    public bool ShaderDebugLog { get; set; }
    public bool DumpSpirv { get; set; }
    public bool ExpandSignedDot { get; set; } = true;
    public string ShaderSetsDirectory { get; set; } = "";
    public string ShaderCacheDirectory { get; set; } = "";
}
