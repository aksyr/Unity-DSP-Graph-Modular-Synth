using UnityEngine;
using System.Collections;
using Unity.Audio;

public class DSPGraphContainer : MonoBehaviour
{
    public GameObject ScopeRendererPrefab;
    public GameObject SpectrumRendererPrefab;

    public DSPGraph DSPGraph { get { return _Graph; } }
    protected DSPGraph _Graph;
    protected MyAudioDriver _Driver;
    protected AudioOutputHandle _OutputHandle;

    public DSPWidgetGraph WidgetGraph;

    void Awake()
    {
        AudioConfiguration audioConfig = AudioSettings.GetConfiguration();
        Debug.LogFormat("BufferSize={0} SampleRate={1}", audioConfig.dspBufferSize, audioConfig.sampleRate);

        _Graph = DSPGraph.Create(SoundFormat.Stereo, 2, audioConfig.dspBufferSize, audioConfig.sampleRate);
        _Driver = new MyAudioDriver { Graph = _Graph };
        _OutputHandle = _Driver.AttachToDefaultOutput();

        WidgetGraph.OnEnable();
        for(int i=WidgetGraph.nodes.Count-1; i>=0; --i)
        {
            WidgetGraph.nodes[i].Init();
        }
        for (int i = WidgetGraph.nodes.Count - 1; i >= 0; --i)
        {
            DSPNodeWidget dspNodeWidget = WidgetGraph.nodes[i] as DSPNodeWidget;
            if (dspNodeWidget == null) continue;
            foreach (var inputPort in dspNodeWidget.Ports)
            {
                if (!inputPort.IsInput) continue;
                var dspInlet = dspNodeWidget.GetDSPPortAttribute(inputPort);
                if (dspInlet == null) continue;

                var outputPorts = inputPort.GetConnections();
                foreach(var outputPort in outputPorts)
                {
                    if (!outputPort.IsOutput) continue;
                    DSPNodeWidget dspNodeWidget2 = outputPort.node as DSPNodeWidget;
                    if (dspNodeWidget2 == null) continue;
                    var dspOutlet = dspNodeWidget2.GetDSPPortAttribute(outputPort);
                    if (dspOutlet == null) continue;

                    using (var block = DSPGraph.CreateCommandBlock())
                    {
                        block.Connect(dspNodeWidget2.DSPNode, dspOutlet.portIndex, dspNodeWidget.DSPNode, dspInlet.portIndex);
                    }

                }

            }
        }
    }

    void Update()
    {
        _Graph.Update();
    }

    void OnDestroy()
    {
        
    }
}
