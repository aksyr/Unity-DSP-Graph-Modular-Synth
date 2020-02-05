using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XNode;
using XNodeEditor;
using static XNodeEditor.NodeEditor;
using UnityEditor;

[CustomNodeEditor(typeof(MonoScopeNodeWidget))]
public class MonoScopeNodeWidgetEditor : DSPNodeWidgetEditor
{
    private MonoScopeNodeWidget _Node;

    public override void OnBodyGUI()
    {
        if (_Node == null) _Node = target as MonoScopeNodeWidget;

        base.OnBodyGUI();

        if (_Node.ScopeRenderer != null && _Node.ScopeRenderer.ScopeRT != null)
        {
            GUILayout.Space(_Node.ScopeRenderer.ScopeRT.height + 10);
            EditorGUI.DrawPreviewTexture(new Rect(20, 140, _Node.ScopeRenderer.ScopeRT.width, _Node.ScopeRenderer.ScopeRT.height), _Node.ScopeRenderer.ScopeRT);
            window.Repaint();
        }
        else
        {
            GUILayout.Space(314 + 10);
            EditorGUI.DrawRect(new Rect(20, 140, 512, 314), new Color(0.3f, 0.3f, 0.4f, 1.0f));
        }

        _Node.UpdateParameters();
    }
}