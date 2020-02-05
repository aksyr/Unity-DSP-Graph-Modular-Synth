using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;
using XNode;
using UnityEditorInternal;
using Unity.Audio;

[NodeWidth(350)]
public class OscilatorNodeWidget : DSPAudioKernelWidget<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>
{
    public float Frequency = 261.626f;
    public OscilatorNode.Mode Mode;
    public float FMMultiplier;
    public bool Unidirectional;

    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget FMInput;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(1, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget Pitch;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(2, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget Reset;

    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget Output;

    protected override void AddUpdateParametersToBlock(DSPCommandBlock block)
    {
        block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_DSPNode, OscilatorNode.Parameters.Frequency, Frequency);
        block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_DSPNode, OscilatorNode.Parameters.Mode, (float)Mode);
        block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_DSPNode, OscilatorNode.Parameters.FMMultiplier, FMMultiplier);
        block.SetFloat<OscilatorNode.Parameters, OscilatorNode.Providers, OscilatorNode>(_DSPNode, OscilatorNode.Parameters.Unidirectional, Unidirectional ? 1.0f : 0.0f);
    }
}