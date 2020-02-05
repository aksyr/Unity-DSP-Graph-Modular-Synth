using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;
using XNode;
using UnityEditorInternal;
using Unity.Audio;

public class AttenuatorNodeWidget : DSPAudioKernelWidget<AttenuatorNode.Parameters, AttenuatorNode.Providers, AttenuatorNode>
{
    [Range(0f, 5f)]
    public float Multiplier = 1;

    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget Input;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget Output;

    protected override void AddUpdateParametersToBlock(DSPCommandBlock block)
    {
        block.SetFloat<AttenuatorNode.Parameters, AttenuatorNode.Providers, AttenuatorNode>(_DSPNode, AttenuatorNode.Parameters.Multiplier, Multiplier);
    }
}