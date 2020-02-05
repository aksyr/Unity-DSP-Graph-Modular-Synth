using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Audio;
using UnityEditor;
using System;

public class DSPWidgetGraph : XNode.NodeGraph
{
    public RootNodeWidget RootNode { get { return _RootNode; } }
    [SerializeField] protected RootNodeWidget _RootNode;

    [HideInInspector, NonSerialized]
    public DSPGraphContainer GraphContainer;

    public void OnEnable()
    {
        GraphContainer = FindObjectOfType<DSPGraphContainer>();
    }

    public void OnDisable()
    {
        GraphContainer = null;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        GraphContainer = null;
    }

#if UNITY_EDITOR
    [MenuItem("Assets/Create/DSPGraph", priority =0)]
    static void CreateDSPWidgetGraph()
    {
        var graphAsset = ScriptableObject.CreateInstance<DSPWidgetGraph>();
        var path = AssetDatabase.GetAssetPath(Selection.activeObject);
        string typeName = UnityEditor.ObjectNames.NicifyVariableName(typeof(DSPWidgetGraph).Name);
        AssetDatabase.CreateAsset(graphAsset, path + "/" + typeName + ".asset");

        var node = graphAsset.AddNode<RootNodeWidget>();
        graphAsset._RootNode = node;
        if (string.IsNullOrEmpty(node.name))
        {
            node.name = UnityEditor.ObjectNames.NicifyVariableName(typeof(RootNodeWidget).Name);
        }
        AssetDatabase.AddObjectToAsset(node, graphAsset);
        AssetDatabase.SaveAssets();
    }
#endif

    //public override XNode.NodeGraph Copy()
    //{
    //    DSPWidgetGraph graph = base.Copy() as DSPWidgetGraph;
    //    for (int i = 0; i < graph.nodes.Count; ++i)
    //    {
    //        if (graph.nodes[i] is RootNodeWidget)
    //        {
    //            graph._RootNode = graph.nodes[i] as RootNodeWidget;
    //        }
    //    }
    //    return graph;
    //}
}