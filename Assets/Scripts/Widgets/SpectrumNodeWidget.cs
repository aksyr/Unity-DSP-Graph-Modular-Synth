using UnityEngine;
using System.Collections;
using Unity.Audio;
using System.Collections.Generic;
using XNode;
using System.Linq;
using System;

[NodeWidth(1074), NodeTint(30, 230, 230)]
public class SpectrumNodeWidget : DSPAudioKernelWidget<SpectrumNode.Parameters, SpectrumNode.Providers, SpectrumNode>
{
    public SpectrumNode.WindowType Window = SpectrumNode.WindowType.Rectangular;

    [Input(ShowBackingValue.Never, ConnectionType.Override)]
    [DSPPort(0, Unity.Audio.SoundFormat.Mono, 1)]
    public DSPNodeWidget Input;

    [NonSerialized]
    public SpectrumRenderer SpectrumRenderer;

    public override void Init()
    {
        base.Init();

        if (DSPReady)
        {
            GameObject go = Instantiate(_DSPWidgetGraph.GraphContainer.SpectrumRendererPrefab);
            SpectrumRenderer = go.GetComponent<SpectrumRenderer>();
            SpectrumRenderer.Init(DSPGraph, DSPNode);
        }
    }

    protected override void AddUpdateParametersToBlock(DSPCommandBlock block)
    {
        block.SetFloat<SpectrumNode.Parameters, SpectrumNode.Providers, SpectrumNode>(_DSPNode, SpectrumNode.Parameters.Window, (float)Window);
    }
}
