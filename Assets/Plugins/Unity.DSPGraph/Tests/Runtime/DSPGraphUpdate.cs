using NUnit.Framework;
using Unity.Collections;
using Unity.Audio;

public class DSPGraphUpdate
{
    struct TestUpdateKernel : IAudioKernel<NoParameters, NoProviders>
    {
        public int Control;
        public float Value;

        public void Initialize()
        {
        }

        public void Execute(ref ExecuteContext<NoParameters, NoProviders> context)
        {
            Assert.AreEqual(Control == 0 ? 0.0f : 69.0f, Value, 0.0001);
        }

        public void Dispose() {}
    }

    struct TestValueKernelUpdater : IAudioKernelUpdate<NoParameters, NoProviders, TestUpdateKernel>
    {
        public float ValueToDrop;

        public void Update(ref TestUpdateKernel audioKernel)
        {
            audioKernel.Control = 1;
            audioKernel.Value = ValueToDrop;
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void UpdateValueFromKernel(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        using (var graph = DSPGraph.Create(soundFormat, channelCount, 100, 1000))
        {
            DSPNode node;
            using (var block = graph.CreateCommandBlock())
            {
                node = block.CreateDSPNode<NoParameters, NoProviders, TestUpdateKernel>();
                block.AddOutletPort(node, channelCount, soundFormat);
                block.Connect(node, 0, graph.RootDSP, 0);
            }

            using (var buff = new NativeArray<float>(200, Allocator.Temp))
            {
                graph.BeginMix(0, executionMode);
                graph.ReadMix(buff, buff.Length / channelCount, channelCount);

                using (var block = graph.CreateCommandBlock())
                {
                    var kernelUpdater = new TestValueKernelUpdater
                    {
                        ValueToDrop = 69.0f
                    };

                    block.UpdateAudioKernel<TestValueKernelUpdater, NoParameters, NoProviders, TestUpdateKernel>(kernelUpdater, node);
                }

                graph.BeginMix(0, executionMode);
                graph.ReadMix(buff, buff.Length / channelCount, channelCount);
            }

            using (var block = graph.CreateCommandBlock())
                block.ReleaseDSPNode(node);
        }
    }

    struct NullAudioKernelUpdater : IAudioKernelUpdate<NoParameters, NoProviders, NullAudioKernel>
    {
        public void Update(ref NullAudioKernel audioKernel)
        {
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void UpdateRequestCallsDelegate(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        using (var graph = DSPGraph.Create(soundFormat, channelCount, 100, 1000))
        {
            DSPNode node;
            using (var block = graph.CreateCommandBlock())
            {
                node = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(node, channelCount, soundFormat);
                block.Connect(node, 0, graph.RootDSP, 0);
            }

            using (var buff = new NativeArray<float>(200, Allocator.Temp))
            {
                graph.BeginMix(0, executionMode);
                graph.ReadMix(buff, buff.Length / channelCount, channelCount);

                var called = false;
                DSPNodeUpdateRequest<NullAudioKernelUpdater, NoParameters, NoProviders, NullAudioKernel> updateRequest;
                using (var block = graph.CreateCommandBlock())
                    updateRequest = block.CreateUpdateRequest<NullAudioKernelUpdater, NoParameters, NoProviders, NullAudioKernel>(
                        new NullAudioKernelUpdater(), node,
                        req => { called = true; });

                graph.BeginMix(0, executionMode);
                graph.ReadMix(buff, buff.Length / channelCount, channelCount);

                Assert.False(called);
                graph.Update();
                Assert.True(called);
                updateRequest.Dispose();
            }

            using (var block = graph.CreateCommandBlock())
                block.ReleaseDSPNode(node);
        }
    }
}
