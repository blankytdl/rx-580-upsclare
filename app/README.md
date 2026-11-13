# FSR4 GFX803 Control Center

A self-contained Windows x64 WPF app. GitHub Actions builds the EXE and portable ZIP; no separately installed .NET runtime is needed.

## Build

Run **Actions → Build Windows Control Center**, or push a change to `app/**`, `addons/**`, `tools/windows/**` or the workflow itself.

The build also compiles `addons/src/fsr4_gfx803_addon.cpp` against the ReShade add-on API and includes `addons/FSR4-GFX803.addon64` in the portable ZIP.

## Included UI

- **Overview:** GPU target, runtime status and feature overview.
- **Process attach:** generic x64 DLL loader for diagnostics only; it is not the FSR4 rendering hook.
- **Game bridge:** select a game executable, detect ReShade's add-on directory, install/remove the ReShade DXIL bridge, configure shader dumping and select an external DXIL override folder.
- **Optimization profile:** save/export upstream-style DLL variants, tiers, shader-set paths, autotuning and profiler settings. Options that the Windows bridge does not yet implement are saved as profile data only.
- **Diagnostics:** run AMD Radeon GPU Analyzer against the GFX803 HLSL probe and inspect output logs.

## Using the actual Windows rendering hook

1. Install ReShade into the target game using a build with **full add-on support**. The ReShade installer has a build selection for add-on support.
2. Open this app's **Game bridge** page and choose the game's EXE.
3. Confirm the detected ReShade add-on search path, optionally choose a folder of hash-named `.dxil` replacement shaders, and enable shader dumping.
4. Press **Install bridge for this game**.
5. Close the game completely and start it again. ReShade loads the add-on when the process starts, before FSR4 creates its compute pipelines.
6. Select **Open shader dump folder** after the game has run FSR4. The add-on writes FSR4 shader containers as `<32-hex-character-hash>.dxil`.
7. A shader pack is only used when a replacement has the exact original container hash.

To uninstall, use **Remove bridge / restore backups**. The installer stores a manifest and verifies file hashes before removing anything; modified files are preserved instead of being overwritten.

## What this does not do yet

The bridge is derived from the Windows ReShade DXIL replacement approach documented by [fsr4-shader-performance-overrides](https://github.com/zgauthier2000/fsr4-shader-performance-overrides/tree/main/windows). It is a native Windows D3D12 shader-pipeline interception mechanism.

It is **not yet a complete GFX803 port**. There is no RX 580-validated DXIL replacement pack, no DXIL signed-dot expansion for GFX803, and no proof yet that AMD's Windows driver emits `v_mad_i32_i24` for the transformed shaders. The bridge can dump input shaders and load replacements, but without a compatible replacement pack it does not make FSR4 faster.

The RDNA2/RDNA3 replacements from other projects should not be assumed to work or be optimal on GFX803. Do not use a replacement pack unless it is built for the exact FSR4 DLL version and has been validated on your GPU.

Use only trusted ReShade add-ons and offline/single-player games. Modified shader pipelines and add-ons may be disallowed by anti-cheat systems. This app does not attempt to bypass anti-cheat.

