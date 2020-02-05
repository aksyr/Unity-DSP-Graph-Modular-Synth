using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Audio;
using Unity.Mathematics;

public class DSPGraphParameters
{
    static void PumpGraph(in DSPGraph graph, int channelCount, DSPGraph.ExecutionMode executionMode)
    {
        using (var buffer = new NativeArray<float>(200, Allocator.Temp))
        {
            int frameCount = buffer.Length / channelCount;
            graph.BeginMix(frameCount, executionMode);
            graph.ReadMix(buffer, frameCount, channelCount);
        }
    }

    struct TestImmediateLerp : IAudioKernel<TestImmediateLerp.Parameters, NoProviders>
    {
        public enum Parameters
        {
            Parameter
        }

        public void Initialize() {}

        public void Execute(ref ExecuteContext<Parameters, NoProviders> context)
        {
            for (int i = 0; i < context.DSPBufferSize; i++)
                Assert.AreEqual(i * 100.0 / 99.0, context.Parameters.GetFloat(Parameters.Parameter, i), 0.0001);
        }

        public void Dispose() {}
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void ImmediateLerp(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        using (var graph = DSPGraph.Create(soundFormat, channelCount, 100, 1000))
        {
            DSPNode node;
            using (var block = graph.CreateCommandBlock())
            {
                node = block.CreateDSPNode<TestImmediateLerp.Parameters, NoProviders, TestImmediateLerp>();
                block.AddOutletPort(node, channelCount, soundFormat);
                block.Connect(node, 0, graph.RootDSP, 0);

                block.SetFloat<TestImmediateLerp.Parameters, NoProviders, TestImmediateLerp>(node,
                    TestImmediateLerp.Parameters.Parameter, 100.0f, 100);
            }

            PumpGraph(in graph, channelCount, executionMode);

            using (var block = graph.CreateCommandBlock())
                block.ReleaseDSPNode(node);
        }
    }

    struct ClampParams : IAudioKernel<ClampParams.Parameters, NoProviders>
    {
        public enum Parameters
        {
            [ParameterDefault(0.0f)]
            [ParameterRange(0.0f, 100.0f)]
            Parameter
        }

        public void Initialize() {}

        public void Execute(ref ExecuteContext<Parameters, NoProviders> context)
        {
            for (int i = 0; i < context.DSPBufferSize; i++)
                Assert.LessOrEqual(context.Parameters.GetFloat(Parameters.Parameter, i), 100.0f);
        }

        public void Dispose() {}
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void ClampedImmediateLerp(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        using (var graph = DSPGraph.Create(soundFormat, channelCount, 100, 1000))
        {
            DSPNode node;
            using (var block = graph.CreateCommandBlock())
            {
                node = block.CreateDSPNode<ClampParams.Parameters, NoProviders, ClampParams>();
                block.AddOutletPort(node, channelCount, soundFormat);
                block.Connect(node, 0, graph.RootDSP, 0);

                block.SetFloat<ClampParams.Parameters, NoProviders, ClampParams>(node, ClampParams.Parameters.Parameter, 200.0f, 100);
            }

            PumpGraph(in graph, channelCount, executionMode);

            using (var block = graph.CreateCommandBlock())
                block.ReleaseDSPNode(node);
        }
    }

    struct DefaultParams : IAudioKernel<DefaultParams.Parameters, NoProviders>
    {
        public enum Parameters
        {
            [ParameterDefault(69.0f)]
            Parameter
        }

        public void Initialize() {}

        public void Execute(ref ExecuteContext<Parameters, NoProviders> context)
        {
            for (int i = 0; i < context.DSPBufferSize; i++)
                Assert.AreEqual(69.0f, context.Parameters.GetFloat(Parameters.Parameter, i));
        }

