using System;
using UnityEngine.Experimental.Audio;
using NUnit.Framework;
using Unity.Collections;
using Unity.Audio;
using UnityEngine;
using UnityEngine.Video;

public class DSPSampleProviders
{
    const int kChannelCount  = 2;
    const int kSampleRate    = 1000;
    const int kDspBufferSize = 200;
    const float kSignalValueA  = 1.0F;
    const float kSignalValueB  = 0.5F;

    enum Providers
    {
        Single,
        [SampleProviderArray]
        VariableSizeArray,
        [SampleProviderArray(3)]
        FixedArray
    }

    struct TestData : IDisposable
    {
        public DSPGraph Graph;
        public DSPNode  Node;

        public void Dispose()
        {
            using (var block = Graph.CreateCommandBlock())
                block.ReleaseDSPNode(Node);
            Graph.Dispose();
        }
    }

    TestData CreateTestData<TKernel, TProviders>(Action<DSPCommandBlock, DSPNode> action = null)
        where TKernel : struct, IAudioKernel<NoParameters, TProviders>
        where TProviders : unmanaged, Enum
    {
        DSPNode node;
        var graph = DSPGraph.Create(SoundFormat.Stereo, kChannelCount, kDspBufferSize, kSampleRate);
        using (var block = graph.CreateCommandBlock())
        {
            node = block.CreateDSPNode<NoParameters, TProviders, TKernel>();
            block.AddOutletPort(node, 2, SoundFormat.Stereo);
            block.Connect(node, 0, graph.RootDSP, 0);
            action?.Invoke(block, node);
        }

        return new TestData {Graph = graph, Node = node};
    }

