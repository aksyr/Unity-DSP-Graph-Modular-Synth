using UnityEngine;
using System.Collections;
using Unity.Audio;
using Unity.Collections;
using Unity.Burst;

[BurstCompile(CompileSynchronously = true)]
public struct ScopeUpdateKernel : IAudioKernelUpdate<ScopeNode.Parameters, ScopeNode.Providers, ScopeNode>
{
    private NativeArray<float> _BufferX;
    public int InputChannelsX;
    public int BufferIdx;
    public float TriggerThreshold;

    public ScopeUpdateKernel(NativeArray<float> bufferX)
    {
        _BufferX = bufferX;
        InputChannelsX = 0;
        BufferIdx = 0;
        TriggerThreshold = 0f;
    }

    public void Update(ref ScopeNode audioKernel)
    {
        _BufferX.CopyFrom(audioKernel.BufferX);
        InputChannelsX = audioKernel.InputChannelsX;
        BufferIdx = audioKernel.BufferIdx;
        TriggerThreshold = audioKernel.TriggerThreshold;
    }
}
