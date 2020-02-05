using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;

public class ScopeManager : MonoBehaviour
{
    protected class Item
    {
        public ScopeRenderer scope;
        public SpectrumRenderer spectrum;
        public Rect rect;
        public float scale;

        public Rect scaledRect { get {
                var scaledRect = new Rect(rect);
                scaledRect.width *= scale;
                scaledRect.height *= scale;
                return scaledRect;
            }
        }
    }

    protected List<Item> _Items = new List<Item>();
    protected Item _HeldItem = null;
    protected Vector2 _PreviousMousePosition = Vector2.zero;

    public bool Draw;

    void Update()
    {
        Vector2 mousePosition = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        Vector2 mouseDelta = mousePosition - _PreviousMousePosition;
        _PreviousMousePosition = mousePosition;

        if (Input.GetMouseButtonUp(0))
        {
            _HeldItem = null;
        }

        if(Input.GetMouseButtonDown(0))
        {
            mouseDelta = Vector2.zero;

            Item touchedItem = null;
            for(int i=_Items.Count-1; i>=0; --i)
            {
                Item item = _Items[i];
                if(item.rect.Contains(mousePosition))
                {
                    touchedItem = item;
                    break;
                }
            }

            if (touchedItem != null) {
                _Items.Remove(touchedItem);
                _Items.Add(touchedItem);
                _HeldItem = touchedItem;
            }
        }

        if(_HeldItem != null && Input.GetMouseButton(0))
        {
            _HeldItem.rect.x += mouseDelta.x;
            _HeldItem.rect.y += mouseDelta.y;

            // scale and reposition
            Vector2 positionInRect = (mousePosition - _HeldItem.scaledRect.position) / _HeldItem.scaledRect.size;
            Vector2 oldSize = _HeldItem.scaledRect.size;
            float scrollDelta = -Input.mouseScrollDelta.y * 0.05f;
            _HeldItem.scale = math.clamp(_HeldItem.scale + scrollDelta, 0.1f, 4f);
            _HeldItem.rect.position += (oldSize-_HeldItem.scaledRect.size) * positionInRect;
        }

        foreach(Item item in _Items)
        {
            item.rect.x = math.clamp(item.rect.x, -item.scaledRect.width + 20, Screen.width - 20);
            item.rect.y = math.clamp(item.rect.y, -item.scaledRect.height + 20, Screen.height - 20);
        }
    }

    void OnGUI()
    {
        if (Draw && Event.current.type.Equals(EventType.Repaint))
        {
            foreach (Item i in _Items)
            {
                Texture tex = i.scope != null ? i.scope.ScopeRT : i.spectrum.SpectrumRT;
                Graphics.DrawTexture(i.scaledRect, tex);
            }
        }
    }

    public void Register(ScopeRenderer sr)
    {
        Rect rect = new Rect(0, 0, sr.ScopeRT.width, sr.ScopeRT.height);
        if(_Items.Count > 0)
        {
            Item lastItem = _Items[_Items.Count - 1];
            rect.x = lastItem.rect.x + 30;
            rect.y = lastItem.rect.y + 15;
        }
        _Items.Add(new Item()
        {
            scope = sr,
            rect = rect,
            scale = 1f
        });
    }

    public void Register(SpectrumRenderer sr)
    {
        Rect rect = new Rect(0, 0, sr.SpectrumRT.width, sr.SpectrumRT.height);
        if (_Items.Count > 0)
        {
            Item lastItem = _Items[_Items.Count - 1];
            rect.x = lastItem.rect.x + 30;
            rect.y = lastItem.rect.y + 15;
        }
        _Items.Add(new Item()
        {
            spectrum = sr,
            rect = rect,
            scale = 1f
        });
    }
}
