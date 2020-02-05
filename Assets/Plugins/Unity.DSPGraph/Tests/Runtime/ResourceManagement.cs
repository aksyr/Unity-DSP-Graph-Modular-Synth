using System;
using System.Text.RegularExpressions;
using NUnit.Framework;

using Unity.Audio;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

public class ResourceManagement
{
    static readonly Regex kNullContextListAssertionMessage = new Regex("NULL resource context list");
    static readonly Regex kNodeLeakMessage = new Regex("Destroyed \\d DSPNodes");

    private const SoundFormat kSoundFormat = SoundFormat.Stereo;
    private const int kChannelCount = 2;


    [Test]
    public void StateChange_InvokesLifecycleCallbacks()
    {
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            DSPNode node = graphSetup.CreateDSPNode<NoParameters, NoProviders, LifecycleTracking>();
            block.AddOutletPort(node, kChannelCount, kSoundFormat);
            block.Connect(node, 0, graph.RootDSP, 0);
        }))
        {
            setup.PumpGraph();
            Assert.Greater(LifecycleTracking.Initialized, 0);
            Assert.Greater(LifecycleTracking.Executed, 0);
            Assert.AreEqual(LifecycleTracking.LifecyclePhase.Executing, LifecycleTracking.Phase);
        }
        Assert.AreEqual(LifecycleTracking.LifecyclePhase.Disposed, LifecycleTracking.Phase);
    }

    [Test]
    public void LeakyKernel_EmitsWarning()
    {
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            DSPNode node = graphSetup.CreateDSPNode<NoParameters, NoProviders, LeakyKernel>();
            block.AddOutletPort(node, kChannelCount, kSoundFormat);
            block.Connect(node, 0, graph.RootDSP, 0);
        }))
        {
            setup.PumpGraph();
            LogAssert.Expect(LogType.Warning, "1 leaked DSP node allocations");
        }
    }

    [Test]
    public void AllocatingKernel_Works()
    {
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            DSPNode node = graphSetup.CreateDSPNode<NoParameters, NoProviders, AllocatingKernel>();
            block.AddOutletPort(node, kChannelCount, kSoundFormat);
            block.Connect(node, 0, graph.RootDSP, 0);
        }))
        {
            setup.PumpGraph();
        }
    }

    [Test]
    public void AllocatingKernelMemory_DuringUpdateJob_Works()
    {
        var node = new DSPNode();
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            node = graphSetup.CreateDSPNode<NoParameters, NoProviders, LifecycleTracking>();
            block.AddOutletPort(node, kChannelCount, kSoundFormat);
            block.Connect(node, 0, graph.RootDSP, 0);
        }))
        {
            using (var block = setup.Graph.CreateCommandBlock())
                block.CreateUpdateRequest<AllocatingUpdateJob, NoParameters, NoProviders, LifecycleTracking>(new AllocatingUpdateJob(), node, null);
            setup.PumpGraph();
        }
    }

    [Test]
    public void AllocatingKernelMemory_OutsideDSPGraphContext_Throws()
    {
        #if UNITY_EDITOR
        // Resource context assertions only happen in the editor
        LogAssert.Expect(LogType.Assert, kNullContextListAssertionMessage);
        #endif
        Assert.Throws<InvalidOperationException>(() => new NativeArray<float>(200, Allocator.AudioKernel));
    }

    [Test]
    public void LeakyGraph_DoesntCrash()
    {
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            DSPNode node = graphSetup.CreateDSPNode<NoParameters, NoProviders, LifecycleTracking>();
            block.AddOutletPort(node, kChannelCount, kSoundFormat);
            block.Connect(node, 0, graph.RootDSP, 0);
        }))
        {
            setup.CleanupNodes = false;
            LogAssert.Expect(LogType.Warning, kNodeLeakMessage);
            setup.PumpGraph();
        }
    }

    [Test]
    public void ReleasingNode_InvalidatesHandle()
    {
        DSPNode node = default;
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            node = graphSetup.CreateDSPNode<NoParameters, NoProviders, LifecycleTracking>();
            block.AddOutletPort(node, kChannelCount, kSoundFormat);
            block.Connect(node, 0, graph.RootDSP, 0);
            graphSetup.CleanupNodes = false;
        }))
        {
            // Ensure that node is created
            setup.PumpGraph();
            Assert.True(node.Valid);

            // Release node
            using (var block = setup.Graph.CreateCommandBlock())
                block.ReleaseDSPNode(node);
            setup.PumpGraph();

            // Ensure that node handle is no longer valid
            Assert.False(node.Valid);
        }
    }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void WritingToInputBuffer_Throws(DSPGraph.ExecutionMode executionMode)
    {
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            DSPNode generator = graphSetup.CreateDSPNode<NoParameters, NoProviders, GenerateOne>();
            block.AddOutletPort(generator, kChannelCount, kSoundFormat);

            DSPNode node = graphSetup.CreateDSPNode<NoParameters, NoProviders, WritingToInputBufferKernel>();
            block.AddInletPort(node, kChannelCount, kSoundFormat);
            block.AddOutletPort(node, kChannelCount, kSoundFormat);

            block.Connect(generator, 0, node, 0);
            block.Connect(node, 0, graph.RootDSP, 0);
        }))
        {
            using (var buff = new NativeArray<float>(200, Allocator.TempJob))
            {
                setup.Graph.BeginMix(0, executionMode);
                setup.Graph.ReadMix(buff, 200 / kChannelCount, kChannelCount);
            }
        }
    }
#endif

    struct LeakyKernel : IAudioKernel<NoParameters, NoProviders>
    {
        [NativeDisableContainerSafetyRestriction]
        private NativeArray<float> m_Buffer;

        public void Initialize()
        {
            m_Buffer = new NativeArray<float>(200, Allocator.AudioKernel);
        }

        public void Execute(ref ExecuteContext<NoParameters, NoProviders> context)
        {
        }

        public void Dispose()
        {
        }
    }

    struct AllocatingKernel : IAudioKernel<NoParameters, NoProviders>
    {
        public void Initialize()
        {
        }

        public void Execute(ref ExecuteContext<NoParameters, NoProviders> context)
        {
            using (var buffer = new NativeArray<float>(context.Outputs.GetSampleBuffer(0).Samples, Allocator.AudioKernel))
                buffer.CopyTo(buffer);
        }

        public void Dispose()
        {
        }
    }

    unsafe struct AllocatingUpdateJob : IAudioKernelUpdate<NoParameters, NoProviders, LifecycleTracking>
    {
        public void Update(ref LifecycleTracking audioKernel)
        {
            using (var buffer = new NativeArray<float>(200, Allocator.AudioKernel))
                UnsafeUtility.MemClear(buffer.GetUnsafePtr(), 200 * UnsafeUtility.SizeOf<float>());
        }
    }

    struct WritingToInputBufferKernel : IAudioKernel<NoParameters, NoProviders>
    {
        public void Initialize() {}

        public void Execute(ref ExecuteContext<NoParameters, NoProviders> context)
        {
            var inputBuffer = context.Inputs.GetSampleBuffer(0).Buffer;
            Assert.Throws<InvalidOperationException>(() =>
            {
                for (int index = 0; index < inputBuffer.Length; ++index)
                    inputBuffer[index] = index;
            });
        }

        public void Dispose() {}
    }
}
