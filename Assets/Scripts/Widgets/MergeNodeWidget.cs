using UnityEngine;
using System.Collections;

public class MergeNodeWidget : DSPAudioKernelWidget<MergeNode.Parameters, MergeNode.Providers, MergeNode>
{
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono1;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(1, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono2;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(2, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono3;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(3, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono4;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(4, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono5;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(5, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono6;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(6, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono7;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(7, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono8;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(8, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono9;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(9, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono10;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(10, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono11;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(11, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono12;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(12, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono13;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(13, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono14;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(14, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono15;
    [Input(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(15, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono16;


    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget Output;
}
