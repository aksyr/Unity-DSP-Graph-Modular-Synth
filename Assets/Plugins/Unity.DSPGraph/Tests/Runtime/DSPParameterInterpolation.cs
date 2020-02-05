using NUnit.Framework;
using Unity.Audio;
using Unity.Mathematics;

public class DSPParameterInterpolation
{
    [Test]
    public unsafe void InterpolatesEmptyRange()
    {
        DSPNode node = default;
        using (var setup = new GraphSetup((graphSetup, graph, block) =>
        {
            node = graphSetup.CreateDSPNode<SingleParameterKernel.Parameters, NoProviders, SingleParameterKernel>();
        }))
        {
            setup.PumpGraph();

            long dspClock = 0;
            float4 value = 10.0f;

            // Get copy of node with populated fields
            node = setup.Graph.LookupNode(node.Handle);
            var nodeParameters = node.Parameters;
            var parameter = nodeParameters[(int)SingleParameterKernel.Parameters.Parameter];
            var newKeyIndex = setup.Graph.AppendKey(parameter.KeyIndex, DSPParameterKey.NullIndex, dspClock, value);

            for (int sampleOffset = 0; sampleOffset < 10; ++sampleOffset)
                Assert.AreEqual(value[0], DSPParameterInterpolator.Generate(sampleOffset, setup.Graph.ParameterKeys.UnsafeDataPointer, newKeyIndex, dspClock, float.MinValue, float.MaxValue, value)[0], 0.001f);
        }
    }
}
