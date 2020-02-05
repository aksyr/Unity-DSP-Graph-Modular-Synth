using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;
using XNode;
using UnityEditorInternal;
using Unity.Audio;

public class ADSRNodeWidget : DSPAudioKernelWidget<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>
{
    [Range(0f, 10f)]
    public float Attack = 0.1f;
    [Range(0f, 10f)]
    public float Decay = 0.1f;
    [Range(0f, 1f)]
    public float Sustain = 0.5f;
    [Range(0f, 10f)]
    public float Release = 0.1f;

    public override void Init()
    {
        base.Init();
    }

    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget Gate;

    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget Output;

    protected override void AddUpdateParametersToBlock(DSPCommandBlock block)
    {
        block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_DSPNode, ADSRNode.Parameters.Attack, Attack);
        block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_DSPNode, ADSRNode.Parameters.Decay, Decay);
        block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_DSPNode, ADSRNode.Parameters.Sustain, Sustain);
        block.SetFloat<ADSRNode.Parameters, ADSRNode.Providers, ADSRNode>(_DSPNode, ADSRNode.Parameters.Release, Release);
    }
}