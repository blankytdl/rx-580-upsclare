# RX 580 FSR4 Upscaler — Windows GFX803 port

A Windows-first research and development project for bringing the FSR4 GFX803/Polaris shader optimizations to the Radeon RX 580.

> **Status: Windows bridge + compiler research preview.** The repository now builds a Windows Control Center and a ReShade D3D12 add-on. The add-on can be installed per game, dump FSR4 compute shaders, and load user-provided hash-matched DXIL replacements before pipelines are created. **A validated GFX803-specific DXIL replacement pack is not included yet**, so this is not yet equivalent to the full Linux GFX803 implementation.

## Windows Control Center

The WPF app lives in `app/Fsr4Gfx803.ControlCenter/`. GitHub Actions builds a self-contained x64 EXE and portable ZIP.

- Overview and diagnostics, including the AMD RGA GFX803 ISA probe.
- Running-process inspection and a generic x64 DLL loader for diagnostics (not the FSR4 rendering path).
- Per-game ReShade bridge installation and removal with backup/restore.
- Per-game configuration for FSR4-only DXIL shader dumping, verbosity and an external DXIL override folder.
- Profile editor for the upstream DLL variants, tiers, shader set path, autotune and profiling options. Options not implemented by the Windows runtime are saved as profile data only.

See [app/README.md](app/README.md) and [addons/README.md](addons/README.md).

## What the Windows bridge actually does

The bridge is a ReShade add-on built as `FSR4-GFX803.addon64`. ReShade must be installed in the game with **full add-on support**. The Control Center copies the add-on into ReShade's configured add-on search directory and writes `FSR4-GFX803.ini`.

When the game starts, the add-on listens to ReShade's D3D12 `create_pipeline` event. For FSR4 model compute shaders it:

1. Reads the 16-byte DXBC container hash from the shader header.
2. Optionally dumps the incoming FSR4 shader as `<hash>.dxil`.
3. Loads a replacement with the same hash from the configured override directory, if one exists.
4. Leaves unmatched shaders untouched.

This is a renderer pipeline interception route, not a late process attach. Close and restart the game after enabling or changing it. It does not bypass anti-cheat and should only be used with trusted add-ons in offline/single-player games.

The bridge is based on the Windows ReShade add-on approach in [zgauthier2000/fsr4-shader-performance-overrides](https://github.com/zgauthier2000/fsr4-shader-performance-overrides). That project has demonstrated DXIL shader replacement on Windows, but its documented tests are on newer Radeon GPUs. Its RDNA-specific shaders must **not** be assumed to optimize GFX803.

## Original Linux GFX803 implementation

The upstream Linux project combines:

1. A patched Mesa/RADV compiler that lowers suitable signed-int8 multiply-accumulates to `v_mad_i32_i24`.
2. A Vulkan layer that expands packed `OpSDot` operations and replaces FSR4 network shaders with tuned SPIR-V sets.
3. Compatible FSR4 upscaler DLL variants and an OptiScaler integration.

Native Windows uses AMD's proprietary D3D12 driver compiler, not Mesa/ACO. The Windows port must transform the DXIL before pipeline creation and prove that the AMD Windows compiler emits GFX803-friendly ISA.

## Compiler probe

The repository includes HLSL probes and `tools/windows/run-rga-gfx803-probe.ps1`. Install a compatible AMD Adrenalin driver and AMD Radeon GPU Analyzer, then run:

```powershell
.\tools\windows\run-rga-gfx803-probe.ps1 -RgaPath "C:\AMD\RGA\rga.exe"
```

Check `expanded.isa` for `v_mad_i32_i24`. A result only counts when RGA confirms `gfx803`.

## Roadmap

- [x] Windows WPF Control Center with GitHub Actions self-contained build.
- [x] ReShade D3D12 pipeline interception add-on.
- [x] Per-game bridge installation, shader dumping and reversible removal.
- [x] DXIL override loading by original FSR4 shader hash.
- [ ] Run the RGA probe on a real RX 580 / GFX803 Windows machine.
- [ ] Build a semantics-preserving DXIL transformation for signed packed-int8 dot products.
- [ ] Prove AMD's Windows driver compiles the rewritten form to `v_mad_i32_i24` (or another equally efficient GFX803 sequence).
- [ ] Produce GFX803-tuned DXIL replacements for the FSR4 network passes and validate their output.
- [ ] Add actual GFX803 quality/speed tiers, shader autotuning and GPU-time profiling to the Windows bridge.
- [ ] Benchmark on RX 580 and compare image quality, upscaler time, frametime and bridge overhead.

## Important limitations

- No GFX803-validated DXIL shader pack is currently bundled.
- Generic DLL attachment does not accelerate FSR4.
- The existing `stock`, `bc250`, `hybrid`, `lossless`, `quality`, `balanced` and `speed` profile names are stored in JSON but are not yet connected to GFX803 DXIL shader variants.
- The Linux RADV patch and SPIR-V layer cannot be loaded into a native Windows game.
- No Windows RX 580 performance claims are made until the shader transformation and output are validated.

## References

- [Original FSR4 GFX803 implementation](https://github.com/Schaka/fsr4-gfx803)
- [Active GFX803 fork](https://github.com/Mrx123u/fsr4-gfx803)
- [Windows DXIL/ReShade shader replacement project](https://github.com/zgauthier2000/fsr4-shader-performance-overrides/tree/main/windows)
- [ReShade](https://github.com/crosire/reshade)
- [AMD Radeon GPU Analyzer](https://github.com/GPUOpen-Tools/radeon_gpu_analyzer)
- [DXIL specification](https://github.com/microsoft/DirectXShaderCompiler/blob/main/docs/DXIL.rst)

