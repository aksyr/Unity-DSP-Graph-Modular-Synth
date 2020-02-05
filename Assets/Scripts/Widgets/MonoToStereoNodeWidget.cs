using UnityEngine;
using System.Collections;

public class MonoToStereoNodeWidget : DSPAudioKernelWidget<MonoToStereoNode.Parameters, MonoToStereoNode.Providers, MonoToStereoNode>
{
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget MonoLeft;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(1, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget MonoRight;

    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Stereo, 2)]
    public DSPNodeWidget StereoOutputs;
}
