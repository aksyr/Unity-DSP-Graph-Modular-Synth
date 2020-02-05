using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XNode;
using XNodeEditor;
using static XNodeEditor.NodeEditor;

[CustomNodeEditor(typeof(DSPNodeWidget))]
public class DSPNodeWidgetEditor : NodeEditor
{
    private DSPNodeWidget _Node;

    public override void OnBodyGUI()
    {
        if (_Node == null) _Node = target as DSPNodeWidget;

        base.OnBodyGUI();

        _Node.UpdateParameters();
    }
}