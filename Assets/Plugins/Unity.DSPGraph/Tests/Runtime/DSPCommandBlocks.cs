using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Audio;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;

public class DSPCommandBlocks
{
    static readonly Regex kArgumentException = new Regex("ArgumentException");

    [Test]
    public void Completing_CanceledCommandBlock_DoesNotWarn()
    {
        using (var setup = new GraphSetup())
        using (var block = setup.Graph.CreateCommandBlock())
            block.Cancel();
        // Complete should have no effect (but also should not produce errors)
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void UncompletedBlock_IsNotExecuted(DSPGraph.ExecutionMode executionMode)
    {
        using (var setup = new GraphSetup())
        {
            var block = setup.Graph.CreateCommandBlock();
            var node = block.CreateDSPNode<NoParameters, NoProviders, GenerateOne>();
            block.AddOutletPort(node, GraphSetup.ChannelCount, GraphSetup.SoundFormat);
            block.Connect(node, 0, setup.Graph.RootDSP, 0);

            using (var buffer = new NativeArray<float>(setup.Graph.DSPBufferSize * GraphSetup.ChannelCount, Allocator.Temp, NativeArrayOptions.ClearMemory))
            {
                setup.Graph.BeginMix(setup.Graph.DSPBufferSize, executionMode);
                setup.Graph.ReadMix(buffer, setup.Graph.DSPBufferSize, GraphSetup.ChannelCount);
                foreach (var sample in buffer)
                    Assert.AreEqual(0.0f, sample, 0.001f);

                block.Complete();
                setup.Graph.BeginMix(setup.Graph.DSPBufferSize, executionMode);
                setup.Graph.ReadMix(buffer, setup.Graph.DSPBufferSize, GraphSetup.ChannelCount);
                foreach (var sample in buffer)
                    Assert.AreEqual(1.0f, sample, 0.001f);

                using (block = setup.Graph.CreateCommandBlock())
                    block.ReleaseDSPNode(node);
            }
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void CancelingBlock_InvalidatesHandle(DSPGraph.ExecutionMode executionMode)
    {
        using (var setup = new GraphSetup())
        {
            var block = setup.Graph.CreateCommandBlock();
            var node = block.CreateDSPNode<NoParameters, NoProviders, GenerateOne>();
            block.AddOutletPort(node, GraphSetup.ChannelCount, GraphSetup.SoundFormat);
            block.Connect(node, 0, setup.Graph.RootDSP, 0);
            block.Cancel();
            Assert.False(block.Valid);
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void Connect_WithInterleavedBlocks_DoesntCrash(DSPGraph.ExecutionMode executionMode)
    {
        using (var setup = new GraphSetup())
        {
            using (var outerBlock = setup.Graph.CreateCommandBlock())
            {
                var node = outerBlock.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                using (var innerBlock = setup.Graph.CreateCommandBlock())
                {
                    innerBlock.AddOutletPort(node, GraphSetup.ChannelCount, GraphSetup.SoundFormat);
                    innerBlock.Connect(node, 0, setup.Graph.RootDSP, 0);
                }
            }

            LogAssert.Expect(LogType.Exception, kArgumentException);
        }
    }
}