    void AddProviderToNode<TKernel>(DSPCommandBlock block, DSPNode node, int index = -1, float value = kSignalValueA, Providers item = Providers.VariableSizeArray, bool insert = true)
        where TKernel : struct, IAudioKernel<NoParameters, Providers>
    {
        var provider = AudioSampleProvider.Create(kChannelCount, kSampleRate);
        provider.enableSilencePadding = false;
        var inputBuff = new NativeArray<float>(2 * kChannelCount * kDspBufferSize, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        for (int i = 0; i < inputBuff.Length; ++i)
            inputBuff[i] = value;
        provider.QueueSampleFrames(inputBuff);
        inputBuff.Dispose();
        if (insert)
            block.InsertSampleProvider<NoParameters, Providers, TKernel>(provider.id, node, item, index);
        else
            block.SetSampleProvider<NoParameters, Providers, TKernel>(provider.id, node, item, index);
    }

    void CheckOutputBuffer(DSPGraph graph, Action<NativeArray<float>> check = null, DSPGraph.ExecutionMode executionMode = DSPGraph.ExecutionMode.Jobified)
    {
        using (var buffer = new NativeArray<float>(kChannelCount * kDspBufferSize, Allocator.Temp))
        {
            graph.BeginMix(0, executionMode);
            graph.ReadMix(buffer, kDspBufferSize, kChannelCount);
            check?.Invoke(buffer);
        }
    }

    void CheckBufferHasValue(NativeArray<float> buffer, float value)
    {
        float maxValue = float.NegativeInfinity;
        foreach (var v in buffer)
            maxValue = Math.Max(maxValue, v);

        Assert.That(maxValue, Is.EqualTo(value));
    }

    struct NoSampleProviderKernel : IAudioKernel<NoParameters, NoProviders>
    {
        public void Initialize() {}
        public void Execute(ref ExecuteContext<NoParameters, NoProviders> context)
        {
            Assert.That(context.Providers.Count, Is.EqualTo(0));
            var providers = context.Providers;
            Assert.Throws<ArgumentOutOfRangeException>(() => providers.GetSampleProvider(0));
        }

        public void Dispose() {}
    }

    [Test]
    public void EmptyNode_HasNoSampleProvider()
    {
        using (var testData = CreateTestData<NoSampleProviderKernel, NoProviders>())
            CheckOutputBuffer(testData.Graph);
    }

    struct InitialSampleProviderKernel : IAudioKernel<NoParameters, Providers>
    {
        public void Initialize() {}
        public void Execute(ref ExecuteContext<NoParameters, Providers> context)
        {
            var providers = context.Providers;
            Assert.That(providers.Count, Is.EqualTo(Enum.GetNames(typeof(Providers)).Length));

            // First is single value, so check its 0th element can be accessed.
            SampleProvider singleProv = providers.GetSampleProvider(Providers.Single);
            Assert.That(singleProv.Valid, Is.False);
            Assert.That(providers.GetCount(Providers.Single), Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => providers.GetSampleProvider(Providers.Single, 1));

            // Second item is a var array. These are born empty.
            Assert.Throws<ArgumentOutOfRangeException>(() => providers.GetSampleProvider(Providers.VariableSizeArray));
            Assert.That(providers.GetCount(Providers.VariableSizeArray), Is.EqualTo(0));

            // Third item is a fixed array. These are born with their final size.
            Assert.That(providers.GetCount(Providers.FixedArray), Is.EqualTo(3));
            for (int i = 0; i < 3; ++i)
            {
                SampleProvider p = providers.GetSampleProvider(Providers.FixedArray, 0);
                Assert.That(p.Valid, Is.False);
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => providers.GetSampleProvider(Providers.FixedArray, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => providers.GetSampleProvider(3));
        }

        public void Dispose() {}
    }

    [Test]
    public void NodeWithSchema_HasAccessibleButInvalidSampleProviders()
    {
        using (var testData = CreateTestData<InitialSampleProviderKernel, Providers>())
            CheckOutputBuffer(testData.Graph);
    }

    struct InaccessibleLocalSampleProviderKernel : IAudioKernel<NoParameters, NoProviders>
    {
        public void Initialize() {}
        public void Execute(ref ExecuteContext<NoParameters, NoProviders> context)
        {
            var localArray = new SampleProviderContainer<NoProviders>();
            Assert.That(localArray.Count, Is.EqualTo(0));

            var localProvider = new SampleProvider();
            Assert.That(localProvider.Valid, Is.False);
            var outputs = context.Outputs;
            Assert.Throws<InvalidOperationException>(() => localProvider.Read(outputs.GetSampleBuffer(0).Buffer.Slice()));
        }

        public void Dispose() {}
    }

    [Test]
    public void LocalProvider_CannotBeUsed()
    {
        using (var testData = CreateTestData<InaccessibleLocalSampleProviderKernel, NoProviders>())
            CheckOutputBuffer(testData.Graph);
    }

    struct SingleSampleProviderKernel : IAudioKernel<NoParameters, Providers>
    {
        public void Initialize() {}
        public void Execute(ref ExecuteContext<NoParameters, Providers> context)
        {
            var provider = context.Providers.GetSampleProvider(Providers.Single);
            Assert.That(provider.Valid, Is.True);
            Assert.That(provider.ChannelCount, Is.EqualTo(kChannelCount));
            Assert.That(provider.SampleRate, Is.EqualTo(kSampleRate));
        }

        public void Dispose() {}
    }

    [Test]
    public void ProviderFields_MatchOriginalProvider()
    {
        using (CreateTestData<InitialSampleProviderKernel, Providers>(
            (block, node) =>
            {
                AddProviderToNode<SingleSampleProviderKernel>(block, node, 0, kSignalValueA, Providers.Single, false);
            }))
        {
        }
    }

    [Test]
    public void FixedProviderArray_CannotBeExtended()
    {
        using (CreateTestData<InitialSampleProviderKernel, Providers>(
            (block, node) =>
            {
                Assert.Throws<InvalidOperationException>(() =>
                    AddProviderToNode<InitialSampleProviderKernel>(block, node, -1, kSignalValueA, Providers.FixedArray, true));
            }))
        {
        }
    }

    [Test]
    public void FixedProviderArray_CannotBeShortened()
    {
        using (var testData = CreateTestData<InitialSampleProviderKernel, Providers>(
            (block, node) =>
            {
                Assert.Throws<InvalidOperationException>(() =>
                    block.RemoveSampleProvider<NoParameters, Providers, InitialSampleProviderKernel>(node, Providers.FixedArray, 0));
            }))
        {
            using (testData.Graph.CreateCommandBlock()) {}
        }
    }

    struct FixedArraySampleProviderKernel : IAudioKernel<NoParameters, Providers>
    {
        public void Initialize() {}
        public void Execute(ref ExecuteContext<NoParameters, Providers> context)
        {
            var provider = context.Providers.GetSampleProvider(Providers.FixedArray);
            Assert.That(provider.Valid, Is.True);
            Assert.That(provider.ChannelCount, Is.EqualTo(kChannelCount));
            Assert.That(provider.SampleRate, Is.EqualTo(kSampleRate));
        }

        public void Dispose() {}
    }

    [Test]
    public void FixedProviderArray_CanReceiveNewProvider()
    {
        using (CreateTestData<FixedArraySampleProviderKernel, Providers>(
            (block, node) =>
            {
                AddProviderToNode<SingleSampleProviderKernel>(block, node, 0, kSignalValueA, Providers.FixedArray, false);
            }))
        {
        }
    }

    [Test]
    public void FixedProviderArray_CanClearProvider()
    {
        using (var testData = CreateTestData<FixedArraySampleProviderKernel, Providers>(
            (block, node) =>
            {
                AddProviderToNode<SingleSampleProviderKernel>(block, node, 0, kSignalValueA, Providers.FixedArray, false);
            }))
        {
            using (var block = testData.Graph.CreateCommandBlock())
                block.SetSampleProvider<NoParameters, Providers, InitialSampleProviderKernel>(0, testData.Node, Providers.FixedArray, 0);
        }
    }

    struct OneSampleProviderKernel : IAudioKernel<NoParameters, Providers>
    {
        public void Initialize() {}
        public void Execute(ref ExecuteContext<NoParameters, Providers> context)
        {
            Assert.That(context.Providers.GetCount(Providers.VariableSizeArray), Is.EqualTo(1));
            var provider = context.Providers.GetSampleProvider(Providers.VariableSizeArray, 0);
            var providers = context.Providers;
            Assert.Throws<ArgumentOutOfRangeException>(() => providers.GetSampleProvider(Providers.VariableSizeArray, 1));
            Assert.That(provider.Valid, Is.True);
            Assert.That(provider.NativeFormat, Is.EqualTo(SampleProvider.NativeFormatType.FLOAT_LE));
        }

        public void Dispose() {}
    }

    [Test]
    public void VariableProviderArray_CanBeExtended()
    {
        using (var testData = CreateTestData<OneSampleProviderKernel, Providers>(
            (block, node) => AddProviderToNode<OneSampleProviderKernel>(block, node)))
            CheckOutputBuffer(testData.Graph);
    }

    [Test]
    public void RemoveProvider_MakesVariableSampleProviderArrayEmpty()
    {
        using (var testData = CreateTestData<InitialSampleProviderKernel, Providers>(
            (block, node) => AddProviderToNode<InitialSampleProviderKernel>(block, node)))
        {
            using (var block = testData.Graph.CreateCommandBlock())
                block.RemoveSampleProvider<NoParameters, Providers, InitialSampleProviderKernel>(testData.Node, Providers.VariableSizeArray, 0);
            CheckOutputBuffer(testData.Graph);
        }
    }

    struct ReleaseSampleProviderKernel : IAudioKernel<NoParameters, Providers>
    {
        bool m_WasCalledOnce;

        public void Initialize() {}
        public void Execute(ref ExecuteContext<NoParameters, Providers> context)
        {
            var provider = context.Providers.GetSampleProvider(Providers.VariableSizeArray, 0);

            if (!m_WasCalledOnce)
            {
                Assert.That(provider.Valid, Is.True);
                provider.Release();

                // Array length stays the same, but provider becomes unusable.
                Assert.That(context.Providers.GetCount(Providers.VariableSizeArray), Is.EqualTo(1));
                Assert.That(provider.Valid, Is.False);
                var sameProvider = context.Providers.GetSampleProvider(Providers.VariableSizeArray, 0);
                Assert.That(sameProvider.Valid, Is.False);
                m_WasCalledOnce = true;
            }
            else
            {
                Assert.That(provider.Valid, Is.False);
                Assert.Throws<InvalidOperationException>(() => provider.Release());
            }
        }

        public void Dispose() {}
    }

    [Test]
    public void ReleaseProvider_MakesSampleProviderInvalid()
    {
        using (var testData = CreateTestData<ReleaseSampleProviderKernel, Providers>(
            (block, node) => AddProviderToNode<ReleaseSampleProviderKernel>(block, node)))
        {
            // Check once for the Release
            CheckOutputBuffer(testData.Graph);
            // Check once to check how the provider appears on subsequent calls.
            CheckOutputBuffer(testData.Graph);
        }
    }

    struct ReadSampleProviderKernel : IAudioKernel<NoParameters, Providers>
    {
        public void Initialize() {}
        public void Execute(ref ExecuteContext<NoParameters, Providers> context)
        {
            context.Providers.GetSampleProvider(Providers.VariableSizeArray, 0).Read(context.Outputs.GetSampleBuffer(0).Buffer.Slice());
        }

        public void Dispose() {}
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void ReadSampleProvider_PlaysSignal(DSPGraph.ExecutionMode executionMode)
    {
        using (var testData = CreateTestData<ReadSampleProviderKernel, Providers>(
            (block, node) => AddProviderToNode<ReadSampleProviderKernel>(block, node)))
        {
            CheckOutputBuffer(testData.Graph, (buffer) => CheckBufferHasValue(buffer, kSignalValueA), executionMode);
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void ReadOverwrittenSampleProvider_PlaysNewSignal(DSPGraph.ExecutionMode executionMode)
    {
        using (var testData = CreateTestData<ReadSampleProviderKernel, Providers>(
            (block, node) => AddProviderToNode<ReadSampleProviderKernel>(block, node)))
        {
            using (var block = testData.Graph.CreateCommandBlock())
                AddProviderToNode<ReadSampleProviderKernel>(block, testData.Node, 0, kSignalValueB);
            CheckOutputBuffer(testData.Graph, (buffer) => CheckBufferHasValue(buffer, kSignalValueB), executionMode);
        }
    }

    struct AddSampleProvidersKernel : IAudioKernel<NoParameters, Providers>
    {
        public void Initialize() {}
        public void Execute(ref ExecuteContext<NoParameters, Providers> context)
        {
            NativeArray<float> outputBuffer = context.Outputs.GetSampleBuffer(0).Buffer;
            ref SampleProviderContainer<Providers> providers = ref context.Providers;
            providers.GetSampleProvider(Providers.VariableSizeArray, 0).Read(outputBuffer.Slice());
            var count = context.Providers.GetCount(Providers.VariableSizeArray);
            using (var tempBuffer =
                       new NativeArray<float>(outputBuffer.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory))
            {
                for (int i = 1; i < count; ++i)
                {
                    var provider = providers.GetSampleProvider(Providers.VariableSizeArray, i);
                    provider.Read(tempBuffer.Slice());
                }

                for (int i = 0; i < outputBuffer.Length; ++i)
                    outputBuffer[i] += tempBuffer[i];
            }
        }

        public void Dispose() {}
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void AddSampleProviders_PlaysAddedSignals(DSPGraph.ExecutionMode executionMode)
    {
        using (var testData = CreateTestData<AddSampleProvidersKernel, Providers>(
            (block, node) => AddProviderToNode<AddSampleProvidersKernel>(block, node)))
        {
            using (var block = testData.Graph.CreateCommandBlock())
                AddProviderToNode<AddSampleProvidersKernel>(block, testData.Node, -1, kSignalValueB);
            CheckOutputBuffer(testData.Graph, (buffer) => CheckBufferHasValue(buffer, kSignalValueA + kSignalValueB), executionMode);
        }
    }

    [Test]
    public void ClearProviderViaAudioClip()
    {
        using (var testData = CreateTestData<AddSampleProvidersKernel, Providers>())
            using (var block = testData.Graph.CreateCommandBlock())
                block.SetSampleProvider<NoParameters, Providers, AddSampleProvidersKernel>((AudioClip)null, testData.Node, Providers.Single);
    }

    [Test]
    public void ClearProviderViaVideo()
    {
        using (var testData = CreateTestData<AddSampleProvidersKernel, Providers>())
            using (var block = testData.Graph.CreateCommandBlock())
                block.SetSampleProvider<NoParameters, Providers, AddSampleProvidersKernel>((VideoPlayer)null, testData.Node, Providers.Single);
    }
}
