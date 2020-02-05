using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;

[BurstCompile]
public struct SpectrumUpdateKernel : IAudioKernelUpdate<SpectrumNode.Parameters, SpectrumNode.Providers, SpectrumNode>
{
    private NativeArray<float2> _Buffer;

    public SpectrumUpdateKernel(NativeArray<float2> buffer)
    {
        _Buffer = buffer;
    }

    public void Update(ref SpectrumNode audioKernel)
    {
        if (audioKernel.Buffer.IsCreated)
        {
            _Buffer.CopyFrom(audioKernel.Buffer);
        }
    }
}
