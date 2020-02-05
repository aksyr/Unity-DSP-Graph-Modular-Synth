using System;
using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
using XNode;
using Unity.Audio;

[NodeTint(30, 230, 30)]
public class RootNodeWidget : DSPNodeWidget
{
    [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.Inherited)]
    [DSPPort(0, Unity.Audio.SoundFormat.Stereo, 2)]
    public DSPNodeWidget StereoInput;

    public override void Init()
    {
        base.Init();

        if (DSPReady)
        {
            _DSPNode = DSPGraph.RootDSP;
        }
    }
}