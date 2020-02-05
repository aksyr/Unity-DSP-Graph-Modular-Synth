using UnityEngine;
using UnityEditor;
using System;
using Unity.Audio;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class DSPPortAttribute : Attribute
{
    public int portIndex { get; private set; }
    public SoundFormat format { get; private set; }
    public int channels { get; private set; }

    public DSPPortAttribute(int portIndex, SoundFormat format, int channels)
    {
        this.portIndex = portIndex;
        this.format = format;
        this.channels = channels;
    }

    public bool IsCompatible(DSPPortAttribute other)
    {
        return format == other.format && channels == other.channels;
    }
}