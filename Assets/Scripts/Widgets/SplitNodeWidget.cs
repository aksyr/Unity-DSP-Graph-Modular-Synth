using UnityEngine;
using System.Collections;

public class SplitNodeWidget : DSPAudioKernelWidget<SplitNode.Parameters, SplitNode.Providers, SplitNode>
{
    [Input(ShowBackingValue.Never, ConnectionType.Override)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget Input;

    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono1;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(1, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono2;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(2, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono3;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(3, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono4;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(4, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono5;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(5, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono6;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(6, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono7;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(7, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono8;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(8, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono9;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(9, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono10;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(10, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono11;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(11, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono12;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(12, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono13;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(13, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono14;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(14, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono15;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(15, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Mono16;
}
