# Windows port architecture

## 1. Current Linux implementation

The reference implementation at [Mrx123u/fsr4-gfx803](https://github.com/Mrx123u/fsr4-gfx803) uses three coordinated pieces:

- **RADV/ACO patch:** the compiler lowers an expanded signed 8-bit dot product into GFX803-friendly integer multiply-add instructions.
- **Vulkan layer:** rewrites packed `OpSDot` operations into a sequence that the patched compiler recognizes, and swaps selected optimized shaders.
- **FSR4 runtime integration:** uses OptiScaler and a compatible FSR4 upscaler DLL under Proton.

The Windows driver is not Mesa/ACO. Reusing the Linux RADV patch verbatim is therefore not a Windows solution.

## 2. Windows research route

The first target is native D3D12:

```text
FSR4 DXIL shader
      |
      v
DXIL / HLSL transformation
      |
      v
AMD DirectX 12 shader compiler
      |
      v
GFX803 ISA (inspect with Radeon GPU Analyzer)
      |
      v
RX 580
```

AMD Radeon GPU Analyzer's DX12 CLI can compile a compute shader for a selected ASIC and emit GCN ISA. The probe uses `-s dx12 -c gfx803` and stores one disassembly for each HLSL entry point.

This is the key test: determine whether the AMD Windows backend recognizes the expanded operations and emits `v_mad_i32_i24`. If it does, the Windows port may be able to keep the driver's normal compilation pipeline and only transform shader IR before pipeline creation.

## 3. Important semantic distinction

HLSL Shader Model 6.4 exposes:

```hlsl
int dot4add_i8packed(uint a, uint b, int accumulator);
```

DXIL represents the signed packed-dot operation as a dedicated operation. DXIL also defines signed integer multiply-add (`IMad(a,b,c) = a*b+c`). Neither one guarantees a specific AMD ISA instruction.

The Linux GFX803 path expands the packed dot product into sign-extended byte values and multiply-adds. Each byte operand fits in signed 8-bit range, which is safely within the signed 24-bit multiplier input range of `v_mad_i32_i24`. Whether the Windows compiler performs the same lowering is an empirical question.

The probe compiles both forms and saves the actual backend disassembly. Do not infer success from DXIL text alone.

## 4. Why not start with VKD3D-Proton?

VKD3D-Proton is valuable in the Linux reference path because it converts D3D12/DXIL workloads to Vulkan/SPIR-V. Its Windows-native DLL build is primarily a development/testing path, not a drop-in route for arbitrary Windows games.

Using it as the primary Windows runtime would add a D3D12-to-Vulkan translation layer and still require a Windows Vulkan backend. For a native Windows port, intercepting or transforming the D3D12 shader/pipeline path is the more direct design to investigate first.

A VKD3D-Proton Windows build can still be useful as a separate experiment if the native D3D12 route proves blocked.

## 5. Later runtime integration

Only after the ISA probe succeeds should the project choose an integration point:

1. Identify how the target game exposes its FSR 3.1 / FSR 4 compatible inputs.
2. Prototype a D3D12 pipeline interception layer that can inspect the FSR4 DXIL before it is compiled.
3. Transform only the packed signed-int8 dot products that match the proven rewrite.
4. Preserve all resources, root signatures, dispatch dimensions, synchronization and shader semantics.
5. Compare outputs against the untouched AMD FSR4 runtime.
6. Measure GPU time and full frame time before and after injection.

A generic shader replacement is not sufficient: FSR4 uses temporal inputs (motion vectors, exposure, jitter and masks) which must remain intact.

## 6. Acceptance criteria

The Windows port is not considered complete until all of the following are demonstrated on an RX 580:

- RGA reports GFX803 and the expanded probe emits the expected ISA.
- The transformed FSR4 shaders compile through the shipping AMD Windows driver.
- No CPU fallback or readback is introduced.
- FSR4's output is validated against the reference path.
- GPU and full-frame timings are measured using repeatable scenes.
- Installation is per-game, reversible, and never overwrites the global AMD driver.
