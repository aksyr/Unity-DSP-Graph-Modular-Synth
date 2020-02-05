using UnityEngine;
using System.Collections;
using Unity.Audio;
using System.Collections.Generic;
using XNode;
using System.Linq;
using System;

public class MixerNodeWidget : DSPAudioKernelWidget<MixerNode.Parameters, MixerNode.Providers, MixerNode>
{
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget PoliInput;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(1, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget PoliCV;

    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget MonoOutput;
}
