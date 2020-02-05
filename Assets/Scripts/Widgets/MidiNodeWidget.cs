using UnityEngine;
using System.Collections;

public class MidiNodeWidget : DSPAudioKernelWidget<MidiNode.Parameters, MidiNode.Providers, MidiNode>
{
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget Gate;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(1, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget Note;
    [Output(ShowBackingValue.Never, ConnectionType.Multiple)]
    [DSPPort(2, Unity.Audio.SoundFormat.Mono, 16)]
    public DSPNodeWidget Retrigger;
}
