using UnityEngine;
using System.Collections;
using Unity.Audio;
using System.Collections.Generic;
using XNode;
using System.Linq;
using System;

[NodeWidth(550), NodeTint(30, 230, 230)]
public class MonoScopeNodeWidget : DSPAudioKernelWidget<ScopeNode.Parameters, ScopeNode.Providers, ScopeNode>
{
    [Range(0.02133333333f, 10)]
    public float Time = 0.1f;
    [Range(-5f, 5f)]
    public float TriggerTreshold = 0f;
    [Range(1f, 20f)]
    public float Height = 5.01f;
    [Range(-20, 20f)]
    public float Offset = 0f;

    [Input(ShowBackingValue.Never, ConnectionType.Override)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget MonoInput;

    [NonSerialized]
    public ScopeRenderer ScopeRenderer;

    public override void Init()
    {
        base.Init();

        if (DSPReady)
        {
            GameObject go = Instantiate(_DSPWidgetGraph.GraphContainer.ScopeRendererPrefab);
            ScopeRenderer = go.GetComponent<ScopeRenderer>();
            ScopeRenderer.Init(DSPGraph, DSPNode);
        }
    }

    protected override void AddUpdateParametersToBlock(DSPCommandBlock block)
    {
        block.SetFloat<ScopeNode.Parameters, ScopeNode.Providers, ScopeNode>(_DSPNode, ScopeNode.Parameters.Time, Time);
        block.SetFloat<ScopeNode.Parameters, ScopeNode.Providers, ScopeNode>(_DSPNode, ScopeNode.Parameters.TriggerTreshold, TriggerTreshold);
        if (ScopeRenderer != null)
        {
            ScopeRenderer.Height = Height;
            ScopeRenderer.Offset = Offset;
        }
    }
}