        public void Dispose() {}
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void DefaultParameters(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        using (var graph = DSPGraph.Create(soundFormat, channelCount, 100, 1000))
        {
            DSPNode node;
            using (var block = graph.CreateCommandBlock())
            {
                node = block.CreateDSPNode<DefaultParams.Parameters, NoProviders, DefaultParams>();
                block.AddOutletPort(node, channelCount, soundFormat);
                block.Connect(node, 0, graph.RootDSP, 0);
            }

            PumpGraph(in graph, channelCount, executionMode);

            using (var block = graph.CreateCommandBlock())
                block.ReleaseDSPNode(node);
        }
    }

    struct InterruptedKey : IAudioKernel<InterruptedKey.Parameters, NoProviders>
    {
        public enum Parameters
        {
            [ParameterDefault(0.0f)]
            Key,

            [ParameterDefault(0.0f)]
            Toggle
        }

        public void Initialize() {}

        public void Execute(ref ExecuteContext<Parameters, NoProviders> context)
        {
            if (Math.Abs(context.Parameters.GetFloat(Parameters.Toggle, 0)) < 0.00001f)
            {
                for (int i = 0; i < context.DSPBufferSize; i++)
                    Assert.AreEqual((i + context.DSPClock) * 100.0 / 199.0, context.Parameters.GetFloat(Parameters.Key, i), 0.0001);
            }
            else
            {
                for (int i = 0; i < context.DSPBufferSize; i++)
                    Assert.AreEqual(69.0f, context.Parameters.GetFloat(Parameters.Key, i), 0.0001);
            }
        }

        public void Dispose() {}
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void InterruptedKeys(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        using (var graph = DSPGraph.Create(soundFormat, channelCount, 100, 1000))
        {
            DSPNode node;
            using (var block = graph.CreateCommandBlock())
            {
                node = block.CreateDSPNode<InterruptedKey.Parameters, NoProviders, InterruptedKey>();
                block.AddOutletPort(node, channelCount, soundFormat);
                block.Connect(node, 0, graph.RootDSP, 0);
                block.SetFloat<InterruptedKey.Parameters, NoProviders, InterruptedKey>(node, InterruptedKey.Parameters.Key, 100.0f, 200);
            }

            using (var buff = new NativeArray<float>(200, Allocator.Temp))
            {
                graph.BeginMix(0, executionMode);
                graph.ReadMix(buff, buff.Length / channelCount, channelCount);

                using (var block = graph.CreateCommandBlock())
                {
                    block.SetFloat<InterruptedKey.Parameters, NoProviders, InterruptedKey>(node, InterruptedKey.Parameters.Key,
                        69.0f);
                    block.SetFloat<InterruptedKey.Parameters, NoProviders, InterruptedKey>(node, InterruptedKey.Parameters.Toggle,
                        1.0f);
                }

                graph.BeginMix(0, executionMode);
                graph.ReadMix(buff, buff.Length / channelCount, channelCount);
            }

            using (var block = graph.CreateCommandBlock())
                block.ReleaseDSPNode(node);
        }
    }

    struct TwoLerps : IAudioKernel<TwoLerps.Parameters, NoProviders>
    {
        public enum Parameters
        {
            [ParameterDefault(0.0f)]
            Parameter
        }

        public void Initialize() {}

        public void Execute(ref ExecuteContext<Parameters, NoProviders> context)
        {
            for (int i = 0; i < 50; i++)
                Assert.AreEqual(i * 100.0 / 49.0, context.Parameters.GetFloat(Parameters.Parameter, i), 0.0001);

            for (int i = 50; i < 100; i++)
                Assert.AreEqual(100.0f + ((i - 49) * -100.0 / 50.0), context.Parameters.GetFloat(Parameters.Parameter, i), 0.0001);
        }

        public void Dispose() {}
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void TwoLerpsInOneMix(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        using (var graph = DSPGraph.Create(soundFormat, channelCount, 100, 1000))
        {
            DSPNode node;
            using (var block = graph.CreateCommandBlock())
            {
                node = block.CreateDSPNode<TwoLerps.Parameters, NoProviders, TwoLerps>();
                block.AddOutletPort(node, channelCount, soundFormat);
                block.Connect(node, 0, graph.RootDSP, 0);

                block.AddFloatKey<TwoLerps.Parameters, NoProviders, TwoLerps>(node, TwoLerps.Parameters.Parameter, 49, 100.0f);
                block.AddFloatKey<TwoLerps.Parameters, NoProviders, TwoLerps>(node, TwoLerps.Parameters.Parameter, 99, 0.0f);
            }

            PumpGraph(in graph, channelCount, executionMode);

            using (var block = graph.CreateCommandBlock())
                block.ReleaseDSPNode(node);
        }
    }

