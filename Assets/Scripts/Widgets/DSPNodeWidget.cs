using System;
using UnityEngine;
using System.Linq;
using System.Collections;
using XNode;
using Unity.Audio;

public abstract class DSPNodeWidget : Node
{
    protected DSPGraph DSPGraph { get { return _DSPWidgetGraph.GraphContainer.DSPGraph; } }
    public bool DSPReady { get { return _DSPWidgetGraph.GraphContainer != null && _DSPWidgetGraph.GraphContainer.DSPGraph.Valid; } }

    public DSPWidgetGraph DSPWidgetGraph { get { return _DSPWidgetGraph; } }
    protected DSPWidgetGraph _DSPWidgetGraph;

    public DSPNode DSPNode { get { return _DSPNode; } }
    protected DSPNode _DSPNode;

    public override void Init()
    {
        base.Init();
        _DSPWidgetGraph = graph as DSPWidgetGraph;
    }

    public void OnDisable()
    {
        _DSPWidgetGraph = null;
    }

    protected void OnDestroy()
    {
        _DSPWidgetGraph = null;
    }

    public virtual DSPPortAttribute GetDSPPortAttribute(NodePort port)
    {
        var outputFieldInfo = NodeDataCache.GetNodeFields(port.node.GetType()).Where(_ => _.Name == port.fieldName).FirstOrDefault();
        if (outputFieldInfo != null)
        {
            return outputFieldInfo.GetCustomAttributes(typeof(DSPPortAttribute), false).FirstOrDefault() as DSPPortAttribute;
        }
        return null;
    }

    public override object GetValue(NodePort port)
    {
        return port;
    }

    public override bool CanConnect(NodePort from, NodePort to)
    {
        // assuming only inputs handle dsp connections
        if (from.node == this)
        {
            //Debug.LogFormat("CanConnect {0} {1}", from.fieldName, to.fieldName);
            var outputDspPort = GetDSPPortAttribute(from);
            var inputDspPort = GetDSPPortAttribute(to);

            if (outputDspPort != null ^ inputDspPort != null)
            {
                return false;
            }
            else if (outputDspPort != null && inputDspPort != null)
            {
                if (from.node == to.node)
                {
                    return false;
                }
                else
                {
                    return outputDspPort.IsCompatible(inputDspPort);
                }
            }
        }
        return base.CanConnect(from, to);
    }

    public override void OnCreateConnection(NodePort from, NodePort to)
    {
        // assuming only inputs handle dsp connections
        if (DSPReady && from.node == this)
        {
            Debug.LogFormat("OnCreateConnection {0} {1}", from.fieldName, to.fieldName);
            var outputDspPort = GetDSPPortAttribute(from);
            var inputDspPort = GetDSPPortAttribute(to);

            if (outputDspPort != null && inputDspPort != null)
            {
                using (var block = DSPGraph.CreateCommandBlock())
                {
                    block.Connect(((DSPNodeWidget)from.node).DSPNode, outputDspPort.portIndex, ((DSPNodeWidget)to.node).DSPNode, inputDspPort.portIndex);
                }
            }
        }
    }

    public override void OnRemoveConnection(NodePort from, NodePort to)
    {
        // assuming only inputs handle dsp connections
        if(DSPReady && from.node == this)
        {
            Debug.LogFormat("OnRemoveConnection {0} {1}", from.fieldName, to.fieldName);
            var outputDspPort = GetDSPPortAttribute(from);
            var inputDspPort = GetDSPPortAttribute(to);

            if (outputDspPort != null && inputDspPort != null)
            {
                using(var block = DSPGraph.CreateCommandBlock())
                {
                    block.Disconnect(((DSPNodeWidget)from.node).DSPNode, outputDspPort.portIndex, ((DSPNodeWidget)to.node).DSPNode, inputDspPort.portIndex);
                }
            }
        }
    }

    public virtual void UpdateParameters()
    {
        if (!DSPReady) return;
        using (var block = DSPGraph.CreateCommandBlock())
        {
            AddUpdateParametersToBlock(block);
        }
    }

    protected virtual void AddUpdateParametersToBlock(DSPCommandBlock block)
    {
    }
}
