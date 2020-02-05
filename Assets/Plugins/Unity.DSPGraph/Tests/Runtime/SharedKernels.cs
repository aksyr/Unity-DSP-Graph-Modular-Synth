using System;
using System.Collections.Generic;
using Unity.Audio;
using Unity.Collections;

enum NoParameters {}
enum NoProviders {}

struct GenerateOne : IAudioKernel<NoParameters, NoProviders>
{
    private float m_OutputValue;

    public void Initialize()
    {
        m_OutputValue = 1f;
    }

    public void Execute(ref ExecuteContext<NoParameters, NoProviders> context)
    {
        for (int o = 0; o < context.Outputs.Count; ++o)
        {
            var buffer = context.Outputs.GetSampleBuffer(o).Buffer;
            for (int i = 0; i < buffer.Length; ++i)
                buffer[i] = m_OutputValue;
        }
    }

    public void Dispose() {}
}

struct PassThrough : IAudioKernel<NoParameters, NoProviders>
{
    public void Initialize() {}

    public void Execute(ref ExecuteContext<NoParameters, NoProviders> context)
    {
        var outBuf = context.Outputs.GetSampleBuffer(0).Buffer;
        var inBuf = context.Inputs.GetSampleBuffer(0).Buffer;
        for (int i = 0; i < outBuf.Length; ++i)
            outBuf[i] = inBuf[i];
    }

    public void Dispose() {}
}

struct NullAudioKernel : IAudioKernel<NoParameters, NoProviders>
{
    public void Initialize()
    {
    }

    public void Execute(ref ExecuteContext<NoParameters, NoProviders> context)
    {
    }

    public void Dispose() {}
}

struct LifecycleTracking : IAudioKernel<NoParameters, NoProviders>
{
    public static LifecyclePhase Phase;
    public static int Initialized;
    public static int Executed;
    public static int Disposed;

    public enum LifecyclePhase
    {
        Uninitialized,
        Initialized,
        Executing,
        Disposed
    }

    public void Initialize()
    {
        Phase = LifecyclePhase.Initialized;
        Initialized = 1;
        Executed = 0;
        Disposed = 0;
    }

    public void Execute(ref ExecuteContext<NoParameters, NoProviders> context)
    {
        Phase = LifecyclePhase.Executing;
        ++Executed;
    }

    public void Dispose()
    {
        Phase = LifecyclePhase.Disposed;
        ++Disposed;
    }
}


struct SingleParameterKernel : IAudioKernel<SingleParameterKernel.Parameters, NoProviders>
{
    public enum Parameters{ Parameter }

    public void Initialize()
    {
    }

    public void Execute(ref ExecuteContext<Parameters, NoProviders> context)
    {
    }

    public void Dispose()
    {
    }
}


class GraphSetup : IDisposable
{
    public DSPGraph Graph { get;  }
    public DSPCommandBlock Block { get; }
    public bool CleanupNodes { get; set; } = true;
    private readonly List<DSPNode> m_CreatedNodes;

    public const SoundFormat SoundFormat = Unity.Audio.SoundFormat.Stereo;
    public const int ChannelCount = 2;

    public GraphSetup(Action<GraphSetup, DSPGraph, DSPCommandBlock> callback = null)
    {
        m_CreatedNodes = new List<DSPNode>();
        Graph = DSPGraph.Create(SoundFormat, ChannelCount, 100, 1000);
        using (Block = Graph.CreateCommandBlock())
            callback?.Invoke(this, Graph, Block);
    }

    public DSPNode CreateDSPNode<TParameters, TProviders, TAudioKernel>()
        where TParameters   : unmanaged, Enum
        where TProviders    : unmanaged, Enum
        where TAudioKernel  : struct, IAudioKernel<TParameters, TProviders>
    {
        DSPNode node = Block.CreateDSPNode<TParameters, TProviders, TAudioKernel>();
        m_CreatedNodes.Add(node);
        return node;
    }

    public void PumpGraph()
    {
        using (var buff = new NativeArray<float>(200, Allocator.TempJob))
        {
            Graph.BeginMix(0);
            Graph.ReadMix(buff, buff.Length / ChannelCount, ChannelCount);
        }
    }

    public void Dispose()
    {
        if (CleanupNodes)
            using (DSPCommandBlock block = Graph.CreateCommandBlock())
                foreach (DSPNode node in m_CreatedNodes)
                    block.ReleaseDSPNode(node);
        Graph.Dispose();
    }
}