    struct TestSecondMix : IAudioKernel<TestSecondMix.Parameters, NoProviders>
    {
        public enum Parameters
        {
            Parameter
        }

        bool m_MixedOnce;
        public const float ValueAfterFirstMix = 31.337f;

        public void Initialize() {}

        public void Execute(ref ExecuteContext<Parameters, NoProviders> context)
        {
            if (!m_MixedOnce)
            {
                m_MixedOnce = true;
                return;
            }

            for (int i = 0; i < context.DSPBufferSize; i++)
                Assert.AreEqual(ValueAfterFirstMix, context.Parameters.GetFloat(Parameters.Parameter, i), 0.0001);
        }

        public void Dispose() {}
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void AfterUncheckedInterpolation_ParameterValueIsCorrect(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        var bufferSize = 100;
        using (var graph = DSPGraph.Create(soundFormat, channelCount, bufferSize, 1000))
        {
            DSPNode node;
            using (var block = graph.CreateCommandBlock())
            {
                node = block.CreateDSPNode<TestSecondMix.Parameters, NoProviders, TestSecondMix>();
                block.AddOutletPort(node, channelCount, soundFormat);
                block.Connect(node, 0, graph.RootDSP, 0);

                block.SetFloat<TestSecondMix.Parameters, NoProviders, TestSecondMix>(node,
                    TestSecondMix.Parameters.Parameter, TestSecondMix.ValueAfterFirstMix, bufferSize / 2);
            }

            // Parameter interpolation finishes without being read during the first mix
            PumpGraph(in graph, channelCount, executionMode);
            // Now we verify that the parameter value is correct
            PumpGraph(in graph, channelCount, executionMode);

            using (var block = graph.CreateCommandBlock())
                block.ReleaseDSPNode(node);
        }
    }

    [Test]
    public void AppendKey_AddsKey()
    {
        DSPNode node = default;
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            node = graphSetup.CreateDSPNode<SingleParameterKernel.Parameters, NoProviders, SingleParameterKernel>();
        }))
        {
            setup.PumpGraph();

            long dspClock = 42;
            float4 value = 10.0f;

            // Get copy of node with populated fields
            node = setup.Graph.LookupNode(node.Handle);
            var nodeParameters = node.Parameters;
            var parameter = nodeParameters[(int)SingleParameterKernel.Parameters.Parameter];
            var newKeyIndex = setup.Graph.AppendKey(parameter.KeyIndex, DSPParameterKey.NullIndex, dspClock, value);

            Assert.AreNotEqual(DSPParameterKey.NullIndex, newKeyIndex);
            var key = setup.Graph.ParameterKeys[newKeyIndex];
            Assert.AreEqual(dspClock, key.DSPClock);
            Assert.AreEqual(value[0], key.Value[0], 0.001f);
            Assert.AreEqual(DSPParameterKey.NullIndex, key.NextKeyIndex);
        }
    }

