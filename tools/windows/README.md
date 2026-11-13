## ReShade full add-on setup helper

The portable ZIP includes `install-reshade-full-addon.ps1`. The Control Center's **Game bridge** page runs it when you click **Download ReShade 6.8.0 (full add-on support)**. It downloads the version-pinned installer from the official ReShade URL and launches it. ReShade's own website asks users not to redistribute its binaries, so the installer is deliberately fetched from the publisher at setup time.

Manual use:

```powershell
.\tools\windows\install-reshade-full-addon.ps1
```

This step requires an internet connection. The DXC compiler, validator and DXIL tools required by the bridge are bundled in the ZIP; ReShade is the only required external runtime downloaded on demand. AMD Radeon GPU Analyzer remains optional for the ISA diagnostic probe.

# Windows tooling

## RGA GFX803 compiler probe

Run from PowerShell:

```powershell
.\run-rga-gfx803-probe.ps1
```

Or pass an explicit RGA executable:

```powershell
.\run-rga-gfx803-probe.ps1 -RgaPath "C:\AMD\RGA\rga.exe"
```

The script compiles two compute entry points for `gfx803`:

- `IntrinsicCS`: HLSL `dot4add_i8packed` (Shader Model 6.4).
- `ExpandedCS`: four signed-byte multiply-accumulate operations.

Outputs are written to `artifacts\rga-gfx803\` by default. The script does not modify the game, graphics driver, registry, or system files.

### Requirements

- Windows with an AMD DirectX 12 driver/compiler.
- Radeon GPU Analyzer with the DirectX 12 CLI backend (`rga.exe -s dx12`).
- An RGA version that supports the `--autogen-dir` option.
- The `gfx803` target must be recognized by the installed RGA/driver combination.

AMD's current RGA release notes list Windows 11 as the supported Windows platform. Older RGA versions may have different OS requirements. If RGA cannot target GFX803 or is unavailable on the installed Windows version, the script will report the error; it will not fall back to another GPU architecture.

### Reading the output

Open `intrinsic.isa` and `expanded.isa`.

The target result of interest is:

```asm
v_mad_i32_i24
```

Look at whether it appears in the expanded path, how many times it appears, and whether the compiler instead emits separate integer multiply/add instructions.

A missing match is not automatically a failed port: inspect the complete ISA and the RGA log first. The driver may choose a different valid instruction sequence or the target may not be supported by that compiler build.

### Important

RGA's DirectX 12 mode uses AMD's driver/compiler path and needs a compatible Adrenalin installation. A result from a different ASIC, an offline shader disassembly, or DXIL text alone is not a GFX803 Windows-driver result.
