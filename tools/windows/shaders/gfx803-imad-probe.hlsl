// Copyright (c) 2026
//
// Compiler probe for the RX 580 / GFX803 Windows port.
// Compile each entry point separately with AMD Radeon GPU Analyzer:
//
//   intrinsic path:  dot4add_i8packed(uint, uint, int)
//   expanded path:   sign-extend four int8 lanes, then multiply-add them
//
// The probe does not assume either form maps to a particular ISA instruction.
// Inspect the resulting GFX803 disassembly.

#define PROBE_ROOT_SIGNATURE \
    "RootFlags(0), " \
    "DescriptorTable(SRV(t0)), " \
    "DescriptorTable(UAV(u0))"

[RootSignature(PROBE_ROOT_SIGNATURE)]
StructuredBuffer<uint2> gPackedPairs : register(t0);
RWStructuredBuffer<int> gResults : register(u0);

int ExtractSignedByte(uint packed, uint byteIndex)
{
    // byteIndex is a compile-time constant at each call site.
    uint shift = byteIndex * 8u;
    return (int)(packed << (24u - shift)) >> 24;
}

int ExpandedDot4Add(uint a, uint b, int accumulator)
{

    int a0 = ExtractSignedByte(a, 0u);
    int a1 = ExtractSignedByte(a, 1u);
    int a2 = ExtractSignedByte(a, 2u);
    int a3 = ExtractSignedByte(a, 3u);

    int b0 = ExtractSignedByte(b, 0u);
    int b1 = ExtractSignedByte(b, 1u);
    int b2 = ExtractSignedByte(b, 2u);
    int b3 = ExtractSignedByte(b, 3u);

    // Keep the multiply-add dependency chain explicit. Each product is
    // the product of two signed 8-bit values, so both inputs fit in 24 bits.
    int sum = accumulator;
    sum = a0 * b0 + sum;
    sum = a1 * b1 + sum;
    sum = a2 * b2 + sum;
    sum = a3 * b3 + sum;
    return sum;
}

[numthreads(64, 1, 1)]
void IntrinsicCS(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint2 pair = gPackedPairs[dispatchThreadId.x];
    gResults[dispatchThreadId.x] =
        dot4add_i8packed(pair.x, pair.y, 0);
}

[numthreads(64, 1, 1)]
void ExpandedCS(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint2 pair = gPackedPairs[dispatchThreadId.x];
    gResults[dispatchThreadId.x] =
        ExpandedDot4Add(pair.x, pair.y, 0);
}
