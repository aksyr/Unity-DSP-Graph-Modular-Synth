using NUnit.Framework;
using System;
using System.Linq;
using Unity.Audio;
using Unity.Collections;

public class DSPGraphConnection
{
    struct NodeData
    {
        public DSPNode Node;
        public DSPConnection Connection;
    }

    struct TestData : IDisposable
    {
        public DSPGraph Graph;
        public DSPNode Node;
        public DSPConnection Connection;
        public const int DefaultBufferSize = 100;
        public const int DefaultChannelCount = 1;
        public const SoundFormat DefaultSoundFormat = SoundFormat.Mono;
        public DSPGraph.ExecutionMode ExecutionMode;

        public static TestData Create(bool createGenerator = true, int channelCount = DefaultChannelCount, SoundFormat soundFormat = DefaultSoundFormat, DSPGraph.ExecutionMode executionMode = DSPGraph.ExecutionMode.Jobified)
        {
            var graph = DSPGraph.Create(soundFormat, channelCount, DefaultBufferSize, 1000);
            NodeData nodeData;
            using (var block = graph.CreateCommandBlock())
                nodeData = createGenerator ? CreateGeneratorNode(block, graph.RootDSP, channelCount, soundFormat) : CreatePassthroughNode(block, graph, channelCount, soundFormat);
            return new TestData
            {
                Graph = graph,
                Node = nodeData.Node,
                Connection = nodeData.Connection,
                ExecutionMode = executionMode,
            };
        }

        public static NodeData CreateGeneratorNode(DSPCommandBlock block, DSPNode consumer, int channelCount, SoundFormat soundFormat)
        {
            var node = block.CreateDSPNode<NoParameters, NoProviders, GenerateOne>();
            block.AddOutletPort(node, channelCount, soundFormat);
            var connection = block.Connect(node, 0, consumer, 0);
            return new NodeData
            {
                Node = node,
                Connection = connection
            };
        }

        public static NodeData CreatePassthroughNode(DSPCommandBlock block, DSPGraph graph, int channelCount, SoundFormat soundFormat)
        {
            var node = block.CreateDSPNode<NoParameters, NoProviders, PassThrough>();
            block.AddInletPort(node, channelCount, soundFormat);
            block.AddOutletPort(node, channelCount, soundFormat);
            var connection = block.Connect(node, 0, graph.RootDSP, 0);
            return new NodeData
            {
                Node = node,
                Connection = connection
            };
        }

        public NodeData CreateGeneratorNode(DSPNode consumer, int channelCount = DefaultChannelCount, SoundFormat soundFormat = DefaultSoundFormat)
        {
            using (var block = Graph.CreateCommandBlock())
                return CreateGeneratorNode(block, consumer, channelCount, soundFormat);
        }

        public NodeData CreateGeneratorNode(int channelCount = DefaultChannelCount, SoundFormat soundFormat = DefaultSoundFormat)
        {
            return CreateGeneratorNode(Graph.RootDSP, channelCount, soundFormat);
        }

        public void Mix(NativeArray<float> buff, int frameCount, int channelCount)
        {
            Graph.BeginMix(frameCount, ExecutionMode);
            Graph.ReadMix(buff, frameCount, channelCount);
        }

        public void CheckMix(params float[] expectedValue)
        {
            var channelCount = expectedValue.Length;
            using (var buff = new NativeArray<float>(DefaultBufferSize * channelCount, Allocator.Temp))
            {
                var sampleFrameCount = buff.Length / channelCount;
                Mix(buff, sampleFrameCount, channelCount);
                for (int i = 0; i < sampleFrameCount; ++i)
                {
                    for (int c = 0; c < channelCount; ++c)
                        Assert.That(buff[i * channelCount + c], Is.EqualTo(expectedValue[c]));
                }
            }
        }

        public void Attenuate(DSPConnection conn, int interpolationLength, params float[] value)
        {
            using (var block = Graph.CreateCommandBlock())
            {
                unsafe
                {
                    fixed(float* valuePtr = value)
                    {
                        block.SetAttenuation(conn, valuePtr, (byte)value.Length, interpolationLength);
                    }
                }
            }
        }

        public void Attenuate(DSPConnection conn, float value, int interpolationLength = 0)
        {
            using (var block = Graph.CreateCommandBlock())
                block.SetAttenuation(conn, value, interpolationLength);
        }

