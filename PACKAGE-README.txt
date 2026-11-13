FSR4 GFX803 Control Center - portable package

Included:
- Self-contained Windows x64 Control Center (no separate .NET runtime)
- FSR4-GFX803 ReShade D3D12 add-on
- DXC assembler/compiler and DXIL validator DLLs
- Native DXIL rewrite and assembly helpers
- PowerShell setup and diagnostics scripts

First setup:
1. Extract this ZIP.
2. Run FSR4-GFX803-ControlCenter.exe.
3. Open Game bridge and click "Download ReShade 6.8.0 (full add-on support)".
4. The helper downloads the official ReShade installer from reshade.me and launches it.
5. Select the game executable and DirectX 12 in ReShade setup, then return to the Control Center and install the bridge.

ReShade itself is not included because the official ReShade website says not to redistribute its binaries.
AMD Radeon GPU Analyzer is optional and is only needed for the separate ISA diagnostic probe.
The DXIL rewrite is experimental; this package does not include an RX 580-validated shader pack.