    [Test]
    public void AppendMultipleKeys_ChainsKeys()
    {
        DSPNode node = default;
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            node = graphSetup.CreateDSPNode<SingleParameterKernel.Parameters, NoProviders, SingleParameterKernel>();
        }))
        {
            setup.PumpGraph();

            long dspClock = 42;
            float4 value = 10.0f;

            // Get copy of node with populated fields
            node = setup.Graph.LookupNode(node.Handle);
            var nodeParameters = node.Parameters;
            var parameter = nodeParameters[(int)SingleParameterKernel.Parameters.Parameter];
            var firstKeyIndex = setup.Graph.AppendKey(parameter.KeyIndex, DSPParameterKey.NullIndex, dspClock, value);
            Assert.AreNotEqual(DSPParameterKey.NullIndex, firstKeyIndex);
            var secondKeyIndex = setup.Graph.AppendKey(firstKeyIndex, firstKeyIndex, dspClock + 1, value + 1);
            Assert.AreNotEqual(DSPParameterKey.NullIndex, secondKeyIndex);
            Assert.AreNotEqual(firstKeyIndex, secondKeyIndex);

            var key = setup.Graph.ParameterKeys[firstKeyIndex];
            Assert.AreEqual(dspClock, key.DSPClock);
            Assert.AreEqual(value[0], key.Value[0], 0.001f);
            Assert.AreEqual(secondKeyIndex, key.NextKeyIndex);

            key = setup.Graph.ParameterKeys[secondKeyIndex];
            Assert.AreEqual(dspClock + 1, key.DSPClock);
            Assert.AreEqual(value[0] + 1, key.Value[0], 0.001f);
            Assert.AreEqual(DSPParameterKey.NullIndex, key.NextKeyIndex);
        }
    }

    [Test]
    public void GetLastKey_ReturnsExpectedKey()
    {
        DSPNode node = default;
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            node = graphSetup.CreateDSPNode<SingleParameterKernel.Parameters, NoProviders, SingleParameterKernel>();
        }))
        {
            setup.PumpGraph();

            long dspClock = 42;
            float4 value = 10.0f;

            // Get copy of node with populated fields
            node = setup.Graph.LookupNode(node.Handle);
            var parameter = node.Parameters[(int)SingleParameterKernel.Parameters.Parameter];
            Assert.AreEqual(DSPParameterKey.NullIndex, setup.Graph.GetLastParameterKeyIndex(parameter.KeyIndex));

            var firstKeyIndex = setup.Graph.AppendKey(parameter.KeyIndex, DSPParameterKey.NullIndex, dspClock, value);
            Assert.AreNotEqual(DSPParameterKey.NullIndex, firstKeyIndex);
            Assert.AreEqual(firstKeyIndex, setup.Graph.GetLastParameterKeyIndex(firstKeyIndex));

            var secondKeyIndex = setup.Graph.AppendKey(firstKeyIndex, firstKeyIndex, dspClock + 1, value + 1);
            Assert.AreNotEqual(DSPParameterKey.NullIndex, secondKeyIndex);
            Assert.AreNotEqual(firstKeyIndex, secondKeyIndex);
            Assert.AreEqual(secondKeyIndex, setup.Graph.GetLastParameterKeyIndex(firstKeyIndex));
        }
    }

    [Test]
    public void FreeKeys_ClearsKeys()
    {
        DSPNode node = default;
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            node = graphSetup.CreateDSPNode<SingleParameterKernel.Parameters, NoProviders, SingleParameterKernel>();
        }))
        {
            setup.PumpGraph();

            long dspClock = 42;
            float4 value = 10.0f;

            // Get copy of node with populated fields
            node = setup.Graph.LookupNode(node.Handle);
            var parameter = node.Parameters[(int)SingleParameterKernel.Parameters.Parameter];
            Assert.AreEqual(DSPParameterKey.NullIndex, setup.Graph.GetLastParameterKeyIndex(parameter.KeyIndex));

            var firstKeyIndex = setup.Graph.AppendKey(parameter.KeyIndex, DSPParameterKey.NullIndex, dspClock, value);
            var secondKeyIndex = setup.Graph.AppendKey(firstKeyIndex, firstKeyIndex, dspClock + 1, value + 1);
            Assert.AreEqual(secondKeyIndex, setup.Graph.GetLastParameterKeyIndex(firstKeyIndex));

            Assert.AreEqual(DSPParameterKey.NullIndex, setup.Graph.FreeParameterKeys(firstKeyIndex));
            Assert.AreEqual(DSPParameterKey.NullIndex, setup.Graph.GetLastParameterKeyIndex(firstKeyIndex));
        }
    }
}
