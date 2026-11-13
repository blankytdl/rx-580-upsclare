# RX 580 FSR4 Upscaler — Windows GFX803 port

A Windows-first research and development project for bringing the **FSR4 GFX803/Polaris shader optimizations** to the Radeon RX 580 without replacing the AMD graphics driver.

> **Status: Windows tooling preview.** This repository now includes a DirectX 12 / AMD ISA probe and a WPF Control Center with a generic x64 DLL attach feature. It still does **not** contain a working Windows FSR4 shader runtime or an FSR4-specific game injector. The generic DLL loader is not the FSR4 implementation.

## Goal

Run the FSR4 GFX803 optimizations on an RX 580 under native Windows, ideally with the same image quality and no measurable extra overhead compared with the optimized Linux implementation.

The first question to prove is whether AMD's Windows DirectX 12 compiler can emit the GCN4 instruction:

`v_mad_i32_i24`

for the expanded signed 8-bit multiply-accumulate sequence used by the Linux GFX803 layer.

The instruction is a GPU ISA operation, not a Linux feature. The Windows port must persuade the Windows shader compiler to generate it; HLSL or DXIL source alone cannot guarantee that.

## Control Center (Windows x64)

The WPF application lives in `app/Fsr4Gfx803.ControlCenter/`. GitHub Actions builds a self-contained Windows x64 executable and ZIP whenever code is pushed to `main`, and can also be run manually from **Actions → Build Windows Control Center**.

The ZIP includes:

- `FSR4-GFX803-ControlCenter.exe` — no separate .NET runtime installation required.
- `tools/windows/` — the RGA probe script and its HLSL source.

The Control Center can enumerate running processes, load a user-selected x64 DLL into a selected native x64 process, save/export upstream-style profiles, and run the GFX803 RGA compiler probe.

**Important:** DLL attachment is generic. It confirms only that Windows loaded the selected module. It does not add FSR4 hooks or shader optimization by itself. The profile editor preserves the upstream DLL variants, tiers, direct shader-set selection, autotune, profiling, debug, SPIR-V dump, SDot and cache settings, but these are saved as profile data only until the Windows runtime implements them. Use only trusted DLLs and offline/single-player games; no anti-cheat bypass is included.

## Why this approach

The Linux `fsr4-gfx803` project combines:

1. A Mesa/RADV compiler patch that lowers appropriate integer operations to `v_mad_i32_i24`.
2. A Vulkan layer that rewrites packed signed int8 dot products into a form the patched compiler can optimize.
3. FSR4 runtime/shader variants and an OptiScaler integration.

Windows does not use Mesa's ACO compiler. This project therefore starts by testing the **AMD DirectX 12 driver compiler** rather than pretending the Linux driver patch can be copied to Windows.

AMD's Radeon GPU Analyzer (RGA) has a DirectX 12 command-line mode that can compile a compute shader for a selected ASIC and produce GCN ISA disassembly. The probe in `tools/windows/` uses that facility.

## First milestone: verify the Windows compiler

1. Install a compatible AMD Adrenalin driver and AMD Radeon GPU Analyzer.
2. Run the probe script:

   ```powershell
   .\tools\windows\run-rga-gfx803-probe.ps1
   ```

3. Inspect the two output files:
   - `intrinsic.isa`: HLSL `dot4add_i8packed` path.
   - `expanded.isa`: explicit signed-byte extraction and multiply-accumulate path.
4. Check whether the expanded path contains `v_mad_i32_i24`, and compare its register usage and instruction count with the intrinsic path.

A result is only valid when RGA reports the requested ASIC as **GFX803**. If the installed RGA/driver combination does not support that target, the script records the failure rather than silently compiling for a different ASIC.

## Roadmap

- [x] Create a Windows port design and compiler probe.
- [x] Add the WPF Control Center and GitHub Actions x64 packaging.
- [x] Add a generic x64 DLL attach helper for testing a future Windows runtime.
- [ ] Run the DirectX 12 compiler probe on a real RX 580 / GFX803 Windows system.
- [ ] Determine whether the Windows compiler emits `v_mad_i32_i24` for the expanded path.
- [ ] If required, build and validate a DXIL transformation that preserves signed 8-bit dot-product semantics.
- [ ] Port the shader-rewrite, shader-set replacement and runtime profiling/tuning logic to a Windows renderer path.
- [ ] Integrate a renderer-aware per-game FSR4 bridge; generic LoadLibrary injection is not enough.
- [ ] Benchmark image quality, GPU time, full-frame time, and injection overhead against the Linux reference.
- [ ] Package a reversible Windows FSR4 runtime only after the shader and renderer paths are proven.

## Important limits

- **No performance guarantee yet.** No Windows RX 580 benchmark has been run by this repository.
- **No driver patch is included.** The initial design avoids replacing or patching the system AMD driver.
- **No Windows FSR4 runtime is included.** A game must expose a compatible temporal-upscaler interface and the Windows project must implement its shader/pipeline interception path.
- **DXIL `IMad` is not the same thing as GFX `v_mad_i32_i24`.** DXIL expresses 32-bit signed multiply-add semantics. The AMD backend may or may not lower a particular expression to the 24-bit GCN instruction.
- **RGA support varies by version and OS.** Recent RGA releases list Windows 11 as their supported Windows environment. The probe requires a DX12-capable RGA build, its DX12 compiler dependencies, and the AMD driver/compiler.

## References

- [Original FSR4 GFX803 implementation](https://github.com/Schaka/fsr4-gfx803)
- [Active GFX803 fork](https://github.com/Mrx123u/fsr4-gfx803)
- [AMD Radeon GPU Analyzer](https://github.com/GPUOpen-Tools/radeon_gpu_analyzer)
- [AMD RGA DirectX 12 compute workflow](https://gpuopen.com/news/radeon-gpu-analyzer-2-2-direct3d12-compute/)
- [DirectX Shader Compiler — DXIL specification](https://github.com/microsoft/DirectXShaderCompiler/blob/main/docs/DXIL.rst)
- [HLSL Shader Model 6.4 dot4add intrinsics](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/hlsl-shader-model-6-4-features-for-direct3d-12)

## Safety

Use this only with offline/single-player games while testing DLL or pipeline interception. Do not inject into anti-cheat-protected multiplayer games.
