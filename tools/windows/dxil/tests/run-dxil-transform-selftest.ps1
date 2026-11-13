#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$DxcPath,
    [Parameter(Mandatory=$true)][string]$DxCompilerPath,
    [Parameter(Mandatory=$true)][string]$AssemblerPath,
    [string]$WorkDirectory = (Join-Path $env:TEMP "fsr4-gfx803-dxil-selftest")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$DxcPath = (Resolve-Path -LiteralPath $DxcPath).Path
$DxCompilerPath = (Resolve-Path -LiteralPath $DxCompilerPath).Path
$AssemblerPath = (Resolve-Path -LiteralPath $AssemblerPath).Path
$source = Join-Path $PSScriptRoot "dot4-probe.hlsl"
$transformer = Join-Path (Split-Path -Parent $PSScriptRoot) "expand_dot4_gfx803.py"
if (-not (Test-Path -LiteralPath $source)) { throw "DXIL self-test HLSL file is missing: $source" }
if (-not (Test-Path -LiteralPath $transformer)) { throw "DXIL transformer is missing: $transformer" }

$python = Get-Command "python.exe" -ErrorAction SilentlyContinue
if ($null -eq $python) { throw "python.exe is required." }

New-Item -ItemType Directory -Force -Path $WorkDirectory | Out-Null
$inputDxil = Join-Path $WorkDirectory "dot4-input.dxil"
$inputLl = Join-Path $WorkDirectory "dot4-input.ll"
$outputLl = Join-Path $WorkDirectory "dot4-expanded.ll"
$outputDxil = Join-Path $WorkDirectory "dot4-expanded.dxil"

& $DxcPath -T cs_6_4 -E main -Fo $inputDxil $source
if ($LASTEXITCODE -ne 0) { throw "DXC could not compile the dot4 HLSL probe." }

$dump = & $DxcPath -dumpbin $inputDxil 2>&1
if ($LASTEXITCODE -ne 0) { throw "DXC could not disassemble the probe DXIL." }
$dumpText = [string]::Join([Environment]::NewLine, @($dump | ForEach-Object { $_.ToString() }))
[System.IO.File]::WriteAllText($inputLl, $dumpText, [System.Text.UTF8Encoding]::new($false))

if (-not (Select-String -LiteralPath $inputLl -Pattern 'dx\.op\.dot4AddPacked\.i32' -Quiet)) {
    throw "The DXC release did not emit the expected dx.op.dot4AddPacked.i32 intrinsic."
}

& $python.Source $transformer $inputLl $outputLl
if ($LASTEXITCODE -ne 0) { throw "The DXIL dot4 expansion script failed." }

& $AssemblerPath $DxCompilerPath $outputLl $outputDxil
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outputDxil)) {
    throw "The transformed DXIL did not pass Microsoft's assembler/validator."
}

$verified = & $DxcPath -dumpbin $outputDxil 2>&1
if ($LASTEXITCODE -ne 0) { throw "DXC could not read back the validated DXIL output." }
$verifyText = [string]::Join([Environment]::NewLine, @($verified | ForEach-Object { $_.ToString() }))
if ($verifyText -match 'dx\.op\.dot4AddPacked\.i32') {
    throw "A packed dot4 intrinsic remains in the transformed DXIL."
}

Write-Host "PASS: DXC compiled the probe, the packed-int8 dot product was expanded, and the DXIL validator accepted the output." -ForegroundColor Green