        public void RemoveNode(DSPNode nodeToRemove)
        {
            using (var block = Graph.CreateCommandBlock())
                block.ReleaseDSPNode(nodeToRemove);
        }

        public void Dispose()
        {
            RemoveNode(Node);
            Graph.Dispose();
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void ConnectNodes_ProducesResults_InInputPorts(DSPGraph.ExecutionMode executionMode)
    {
        using (var testData = TestData.Create(true, TestData.DefaultChannelCount, TestData.DefaultSoundFormat, executionMode))
            testData.CheckMix(1.0F);
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void AttenuatedInput_ProducesAttenuatedResults_InInputPorts(DSPGraph.ExecutionMode executionMode)
    {
        using (var testData = TestData.Create(true, TestData.DefaultChannelCount, TestData.DefaultSoundFormat, executionMode))
        {
            var attenuation = 0.5F;
            testData.Attenuate(testData.Connection, attenuation);
            testData.CheckMix(attenuation);
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void AttenuatedInput_ProducesMultiChannelAttenuatedResults_InInputPorts(DSPGraph.ExecutionMode executionMode)
    {
        using (var testData = TestData.Create(true, 2, SoundFormat.Stereo, executionMode))
        {
            var attenuationLeft = 0.3F;
            var attenuationRight = 0.7F;
            testData.Attenuate(testData.Connection, 0, attenuationLeft, attenuationRight);
            testData.CheckMix(attenuationLeft, attenuationRight);
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void AttenuatedInterpolatedInput_ProducesInterpolatedResults_InInputPorts(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        using (var testData = TestData.Create(true, channelCount, soundFormat, executionMode))
        {
            var attenuation = 0.0F;
            testData.Attenuate(testData.Connection, attenuation, TestData.DefaultBufferSize);
            using (var buff = new NativeArray<float>(TestData.DefaultBufferSize * channelCount, Allocator.Temp))
            {
                testData.Mix(buff, buff.Length / channelCount, channelCount);
                VerifyInterpolatedAttenuation(buff, 0, TestData.DefaultBufferSize, channelCount,
                    new[]{ DSPConnection.DefaultAttenuation, DSPConnection.DefaultAttenuation },
                    new []
                    {
                        -1.0f / (TestData.DefaultBufferSize - 1),
                        -1.0f / (TestData.DefaultBufferSize - 1),
                    });
            }
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void AttenuatedInterpolatedInput_ProducesMultiChannelInterpolatedResults_InInputPorts(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        using (var testData = TestData.Create(true, channelCount, soundFormat, executionMode))
        {
            var attenuationLeft = 0.0F;
            var attenuationRight = 0.5F;
            testData.Attenuate(testData.Connection, TestData.DefaultBufferSize, attenuationLeft, attenuationRight);
            using (var buff = new NativeArray<float>(TestData.DefaultBufferSize * channelCount, Allocator.Temp))
            {
                testData.Mix(buff, buff.Length / channelCount, channelCount);
                VerifyInterpolatedAttenuation(buff, 0, TestData.DefaultBufferSize, channelCount,
                    new[]{ DSPConnection.DefaultAttenuation, DSPConnection.DefaultAttenuation },
                    new []
                    {
                        -(1 - attenuationLeft) / (TestData.DefaultBufferSize - 1),
                        -(1 - attenuationRight) / (TestData.DefaultBufferSize - 1),
                    });
            }
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void KeyedAttenuation_ProducesInterpolatedResults_InInputPorts(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        var baseAttenuation = new[]{ 0.0f, 0.0f };
        using (var testData = TestData.Create(true, channelCount, soundFormat, executionMode))
        {
            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.SetAttenuation(testData.Connection, baseAttenuation[0]);
                block.AddAttenuationKey(testData.Connection, 49, 100.0f);
                block.AddAttenuationKey(testData.Connection, 99, 0.0f);
            }

            using (var buff = new NativeArray<float>(TestData.DefaultBufferSize * channelCount, Allocator.Temp))
            {
                testData.Mix(buff, buff.Length / channelCount, channelCount);
                VerifyInterpolatedAttenuation(buff, 0, 50, channelCount,
                    baseAttenuation,
                    new []
                    {
                        100.0f / 49,
                        100.0f / 49,
                    });
                VerifyInterpolatedAttenuation(buff, 50, 50, channelCount,
                    new[]
                    {
                        100.0f,
                        100.0f,
                    },
                    new []
                    {
                        -100.0f / 99,
                        -100.0f / 99,
                    });
            }
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void SustainedAttenuation_ProducesSameAttenuation_InInputPorts(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        var baseAttenuation = new[]{ 0.0f, 0.0f };
        using (var testData = TestData.Create(true, channelCount, soundFormat, executionMode))
        {
            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.SetAttenuation(testData.Connection, baseAttenuation[0]);
                block.AddAttenuationKey(testData.Connection, 49, 100.0f);
                block.SustainAttenuation(testData.Connection, 99);
            }

            using (var buff = new NativeArray<float>(TestData.DefaultBufferSize * channelCount, Allocator.Temp))
            {
                testData.Mix(buff, buff.Length / channelCount, channelCount);
                VerifyInterpolatedAttenuation(buff, 0, 50, channelCount,
                    baseAttenuation,
                    new []
                    {
                        100.0f / 49,
                        100.0f / 49,
                    });
                VerifyInterpolatedAttenuation(buff, 50, 50, channelCount,
                    new[]
                    {
                        100.0f,
                        100.0f,
                    },
                    new []
                    {
                        0.0f,
                        0.0f,
                    });
            }
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void MultiAttenuatedInput_ProducesSummedAttenuatedResults_InInputPorts(DSPGraph.ExecutionMode executionMode)
    {
        using (var testData = TestData.Create(true, TestData.DefaultChannelCount, TestData.DefaultSoundFormat, executionMode))
        {
            var attenuation1 = 0.5F;
            testData.Attenuate(testData.Connection, attenuation1);
            var nodeData = testData.CreateGeneratorNode();
            var attenuation2 = 0.25F;
            testData.Attenuate(nodeData.Connection, attenuation2);
            testData.CheckMix(attenuation1 + attenuation2);
            testData.RemoveNode(nodeData.Node);
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void MultiNoopInput_ProducesSummedAttenuatedResults_InInputPorts(DSPGraph.ExecutionMode executionMode)
    {
        // This test covers buffer stealing when inputs are noop (i.e. having a 1.0 non-animated attenuation).
        // Because buffer stealing is only applicable to inputs that don't belong to the root node, we
        // use an intermediate pass-through node (hence passing false to Create in order for it to not create the
        // generator that it normally creates and create a PassThrough instead).
        using (var testData = TestData.Create(false, TestData.DefaultChannelCount, TestData.DefaultSoundFormat, executionMode))
        {
            var generator1 = testData.CreateGeneratorNode(testData.Node);
            var attenuation1 = 1.0F;
            testData.Attenuate(generator1.Connection, attenuation1);
            var generator2 = testData.CreateGeneratorNode(testData.Node);
            var attenuation2 = 0.25F;
            testData.Attenuate(generator2.Connection, attenuation2);
            testData.CheckMix(attenuation1 + attenuation2);
            testData.RemoveNode(generator1.Node);
            testData.RemoveNode(generator2.Node);
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void InterruptedAttenuation_ContinuesWithAttenuatedValue(DSPGraph.ExecutionMode executionMode)
    {
        var channelCount = 2;
        var soundFormat = SoundFormat.Stereo;
        using (var testData = TestData.Create(true, channelCount, soundFormat, executionMode))
        {
            var attenuation = 0.0f;
            var attenuationLength = 2 * TestData.DefaultBufferSize;
            testData.Attenuate(testData.Connection, attenuation, attenuationLength);
            using (var buff = new NativeArray<float>(TestData.DefaultBufferSize * channelCount, Allocator.Temp))
            {
                float[] baseAttenuationValue = { DSPConnection.DefaultAttenuation, DSPConnection.DefaultAttenuation };
                float[] attenuationDelta = { (-1 / ((float)(TestData.DefaultBufferSize * 2) - 1)), (-1 / ((float)(TestData.DefaultBufferSize * 2) - 1)) };

                testData.Mix(buff, buff.Length / channelCount, channelCount);
                VerifyInterpolatedAttenuation(buff, 0, TestData.DefaultBufferSize, channelCount, baseAttenuationValue, attenuationDelta);

                // Lookup updated connection from graph
                var connection = testData.Graph.LookupConnection(testData.Connection.Handle);
                baseAttenuationValue = new[]{ connection.Attenuation.Value[0], connection.Attenuation.Value[1] };
                Assert.AreEqual(buff[(TestData.DefaultBufferSize * channelCount) - 1], baseAttenuationValue[0], 0.0001);

                testData.Attenuate(testData.Connection, DSPConnection.DefaultAttenuation, TestData.DefaultBufferSize);
                testData.Mix(buff, buff.Length / channelCount, channelCount);
                attenuationDelta = new[]
                {
                    (DSPConnection.DefaultAttenuation - baseAttenuationValue[0]) / (TestData.DefaultBufferSize - 1),
                    (DSPConnection.DefaultAttenuation - baseAttenuationValue[1]) / (TestData.DefaultBufferSize - 1),
                };
                VerifyInterpolatedAttenuation(buff, 0, TestData.DefaultBufferSize, channelCount, baseAttenuationValue, attenuationDelta);

                connection = testData.Graph.LookupConnection(testData.Connection.Handle);
                Assert.AreEqual(DSPConnection.DefaultAttenuation, connection.Attenuation.Value[0]);
            }
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void DisconnectionByHandle_ProducesDefaultSamples_InInputPorts(DSPGraph.ExecutionMode executionMode)
    {
        using (var testData = TestData.Create(true, TestData.DefaultChannelCount, TestData.DefaultSoundFormat, executionMode))
        {
            var channelCount = 1;
            var attenuation1 = 0.5F;
            testData.Attenuate(testData.Connection, attenuation1);
            var nodeData = testData.CreateGeneratorNode();
            var attenuation2 = 0.25F;
            testData.Attenuate(nodeData.Connection, attenuation2);
            using (var buff = new NativeArray<float>(TestData.DefaultBufferSize * channelCount, Allocator.Temp))
                testData.Mix(buff, buff.Length / channelCount, channelCount);

            using (var block = testData.Graph.CreateCommandBlock())
                block.Disconnect(testData.Connection);

            testData.CheckMix(attenuation2);

            using (var block = testData.Graph.CreateCommandBlock())
                block.Disconnect(nodeData.Connection);

            testData.CheckMix(0.0F);

            testData.RemoveNode(nodeData.Node);
        }
    }

    [Test]
    public void Completing_CanceledCommandBlock_DoesNotWarn()
    {
        using (var testData = TestData.Create())
        using (var block = testData.Graph.CreateCommandBlock())
            block.Cancel();
        // Complete should have no effect (but also should not produce errors)
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified | DSPGraph.ExecutionMode.ExecuteNodesWithNoOutputs)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous | DSPGraph.ExecutionMode.ExecuteNodesWithNoOutputs)]
    public void UnattachedSubtreesAreExecutedWithModeEnabled(DSPGraph.ExecutionMode executionMode)
    {
        using (var testData = TestData.Create(false, TestData.DefaultChannelCount, TestData.DefaultSoundFormat, executionMode))
        {
            DSPNode unattachedNode;
            using (var block = testData.Graph.CreateCommandBlock())
            {
                // Create a node that doesn't have an output path to the root of the graph
                unattachedNode = block.CreateDSPNode<NoParameters, NoProviders, LifecycleTracking>();
                block.AddOutletPort(testData.Node, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(unattachedNode, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddOutletPort(unattachedNode, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.Connect(testData.Node, 1, unattachedNode, 0);
            }
            testData.CheckMix(0.0F);

            // The lifecycle node should be executing
            Assert.AreEqual(LifecycleTracking.LifecyclePhase.Executing, LifecycleTracking.Phase);

            using (var block = testData.Graph.CreateCommandBlock())
                block.ReleaseDSPNode(unattachedNode);
        }
    }

    [Test]
    [TestCase(DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous)]
    public void UnattachedSubtreesAreNotExecutedWithModeDisabled(DSPGraph.ExecutionMode executionMode)
    {
        using (var testData = TestData.Create(false, TestData.DefaultChannelCount, TestData.DefaultSoundFormat, executionMode))
        {
            DSPNode unattachedNode;
            using (var block = testData.Graph.CreateCommandBlock())
            {
                // Create a node that doesn't have an output path to the root of the graph
                unattachedNode = block.CreateDSPNode<NoParameters, NoProviders, LifecycleTracking>();
                block.AddOutletPort(testData.Node, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(unattachedNode, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddOutletPort(unattachedNode, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.Connect(testData.Node, 1, unattachedNode, 0);
            }
            testData.CheckMix(0.0F);

            // The lifecycle node should not be executing
            Assert.AreEqual(LifecycleTracking.LifecyclePhase.Initialized, LifecycleTracking.Phase);

            using (var block = testData.Graph.CreateCommandBlock())
                block.ReleaseDSPNode(unattachedNode);
        }
    }

    [Test]
    [TestCase((DSPGraph.ExecutionMode) 0)]
    [TestCase(DSPGraph.ExecutionMode.Synchronous | DSPGraph.ExecutionMode.Jobified)]
    [TestCase(DSPGraph.ExecutionMode.ExecuteNodesWithNoOutputs)]
    public void InvalidExecutionMode_Throws(DSPGraph.ExecutionMode executionMode)
    {
        using (var testData = TestData.Create(false, TestData.DefaultChannelCount, TestData.DefaultSoundFormat, executionMode))
        {
            Assert.Throws<ArgumentException>(() => testData.CheckMix(0.0F));
        }
    }

    [Test]
    public void ConnectDisconnectClearsState()
    {
        using (var testData = TestData.Create(false))
        {
            DSPNode source;
            DSPNode destination;
            using (var block = testData.Graph.CreateCommandBlock())
            {
                source = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(source, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                destination = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddInletPort(destination, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                var connection = block.Connect(source, 0, destination, 0);
                block.Disconnect(connection);
            }
            testData.CheckMix(0.0f);

            // Need to look up actual nodes from graph here to access real members
            source = testData.Graph.LookupNode(source.Handle);
            destination = testData.Graph.LookupNode(destination.Handle);

            Assert.AreEqual(DSPConnection.InvalidIndex, source.OutputConnectionIndex);
            Assert.AreEqual(DSPConnection.InvalidIndex, destination.InputConnectionIndex);

            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.ReleaseDSPNode(source);
                block.ReleaseDSPNode(destination);
            }
        }
    }

    [Test]
    public void DoubleConnectThrowsException()
    {
        using (var testData = TestData.Create(false))
        {
            DSPNode source;
            DSPNode destination;
            using (var block = testData.Graph.CreateCommandBlock())
            {
                source = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(source, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                destination = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddInletPort(destination, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                block.Connect(source, 0, destination, 0);
                block.Connect(source, 0, destination, 0);
            }
            Assert.Throws<InvalidOperationException>(() => testData.CheckMix(0.0f));

            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.ReleaseDSPNode(source);
                block.ReleaseDSPNode(destination);
            }
        }
    }

    [Test]
    public void IncompatiblePortsThrowsException()
    {
        using (var testData = TestData.Create(false))
        {
            DSPNode source;
            DSPNode destination;
            using (var block = testData.Graph.CreateCommandBlock())
            {
                source = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(source, 1, SoundFormat.Mono);

                destination = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddInletPort(destination, 4, SoundFormat.Quad);

                block.Connect(source, 0, destination, 0);
            }
            Assert.Throws<InvalidOperationException>(() => testData.CheckMix(0.0f));

            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.ReleaseDSPNode(source);
                block.ReleaseDSPNode(destination);
            }
        }
    }

    [Test]
    public void CreatingDirectCycleThrowsException()
    {
        using (var testData = TestData.Create(false))
        {
            DSPNode source;
            DSPNode destination;
            using (var block = testData.Graph.CreateCommandBlock())
            {
                source = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(source, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(source, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                destination = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(destination, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(destination, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                block.Connect(source, 0, destination, 0);
                block.Connect(destination, 0, source, 0);
            }
            Assert.Throws<InvalidOperationException>(() => testData.CheckMix(0.0f));

            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.ReleaseDSPNode(source);
                block.ReleaseDSPNode(destination);
            }
        }
    }

    [Test]
    public void CreatingIndirectCycleThrowsException()
    {
        using (var testData = TestData.Create(false))
        {
            DSPNode a;
            DSPNode b;
            DSPNode c;
            DSPNode d;
            using (var block = testData.Graph.CreateCommandBlock())
            {
                a = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                b = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                c = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                d = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                block.Connect(a, 0, b, 0);
                block.Connect(b, 0, c, 0);
                block.Connect(c, 0, d, 0);
                block.Connect(d, 0, a, 0);
            }
            Assert.Throws<InvalidOperationException>(() => testData.CheckMix(0.0f));

            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.ReleaseDSPNode(a);
                block.ReleaseDSPNode(b);
                block.ReleaseDSPNode(c);
                block.ReleaseDSPNode(d);
            }
        }
    }

    [Test]
    public void SidechainConnectionSucceeds()
    {
        using (var testData = TestData.Create(false))
        {
            DSPNode a;
            DSPNode b;
            DSPNode c;
            DSPNode d;
            using (var block = testData.Graph.CreateCommandBlock())
            {
                a = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                b = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                c = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                d = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                block.Connect(b, 0, a, 0);
                block.Connect(c, 0, b, 0);
                block.Connect(d, 0, c, 0);
                block.Connect(c, 0, a, 0);
                block.Connect(d, 0, a, 0);
            }

            testData.CheckMix(0.0f);

            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.ReleaseDSPNode(a);
                block.ReleaseDSPNode(b);
                block.ReleaseDSPNode(c);
                block.ReleaseDSPNode(d);
            }
        }
    }

    [Test]
    public void FindConnectionSucceeds()
    {
        using (var testData = TestData.Create(false))
        {
            DSPNode a;
            DSPNode b;
            DSPNode c;
            DSPNode d;
            DSPConnection connection;
            using (var block = testData.Graph.CreateCommandBlock())
            {
                a = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                b = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                c = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                d = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                block.Connect(b, 0, a, 0);
                block.Connect(c, 0, b, 0);
                block.Connect(d, 0, c, 0);
                connection = block.Connect(c, 0, a, 0);
                block.Connect(d, 0, a, 0);
            }

            testData.CheckMix(0.0f);
            var connectionIndex = testData.Graph.FindConnectionIndex(c.Handle.Id, 0, a.Handle.Id, 0);
            Assert.AreNotEqual(DSPConnection.InvalidIndex, connectionIndex);
            Assert.AreEqual(connection.Handle.Id, connectionIndex);

            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.ReleaseDSPNode(a);
                block.ReleaseDSPNode(b);
                block.ReleaseDSPNode(c);
                block.ReleaseDSPNode(d);
            }
        }
    }

    [Test]
    public void FindConnection_WithWrongPort_Fails()
    {
        using (var testData = TestData.Create(false))
        {
            DSPNode a;
            DSPNode b;
            DSPNode c;
            DSPNode d;
            DSPConnection connectionCB;
            DSPConnection connectionCA;
            using (var block = testData.Graph.CreateCommandBlock())
            {
                a = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                b = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                c = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddOutletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                d = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                block.Connect(b, 0, a, 0);
                connectionCB = block.Connect(c, 0, b, 1);
                block.Connect(d, 0, c, 0);
                connectionCA = block.Connect(c, 1, a, 0);
                block.Connect(d, 0, a, 0);
            }

            testData.CheckMix(0.0f);
            var connectionIndex = testData.Graph.FindConnectionIndex(c.Handle.Id, 0, b.Handle.Id, 1);
            Assert.AreNotEqual(DSPConnection.InvalidIndex, connectionIndex);
            Assert.AreEqual(connectionCB.Handle.Id, connectionIndex);

            connectionIndex = testData.Graph.FindConnectionIndex(c.Handle.Id, 1, a.Handle.Id, 0);
            Assert.AreNotEqual(DSPConnection.InvalidIndex, connectionIndex);
            Assert.AreEqual(connectionCA.Handle.Id, connectionIndex);

            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.ReleaseDSPNode(a);
                block.ReleaseDSPNode(b);
                block.ReleaseDSPNode(c);
                block.ReleaseDSPNode(d);
            }
        }
    }

    [Test]
    public void FindConnection_WithValidNonzeroPort_Succeeds()
    {
        using (var testData = TestData.Create(false))
        {
            DSPNode a;
            DSPNode b;
            DSPNode c;
            DSPNode d;
            using (var block = testData.Graph.CreateCommandBlock())
            {
                a = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                b = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                c = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                d = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                block.Connect(b, 0, a, 0);
                block.Connect(c, 0, b, 0);
                block.Connect(d, 0, c, 0);
                block.Connect(c, 0, a, 0);
                block.Connect(d, 0, a, 0);
            }

            testData.CheckMix(0.0f);
            Assert.AreEqual(DSPConnection.InvalidIndex, testData.Graph.FindConnectionIndex(c.Handle.Id, 0, a.Handle.Id, 1));
            Assert.AreEqual(DSPConnection.InvalidIndex, testData.Graph.FindConnectionIndex(c.Handle.Id, 1, a.Handle.Id, 0));

            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.ReleaseDSPNode(a);
                block.ReleaseDSPNode(b);
                block.ReleaseDSPNode(c);
                block.ReleaseDSPNode(d);
            }
        }
    }

    [Test]
    public void Disconnect_InConnectionOrder()
    {
        using (var testData = TestData.Create(false))
        {
            DSPNode a;
            DSPNode b;
            DSPNode c;
            DSPNode d;
            DSPConnection bConnection;
            DSPConnection cConnection;
            DSPConnection dConnection;

            using (var block = testData.Graph.CreateCommandBlock())
            {
                a = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                b = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                c = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                d = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                bConnection = block.Connect(a, 0, b, 0);
                cConnection = block.Connect(a, 0, c, 0);
                dConnection = block.Connect(a, 0, d, 0);
            }

            testData.CheckMix(0.0f);

            // Need to look up actual nodes from graph here to access real members
            a = testData.Graph.LookupNode(a.Handle);
            b = testData.Graph.LookupNode(b.Handle);
            c = testData.Graph.LookupNode(c.Handle);
            d = testData.Graph.LookupNode(d.Handle);

            // Test data automatically connects a passthrough node to the graph root
            Assert.AreEqual(4, CountUsedConnections(testData.Graph));

            Assert.AreEqual(3, CountOutputConnections(testData.Graph, a));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, a));
            Assert.AreEqual(1, CountInputConnections(testData.Graph, b));
            Assert.AreEqual(1, CountInputConnections(testData.Graph, c));
            Assert.AreEqual(1, CountInputConnections(testData.Graph, d));

            testData.Graph.Disconnect(bConnection.Handle);
            Assert.AreEqual(2, CountOutputConnections(testData.Graph, a));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, a));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, b));
            Assert.AreEqual(1, CountInputConnections(testData.Graph, c));
            Assert.AreEqual(1, CountInputConnections(testData.Graph, d));

            testData.Graph.Disconnect(cConnection.Handle);
            Assert.AreEqual(1, CountOutputConnections(testData.Graph, a));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, a));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, b));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, c));
            Assert.AreEqual(1, CountInputConnections(testData.Graph, d));

            testData.Graph.Disconnect(dConnection.Handle);
            Assert.AreEqual(0, CountOutputConnections(testData.Graph, a));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, a));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, b));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, c));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, d));


            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.ReleaseDSPNode(a);
                block.ReleaseDSPNode(b);
                block.ReleaseDSPNode(c);
                block.ReleaseDSPNode(d);
            }
        }
    }

