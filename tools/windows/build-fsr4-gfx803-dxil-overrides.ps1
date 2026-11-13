#requires -Version 5.1
<#
.SYNOPSIS
    Expands signed packed-int8 DXIL dot products in dumped FSR4 shaders and validates the output.

.DESCRIPTION
    This is a research pipeline, not a finished GFX803 shader pack. It dumps DXIL as LLVM IR,
    expands each dx.op.dot4AddPacked.i32 operation into four signed-byte multiplies and adds,
    then asks Microsoft's DXC assembler/validator to produce a signed DXIL container.

    The output keeps the original shader container hash in its filename so the ReShade add-on
    can match it at pipeline creation time. Whether the AMD Windows driver emits v_mad_i32_i24
    still needs to be checked with Radeon GPU Analyzer on GFX803.

.PARAMETER InputDirectory
    Folder containing FSR4 shader dumps named <32-hex-hash>.dxil.

.PARAMETER OutputDirectory
    Folder to write transformed hash-named DXIL replacements.

.PARAMETER DxcPath
    Path to dxc.exe from Microsoft's DirectXShaderCompiler release.

.PARAMETER DxCompilerPath
    Path to dxcompiler.dll from the same DXC release.

.PARAMETER AssemblerPath
    Path to dxilasm-win.exe built by this repository.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$InputDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$DxcPath,
    [Parameter(Mandatory=$true)][string]$DxCompilerPath,
    [Parameter(Mandatory=$true)][string]$AssemblerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

foreach ($path in @($InputDirectory, $DxcPath, $DxCompilerPath, $AssemblerPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required path not found: $path" }
}
$InputDirectory = (Resolve-Path -LiteralPath $InputDirectory).Path
$DxcPath = (Resolve-Path -LiteralPath $DxcPath).Path
$DxCompilerPath = (Resolve-Path -LiteralPath $DxCompilerPath).Path
$AssemblerPath = (Resolve-Path -LiteralPath $AssemblerPath).Path
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$transformer = Join-Path $PSScriptRoot "expand_dot4_gfx803.py"
if (-not (Test-Path -LiteralPath $transformer)) { throw "DXIL transformer not found: $transformer" }
$python = Get-Command "python.exe" -ErrorAction SilentlyContinue
if ($null -eq $python) { throw "python.exe is required to run the DXIL IR transformer." }

$files = Get-ChildItem -LiteralPath $InputDirectory -Filter "*.dxil" -File
if ($files.Count -eq 0) { throw "No .dxil shader dumps found in $InputDirectory" }

$built = 0
$skipped = 0
$failed = 0
$temp = Join-Path $OutputDirectory ".work"
New-Item -ItemType Directory -Force -Path $temp | Out-Null

foreach ($file in $files) {
    if ($file.BaseName -notmatch '^[0-9a-fA-F]{32}$') {
        Write-Warning "Skipping non-hash shader filename: $($file.Name)"
        $skipped++
        continue
    }

    $llIn = Join-Path $temp ($file.BaseName + ".input.ll")
    $llOut = Join-Path $temp ($file.BaseName + ".expanded.ll")
    $dxilOut = Join-Path $OutputDirectory ($file.BaseName.ToLowerInvariant() + ".dxil")

    try {
        $dump = & $DxcPath -dumpbin $file.FullName 2>&1
        $dumpExit = $LASTEXITCODE
        $dumpText = ($dump | ForEach-Object { "$_" }) -join [Environment]::NewLine
        if ($dumpExit -ne 0 -or [string]::IsNullOrWhiteSpace($dumpText)) {
            throw "dxc -dumpbin failed (exit $dumpExit)."
        }
        [System.IO.File]::WriteAllText($llIn, $dumpText, [System.Text.UTF8Encoding]::new($false))

        & $python.Source $transformer $llIn $llOut
        $transformExit = $LASTEXITCODE
        if ($transformExit -eq 2) {
            Write-Host "No packed int8 dot products: $($file.Name)"
            $skipped++
            continue
        }
        if ($transformExit -ne 0) { throw "DXIL dot4 expansion failed (exit $transformExit)." }

        & $AssemblerPath $DxCompilerPath $llOut $dxilOut
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dxilOut)) {
            throw "DXIL assembly/validation failed."
        }

        # Confirm that the output remains a readable DXIL container.
        $verify = & $DxcPath -dumpbin $dxilOut 2>&1
        if ($LASTEXITCODE -ne 0) { throw "DXC could not read back the assembled output." }

        Write-Host "Built GFX803 dot4-expansion replacement: $($file.BaseName).dxil" -ForegroundColor Green
        $built++
    }
    catch {
        Write-Warning "$($file.Name): $($_.Exception.Message)"
        Remove-Item -LiteralPath $dxilOut -Force -ErrorAction SilentlyContinue
        $failed++
    }
}

Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ""
Write-Host "Summary: built=$built skipped=$skipped failed=$failed"
if ($failed -gt 0) {
    throw "$failed shader(s) failed transformation or DXIL validation. No incomplete file was retained."
}
if ($built -eq 0) {
    throw "No shader replacements were built. Confirm these dumps contain dx.op.dot4AddPacked.i32 calls."
}
