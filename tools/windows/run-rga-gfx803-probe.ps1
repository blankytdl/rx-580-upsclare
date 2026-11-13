#requires -Version 5.1
<#
.SYNOPSIS
    Compiles two HLSL int8 dot-product variants for AMD GFX803 and saves ISA.

.DESCRIPTION
    Uses Radeon GPU Analyzer's DirectX 12 mode:
      - IntrinsicCS: HLSL dot4add_i8packed
      - ExpandedCS: explicit signed-byte extraction and multiply-add chain

    The requested target is gfx803 (Polaris / RX 580). The script fails visibly
    if RGA cannot compile for that target; it never substitutes another ASIC.

.PARAMETER RgaPath
    Optional full path to rga.exe. If omitted, rga.exe must be on PATH.

.PARAMETER OutputDirectory
    Output directory for ISA, RGA logs, generated pipeline files and summary.

.EXAMPLE
    .\run-rga-gfx803-probe.ps1

.EXAMPLE
    .\run-rga-gfx803-probe.ps1 -RgaPath 'C:\AMD\RGA\rga.exe'
#>
[CmdletBinding()]
param(
    [string]$RgaPath,
    [string]$OutputDirectory = (Join-Path (Get-Location) "artifacts\rga-gfx803")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$shaderPath = Join-Path $scriptRoot "shaders\gfx803-imad-probe.hlsl"

if (-not (Test-Path -LiteralPath $shaderPath -PathType Leaf)) {
    throw "Probe shader not found: $shaderPath"
}

if ([string]::IsNullOrWhiteSpace($RgaPath)) {
    $rgaCommand = Get-Command "rga.exe" -ErrorAction SilentlyContinue
    if ($null -eq $rgaCommand) {
        throw "rga.exe was not found. Install AMD Radeon GPU Analyzer and either add its folder to PATH or pass -RgaPath."
    }
    $RgaPath = $rgaCommand.Source
}

if (-not (Test-Path -LiteralPath $RgaPath -PathType Leaf)) {
    throw "RGA executable not found: $RgaPath"
}

$RgaPath = (Resolve-Path -LiteralPath $RgaPath).Path
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$autogenDirectory = Join-Path $OutputDirectory "autogen"
New-Item -ItemType Directory -Path $autogenDirectory -Force | Out-Null

Write-Host "RGA:       $RgaPath"
Write-Host "Shader:    $shaderPath"
Write-Host "Target:    gfx803 (Polaris / RX 580)"
Write-Host "Output:    $OutputDirectory"
Write-Host ""
Write-Host "The DX12 RGA backend requires a compatible AMD driver/compiler."
Write-Host "This is a compile/ISA probe; it does not run FSR4 or modify a game."
Write-Host ""

$cases = @(
    @{ Name = "intrinsic"; Entry = "IntrinsicCS" },
    @{ Name = "expanded"; Entry = "ExpandedCS" }
)

$results = @()
$hadFailure = $false

foreach ($case in $cases) {
    $isaPath = Join-Path $OutputDirectory ($case.Name + ".isa")
    $logPath = Join-Path $OutputDirectory ($case.Name + ".rga.log")

    Write-Host "=== Compiling $($case.Entry) for gfx803 ===" -ForegroundColor Cyan

    $rgaArguments = @(
        "-s", "dx12",
        "-c", "gfx803",
        "--cs", $shaderPath,
        "--cs-entry", $case.Entry,
        "--cs-model", "cs_6_4",
        "--autogen-dir", $autogenDirectory,
        "--isa", $isaPath
    )

    $rgaOutput = & $RgaPath @rgaArguments 2>&1
    $exitCode = $LASTEXITCODE
    $rgaOutput | Tee-Object -FilePath $logPath

    if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $isaPath -PathType Leaf)) {
        Write-Warning "RGA failed for $($case.Entry). See $logPath"
        $results += [pscustomobject]@{
            Variant = $case.Name
            Entry = $case.Entry
            Compiled = $false
            HasVMadI32I24 = $false
            IsaFile = $isaPath
            LogFile = $logPath
        }
        $hadFailure = $true
        continue
    }

    $hasMad = Select-String -LiteralPath $isaPath -Pattern '(?i)\bv_mad_i32_i24\b' -Quiet
    $hasMul = Select-String -LiteralPath $isaPath -Pattern '(?i)\bv_mul_lo_i32\b' -Quiet
    $hasAdd = Select-String -LiteralPath $isaPath -Pattern '(?i)\bv_add_[ui]32\b' -Quiet

    if ($hasMad) {
        Write-Host "Found v_mad_i32_i24 in $($case.Name).isa" -ForegroundColor Green
    }
    else {
        Write-Warning "No v_mad_i32_i24 match found in $($case.Name).isa. Inspect the complete ISA; compiler output may use a different sequence."
    }

    $results += [pscustomobject]@{
        Variant = $case.Name
        Entry = $case.Entry
        Compiled = $true
        HasVMadI32I24 = [bool]$hasMad
        HasVMulLoI32 = [bool]$hasMul
        HasVAddI32 = [bool]$hasAdd
        IsaFile = $isaPath
        LogFile = $logPath
    }
}

$summaryPath = Join-Path $OutputDirectory "summary.json"
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
$results | Format-Table Variant, Compiled, HasVMadI32I24, HasVMulLoI32, HasVAddI32 -AutoSize
Write-Host "Summary: $summaryPath"

if ($hadFailure) {
    throw "One or more RGA compile jobs failed. Check the per-variant .rga.log files. No other ASIC was substituted."
}

$expanded = $results | Where-Object { $_.Variant -eq "expanded" }
if ($expanded.HasVMadI32I24) {
    Write-Host "PASS: expanded integer multiply-add compiled to v_mad_i32_i24." -ForegroundColor Green
}
else {
    Write-Warning "INCONCLUSIVE: the expanded path did not show v_mad_i32_i24. This does not yet prove a port is impossible; inspect the ISA and compare alternative source forms."
}
