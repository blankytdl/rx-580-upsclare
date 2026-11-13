RWStructuredBuffer<int> OutputValues : register(u0);
StructuredBuffer<uint> PackedA : register(t0);
StructuredBuffer<uint> PackedB : register(t1);
StructuredBuffer<int> Accumulators : register(t2);

[numthreads(1, 1, 1)]
void main(uint3 dispatchId : SV_DispatchThreadID)
{
    const uint a = PackedA[dispatchId.x];
    const uint b = PackedB[dispatchId.x];
    const int accumulator = Accumulators[dispatchId.x];
    OutputValues[dispatchId.x] = dot4add_i8packed(a, b, accumulator);
}
