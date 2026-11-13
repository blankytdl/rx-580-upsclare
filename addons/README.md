# ReShade DXIL bridge add-on

This folder contains the native Windows rendering hook used by the Control Center.

## What it does

`FSR4-GFX803.addon64` is a ReShade add-on. ReShade loads it when the game starts. It registers for `create_pipeline` and inspects D3D12 compute shader bytecode before the driver creates a pipeline.

For FSR4 model shaders, the add-on:

- Computes the original DXBC container hash (16 bytes at offset 4).
- Optionally dumps the incoming DXIL container to the configured dump folder using that hash as its filename.
- Looks for `<hash>.dxil` in the configured replacement directory.
- Uses a matching replacement only after basic DXBC-container validation.
- Leaves unknown and unmatched shaders unchanged.

## Requirements

- A D3D12 game using an FSR4-compatible AMD upscaler DLL.
- ReShade installed in that game with **full add-on support**.
- A 64-bit game and the `.addon64` bridge.
- Optional shader replacement files built for the exact FSR4 DLL version and GPU.

This is an add-on loaded by ReShade at process startup. It does not attach to a game after it has already started. Restart the game after installing, removing, or changing the add-on configuration.

## Install through the Control Center

Open **Game bridge**, select the game's executable, confirm the detected ReShade add-on folder, and press **Install bridge for this game**. The app creates:

- `FSR4-GFX803.addon64`
- `FSR4-GFX803.ini`
- `fsr4-overrides/dump/` when shader dumping is enabled
- `.fsr4-gfx803.install.json` to track owned files and backup restoration

Use **Remove bridge / restore backups** to reverse the installation. The manager verifies SHA-256 hashes before removing files, so files modified after installation are preserved.

## Manual configuration

The config file is read from the same directory as the add-on:

```ini
[FSR4GFX803]
Enabled=1
DumpShaders=1
VerboseLog=0
OnlyFsr4Shaders=1
OverrideDirectory=fsr4-overrides
DumpDirectory=fsr4-overrides/dump
```

Paths may be relative to the add-on directory or absolute. Dump files are named `<32-character-container-hash>.dxil`. Put a replacement with the same filename in the override directory to replace that shader.

## Current GFX803 limitation

This bridge implements the Windows interception and shader dump/load infrastructure. It does **not** yet implement the Linux RADV `OpSDot` rewrite or the GFX803 shader optimization sets. No GFX803-validated Windows DXIL pack is bundled. The current bridge is therefore a foundation for capturing and replacing shaders, not a claim that FSR4 is already accelerated on an RX 580.

Do not use RDNA2/RDNA3 DXIL packs as if they were GFX803-optimized. Validate the exact FSR4 library version, shader output and GPU timings before treating any replacement as safe.

## Build and license

GitHub Actions checks out ReShade at a pinned revision and builds this source as a static-CRT x64 `.addon64` using MSVC. ReShade is not bundled with the application.

The add-on source is GPL-2.0-or-later and derived from the Windows add-on in `zgauthier2000/fsr4-shader-performance-overrides`. See [LICENSE](LICENSE) and the repository's [third-party notices](../THIRD_PARTY_NOTICES.md).
