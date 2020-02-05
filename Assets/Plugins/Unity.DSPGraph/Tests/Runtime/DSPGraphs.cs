using System;
using NUnit.Framework;
using Unity.Audio;
using Unity.Collections;
using UnityEngine.TestTools;

public class DSPGraphs
{
    // Invalid sound format
    [TestCase(-1, 2, 200, 48000)]
    // Mismatched format/channelCount
    [TestCase(SoundFormat.Raw, 1, 200, 48000)]
    [TestCase(SoundFormat.Mono, 2, 200, 48000)]
    [TestCase(SoundFormat.Stereo, 1, 200, 48000)]
    [TestCase(SoundFormat.Quad, 2, 200, 48000)]
    [TestCase(SoundFormat.Surround, 2, 200, 48000)]
    [TestCase(SoundFormat.FiveDot1, 2, 200, 48000)]
    [TestCase(SoundFormat.SevenDot1, 2, 200, 48000)]
    // Invalid channel count
    [TestCase(SoundFormat.Raw, 0, 200, 48000)]
    // Invalid buffer size
    [TestCase(SoundFormat.Stereo, 2, 0, 48000)]
    // Mismatched channelCount/bufferSize
    [TestCase(SoundFormat.Stereo, 2, 201, 48000)]
    [TestCase(SoundFormat.SevenDot1, 2, 100, 48000)]
    // Invalid sample rate
    [TestCase(SoundFormat.Stereo, 2, 200, 0)]
    public void DSPGraphCreateParameters_AreValidated(SoundFormat format, int channels, int bufferSize, int sampleRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { DSPGraph.Create(format, channels, bufferSize, sampleRate); });
    }

    [Test]
    public void BeginEndMix_FromNonMixerThread_Throws()
    {
        try
        {
            // Don't fail because of debug assertions
            LogAssert.ignoreFailingMessages = true;
            var soundFormat = SoundFormat.Stereo;
            var channelCount = 2;
            var frameCount = 5;

            using (var graph = DSPGraph.Create(soundFormat, channelCount, 1024, 48000))
            {
                Assert.Throws<InvalidOperationException>(() => { graph.OutputMixer.BeginMix(frameCount); });

                using (var buffer = new NativeArray<float>(frameCount * channelCount, Allocator.TempJob))
                    Assert.Throws<InvalidOperationException>(() =>
                    {
                        graph.OutputMixer.ReadMix(buffer, frameCount, channelCount);
                    });
            }
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void MixOverwritesData(DSPGraph.ExecutionMode executionMode)
    {
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            DSPNode node = graphSetup.CreateDSPNode<NoParameters, NoProviders, GenerateOne>();
            block.AddOutletPort(node, GraphSetup.ChannelCount, GraphSetup.SoundFormat);
            block.Connect(node, 0, graph.RootDSP, 0);
        }))
        {
            using (var buffer = new NativeArray<float>(setup.Graph.DSPBufferSize * GraphSetup.ChannelCount, Allocator.Temp, NativeArrayOptions.ClearMemory))
            {
                setup.Graph.BeginMix(setup.Graph.DSPBufferSize, executionMode);
                setup.Graph.ReadMix(buffer, setup.Graph.DSPBufferSize, GraphSetup.ChannelCount);
                foreach (var sample in buffer)
                    Assert.AreEqual(1.0f, sample, 0.001f);
            }
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void DSPClockIncrementsByLength(DSPGraph.ExecutionMode executionMode)
    {
        using (var setup = new GraphSetup())
        {
            for (int i = 0; i < 10; ++i)
            {
                Assert.AreEqual(i * setup.Graph.DSPBufferSize, setup.Graph.DSPClock);
                setup.PumpGraph();
            }
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void MultipleBeginMix_Works(DSPGraph.ExecutionMode executionMode)
    {
        using (var setup = new GraphSetup())
        {
            using (var buffer = new NativeArray<float>(setup.Graph.DSPBufferSize * GraphSetup.ChannelCount, Allocator.Temp))
            {
                int i;
                for (i = 0; i < 10; ++i)
                    setup.Graph.BeginMix(setup.Graph.DSPBufferSize);
                setup.Graph.ReadMix(buffer, setup.Graph.DSPBufferSize, GraphSetup.ChannelCount);
                Assert.AreEqual(i * setup.Graph.DSPBufferSize, setup.Graph.DSPClock);
            }
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void ReleasingDSPNode_InChain_KillsSignal(DSPGraph.ExecutionMode executionMode)
    {
        DSPNode generator = default;
        DSPNode passthrough = default;
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            generator = block.CreateDSPNode<NoParameters, NoProviders, GenerateOne>();
            block.AddOutletPort(generator, GraphSetup.ChannelCount, GraphSetup.SoundFormat);

            passthrough = block.CreateDSPNode<NoParameters, NoProviders, PassThrough>();
            block.AddInletPort(passthrough, GraphSetup.ChannelCount, GraphSetup.SoundFormat);
            block.AddOutletPort(passthrough, GraphSetup.ChannelCount, GraphSetup.SoundFormat);

            block.Connect(generator, 0, passthrough, 0);
            block.Connect(passthrough, 0, graph.RootDSP, 0);
        }))
        {
            using (var buffer = new NativeArray<float>(setup.Graph.DSPBufferSize * GraphSetup.ChannelCount, Allocator.Temp))
            {
                setup.Graph.BeginMix(setup.Graph.DSPBufferSize);
                setup.Graph.ReadMix(buffer, setup.Graph.DSPBufferSize, GraphSetup.ChannelCount);
                foreach (var sample in buffer)
                    Assert.AreEqual(1.0f, sample, 0.001f);

                using (var block = setup.Graph.CreateCommandBlock())
                    block.ReleaseDSPNode(passthrough);

                setup.Graph.BeginMix(setup.Graph.DSPBufferSize);
                setup.Graph.ReadMix(buffer, setup.Graph.DSPBufferSize, GraphSetup.ChannelCount);
                foreach (var sample in buffer)
                    Assert.AreEqual(0.0f, sample, 0.001f);

                using (var block = setup.Graph.CreateCommandBlock())
                    block.ReleaseDSPNode(generator);
            }
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void DSPNode_WithNoInputsOrOutputs_IsNotExecuted(DSPGraph.ExecutionMode executionMode)
    {
        DSPNode node = default;
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            node = graphSetup.CreateDSPNode<NoParameters, NoProviders, LifecycleTracking>();
        }))
        {
            setup.PumpGraph();
            Assert.AreEqual(0, LifecycleTracking.Executed);
        }
    }
}