    [Test]
    public void Disconnect_InReverseConnectionOrder()
    {
        using (var testData = TestData.Create(false))
        {
            DSPNode a;
            DSPNode b;
            DSPNode c;
            DSPNode d;
            DSPConnection bConnection;
            DSPConnection cConnection;
            DSPConnection dConnection;

            using (var block = testData.Graph.CreateCommandBlock())
            {
                a = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(a, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                b = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(b, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                c = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(c, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                d = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);
                block.AddInletPort(d, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                bConnection = block.Connect(a, 0, b, 0);
                cConnection = block.Connect(a, 0, c, 0);
                dConnection = block.Connect(a, 0, d, 0);
            }

            testData.CheckMix(0.0f);

            // Need to look up actual nodes from graph here to access real members
            a = testData.Graph.LookupNode(a.Handle);
            b = testData.Graph.LookupNode(b.Handle);
            c = testData.Graph.LookupNode(c.Handle);
            d = testData.Graph.LookupNode(d.Handle);

            // Test data automatically connects a passthrough node to the graph root
            Assert.AreEqual(4, CountUsedConnections(testData.Graph));

            Assert.AreEqual(3, CountOutputConnections(testData.Graph, a));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, a));
            Assert.AreEqual(1, CountInputConnections(testData.Graph, b));
            Assert.AreEqual(1, CountInputConnections(testData.Graph, c));
            Assert.AreEqual(1, CountInputConnections(testData.Graph, d));

            testData.Graph.Disconnect(dConnection.Handle);
            Assert.AreEqual(2, CountOutputConnections(testData.Graph, a));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, a));
            Assert.AreEqual(1, CountInputConnections(testData.Graph, b));
            Assert.AreEqual(1, CountInputConnections(testData.Graph, c));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, d));

