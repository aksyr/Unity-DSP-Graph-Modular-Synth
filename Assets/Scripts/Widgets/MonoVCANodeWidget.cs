using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;
using XNode;
using UnityEditorInternal;
using Unity.Audio;

public class MonoVCANodeWidget : DSPAudioKernelWidget<VCANode.Parameters, VCANode.Providers, VCANode>
{
    [Range(0f, 1f)]
    public float Multiplier = 1;

    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Voltage;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(1, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget MonoInput;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget MonoOutput;

    protected override void AddUpdateParametersToBlock(DSPCommandBlock block)
    {
        block.SetFloat<VCANode.Parameters, VCANode.Providers, VCANode>(_DSPNode, VCANode.Parameters.Multiplier, Multiplier);
    }
}