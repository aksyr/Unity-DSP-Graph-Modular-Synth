using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XNode;
using XNodeEditor;
using static XNodeEditor.NodeEditor;
using UnityEditor;

[CustomNodeEditor(typeof(SpectrumNodeWidget))]
public class SpectrumNodeWidgetEditor : DSPNodeWidgetEditor
{
    private SpectrumNodeWidget _Node;
    private Vector2 _ScrollPos;

    public override void OnBodyGUI()
    {
        if (_Node == null) _Node = target as SpectrumNodeWidget;

        base.OnBodyGUI();

        if (_Node.SpectrumRenderer != null && _Node.SpectrumRenderer.SpectrumRT != null)
        {
            GUILayout.Space(_Node.SpectrumRenderer.SpectrumRT.height + 10);
            EditorGUI.DrawPreviewTexture(new Rect(20, 80, 1024, 340), _Node.SpectrumRenderer.SpectrumRT);
            window.Repaint();
        }
        else
        {
            GUILayout.Space(340 + 10);
            EditorGUI.DrawRect(new Rect(20, 80, 1024, 340), new Color(0.3f, 0.3f, 0.4f, 1.0f));
        }
    }
}