            testData.Graph.Disconnect(cConnection.Handle);
            Assert.AreEqual(1, CountOutputConnections(testData.Graph, a));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, a));
            Assert.AreEqual(1, CountInputConnections(testData.Graph, b));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, c));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, d));

            testData.Graph.Disconnect(bConnection.Handle);
            Assert.AreEqual(0, CountOutputConnections(testData.Graph, a));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, a));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, b));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, c));
            Assert.AreEqual(0, CountInputConnections(testData.Graph, d));


            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.ReleaseDSPNode(a);
                block.ReleaseDSPNode(b);
                block.ReleaseDSPNode(c);
                block.ReleaseDSPNode(d);
            }
        }
    }

    [Test]
    public void AttenuationCheck()
    {
        using (var testData = TestData.Create(false))
        {
            DSPNode source;
            DSPNode destination;
            DSPConnection connection;
            using (var block = testData.Graph.CreateCommandBlock())
            {
                source = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddOutletPort(source, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                destination = block.CreateDSPNode<NoParameters, NoProviders, NullAudioKernel>();
                block.AddInletPort(destination, TestData.DefaultChannelCount, TestData.DefaultSoundFormat);

                connection = block.Connect(source, 0, destination, 0);
            }
            testData.CheckMix(0.0f);

            // Need to look up actual connection from graph here to access real members
            connection = testData.Graph.Connections[connection.Handle.Id];
            Assert.False(connection.HasAttenuation);

            using (var block = testData.Graph.CreateCommandBlock())
                block.SetAttenuation(connection, 0.5f);
            testData.CheckMix(0.0f);
            connection = testData.Graph.Connections[connection.Handle.Id];
            Assert.True(connection.HasAttenuation);

            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.SetAttenuation(connection, 1.0f);
                block.AddAttenuationKey(connection, 100000, 0.5f);
            }

            testData.CheckMix(0.0f);
            connection = testData.Graph.Connections[connection.Handle.Id];
            Assert.True(connection.HasAttenuation);

            using (var block = testData.Graph.CreateCommandBlock())
            {
                block.ReleaseDSPNode(source);
                block.ReleaseDSPNode(destination);
            }
        }
    }

    static int CountUsedConnections(DSPGraph graph)
    {
        return graph.Connections.Count(connection => connection.Valid);
    }

    static int CountInputConnections(DSPGraph graph, DSPNode node)
    {
        var connections = graph.Connections;
        int count = 0;
        var inputConnectionIndex = node.InputConnectionIndex;

        while (inputConnectionIndex != DSPConnection.InvalidIndex)
        {
            ++count;
            inputConnectionIndex = connections[inputConnectionIndex].NextInputConnectionIndex;
        }

        return count;
    }

    static int CountOutputConnections(DSPGraph graph, DSPNode node)
    {
        var connections = graph.Connections;
        int count = 0;
        var outputConnectionIndex = node.OutputConnectionIndex;

        while (outputConnectionIndex != DSPConnection.InvalidIndex)
        {
            ++count;
            outputConnectionIndex = connections[outputConnectionIndex].NextOutputConnectionIndex;
        }

        return count;
    }

    static void VerifyInterpolatedAttenuation(NativeArray<float> buffer, int firstSample, int sampleCount, int channelCount, float[] baseAttenuationValue, float[] attenuationDelta)
    {
        for (int sample = firstSample; sample < sampleCount; ++sample)
        {
            for (int channel = 0; channel < channelCount; ++channel)
            {
                var bufferIndex = (sample * channelCount) + channel;
                Assert.AreEqual(baseAttenuationValue[channel] + (sample * attenuationDelta[channel]), buffer[bufferIndex], 0.0001, $"Mismatched expectations at index {bufferIndex}");
            }
        }
    }

}
