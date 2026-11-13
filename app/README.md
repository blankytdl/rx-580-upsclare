# FSR4 GFX803 Control Center

A Windows x64 WPF control app built as a self-contained single-file executable by GitHub Actions.

## Build

The workflow .github/workflows/build-windows-control-center.yml runs on pushes to the app, diagnostic tools, and workflow, and can also be started manually from the Actions tab.

Download the FSR4-GFX803-ControlCenter-win-x64 artifact and extract the ZIP. Run:

**FSR4-GFX803-ControlCenter.exe**

The ZIP contains the EXE and tools/windows/ so the RGA probe button can find its PowerShell script. The application does not require a separately installed .NET runtime.

## Current functions

- Enumerate running Windows processes and display PID/window title.
- Load a user-selected x64 DLL into a selected native x64 process using Windows LoadLibraryW through a remote thread.
- Save and export a profile using the upstream names for DLL variants, tiers, shader-set selection, autotune, GPU profiling, debug output, SPIR-V dumping, SDot expansion, and cache directories.
- Run the existing AMD RGA GFX803 DX12 ISA probe and open its output/log folders.

DLL attachment is generic. Only use trusted DLLs and offline/single-player games. This app does not bypass anti-cheat, protected-process restrictions, or integrity checks. A successful attach means Windows loaded the module; it does not mean the module can hook the game's renderer.

## Important runtime limitation

This is **not yet a Windows FSR4 implementation**. The upstream fsr4-gfx803 engine requires a patched Mesa/RADV driver, a Vulkan layer which rewrites SPIR-V OpSDot operations and substitutes tuned shader sets, and a compatible FSR4 upscaler DLL. Its launcher configures these before game startup. A native Windows game does not use the Linux RADV compiler or its patched ACO backend.

The profile page preserves the upstream options for porting work, but currently they are saved as JSON only. They are not applied to a game by this build. The Windows shader runtime must be ported and validated before those options can affect frame rendering. Do not inject the original Linux .so layer into a Windows game.

## Profile options represented

- DLL variants: stock, bc250, hybrid
- Tiers: lossless, quality, balanced, speed, off, none
- Direct shader-set name override
- Shader autotuning
- Per-dispatch GPU profiling
- Shader replacement/debug logging
- SPIR-V module dumping
- Signed dot-product expansion
- Shader-set and cache paths

These options correspond to upstream concepts; the Windows bridge still needs to implement them.
