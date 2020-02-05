using UnityEngine;
using System.Collections;
using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Audio;
using XNode;

public abstract class DSPAudioKernelWidget<TParameters, TProviders, TAudioKernel> : DSPNodeWidget
    where TParameters : unmanaged, Enum
    where TProviders : unmanaged, Enum
    where TAudioKernel : struct, IAudioKernel<TParameters, TProviders>
{
    public override void Init()
    {
        base.Init();

        if (DSPReady)
        {
            using (var block = DSPGraph.CreateCommandBlock())
            {
                _DSPNode = block.CreateDSPNode<TParameters, TProviders, TAudioKernel>();

                // gather possible inlets and outlets
                List<DSPPortAttribute> inlets = new List<DSPPortAttribute>();
                List<DSPPortAttribute> outlets = new List<DSPPortAttribute>();
                {
                    var fields = NodeDataCache.GetNodeFields(GetType());
                    foreach (var field in fields)
                    {
                        var attributes = field.GetCustomAttributes(false).ToList();
                        var dspPort = attributes.Find(x => x is DSPPortAttribute) as DSPPortAttribute;
                        if (dspPort != null)
                        {
                            if (attributes.Find(x => x is InputAttribute) != null)
                            {
                                inlets.Add(dspPort);
                            }
                            else if (attributes.Find(x => x is OutputAttribute) != null)
                            {
                                outlets.Add(dspPort);
                            }
                        }
                    }
                }

                // sort by index
                inlets.Sort((l, r) =>
                {
                    return l.portIndex - r.portIndex;
                });
                outlets.Sort((l, r) =>
                {
                    return l.portIndex - r.portIndex;
                });
#if UNITY_ASSERTIONS
                // validate
                for (int i = 0; i < inlets.Count; ++i) Debug.Assert(inlets[i].portIndex == i);
                for (int i = 0; i < outlets.Count; ++i) Debug.Assert(outlets[i].portIndex == i);
#endif

                // add inlets and outlets
                foreach (var inlet in inlets) block.AddInletPort(_DSPNode, inlet.channels, inlet.format);
                foreach (var outlet in outlets) block.AddOutletPort(_DSPNode, outlet.channels, outlet.format);

                AddUpdateParametersToBlock(block);
            }
        }
    }
}
