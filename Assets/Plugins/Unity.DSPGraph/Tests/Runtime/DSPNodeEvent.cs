using System;
using UnityEngine;
using NUnit.Framework;
using Unity.Collections;
using Unity.Audio;

public class DSPNodeEvent
{
    enum EnumEvent
    {
        EnumEvent0,
        EnumEvent1,
        EnumEvent2
    }

    struct EmptyEvent {}

    struct EventWithInt
    {
        public int Value;
    }

    struct EventWithManyFields
    {
        public int   IntValue;
        public bool  BoolValue;
        public float FloatValue;
        public char  CharValue;
    }

    struct TestEventKernel : IAudioKernel<NoParameters, NoProviders>
    {
        public void Initialize() {}

        public void Execute(ref ExecuteContext<NoParameters, NoProviders> context)
        {
            context.PostEvent(EnumEvent.EnumEvent1);
            context.PostEvent(new EmptyEvent());
            context.PostEvent(new EventWithInt {Value = 42});
            context.PostEvent(new EventWithManyFields {IntValue = 42, BoolValue = true, FloatValue = 3.1416F, CharValue = 'x'});
        }

        public void Dispose() {}
    }

    class TestData : IDisposable
    {
        public DSPGraph Graph;
        public DSPNode  Node;
        public int      EnumEventCallCount;
        public int      EmptyEventCallCount;
        public int      EventWithIntCallCount;
        public int      EventWithManyFieldsCallCount;

        public static TestData Create()
        {
            var testData = new TestData {Graph = DSPGraph.Create(SoundFormat.Stereo, 2, 100, 1000)};
            using (var block = testData.Graph.CreateCommandBlock())
            {
                testData.Node = block.CreateDSPNode<NoParameters, NoProviders, TestEventKernel>();
                block.AddOutletPort(testData.Node, 2, SoundFormat.Stereo);
                block.Connect(testData.Node, 0, testData.Graph.RootDSP, 0);
            }
            return testData;
        }

        public void Sync()
        {
            using (var buff = new NativeArray<float>(200, Allocator.Temp))
            {
                Graph.BeginMix(0);
                Graph.ReadMix(buff, buff.Length / 2, 2);
            }
            Graph.Update();
        }

        public void EnumEventHandler(DSPNode evNode, EnumEvent ev)
        {
            Debug.Log("EnumEventHandler");
            Assert.That(evNode, Is.EqualTo(Node));
            Assert.That(ev, Is.EqualTo(EnumEvent.EnumEvent1));
            ++EnumEventCallCount;
            Debug.Log("EnumEventHandler call count: " + EnumEventCallCount);
        }

        public void EmptyEventHandler(DSPNode evNode, EmptyEvent ev)
        {
            Debug.Log("EmptyEventHandler");
            Assert.That(evNode, Is.EqualTo(Node));
            ++EmptyEventCallCount;
        }

        public void EventWithIntHandler(DSPNode evNode, EventWithInt ev)
        {
            Debug.Log("EventWithIntHandler");
            Assert.That(evNode, Is.EqualTo(Node));
            Assert.That(ev.Value, Is.EqualTo(42));
            ++EventWithIntCallCount;
        }

        public void EventWithManyFieldsHandler(DSPNode evNode, EventWithManyFields ev)
        {
            Debug.Log("EventWithManyFieldsHandler");
            Assert.That(evNode, Is.EqualTo(Node));
            Assert.That(ev.IntValue, Is.EqualTo(42));
            Assert.That(ev.BoolValue, Is.EqualTo(true));
            Assert.That(ev.FloatValue, Is.EqualTo(3.1416F));
            Assert.That(ev.CharValue, Is.EqualTo('x'));
            ++EventWithManyFieldsCallCount;
        }

        public void Dispose()
        {
            using (var block = Graph.CreateCommandBlock())
                block.ReleaseDSPNode(Node);
            Graph.Dispose();
        }
    }

    [Test]
    public void AddingNullHandler_IsRejected()
    {
        using (var testData = TestData.Create())
            Assert.Throws<ArgumentNullException>(() => testData.Graph.AddNodeEventHandler<EnumEvent>(null));
    }

    [Test]
    public void UnknownHandler_CannotBeRemoved()
    {
        using (var testData = TestData.Create())
        {
            var success = testData.Graph.RemoveNodeEventHandler(0);
            Assert.That(success, Is.False);
        }
    }

    [Test]
    public void AddedHandler_CanBeRemoved()
    {
        using (var testData = TestData.Create())
        {
            Action<DSPNode, EnumEvent> a = testData.EnumEventHandler;
            var handlerId = testData.Graph.AddNodeEventHandler(a);
            var success = testData.Graph.RemoveNodeEventHandler(handlerId);
            Assert.That(success, Is.True);
        }
    }

    [Test]
    public void PostEvent_WithNoHandler_CorrectlyIgnoresEvent()
    {
        using (var testData = TestData.Create())
        {
            testData.Sync();
        }
    }

    [Test]
    public void PostEnumEvent_WithAssociatedHandler_CorrectlyReceivesEvent()
    {
        using (var testData = TestData.Create())
        {
            Action<DSPNode, EnumEvent> a = testData.EnumEventHandler;
            var handlerId = testData.Graph.AddNodeEventHandler(a);

            testData.Sync();
            Assert.That(testData.EnumEventCallCount, Is.EqualTo(1));

            testData.Graph.RemoveNodeEventHandler(handlerId);
            testData.Sync();
            Assert.That(testData.EnumEventCallCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void PostEmptyEvent_WithAssociatedHandler_CorrectlyReceivesEvent()
    {
        using (var testData = TestData.Create())
        {
            Action<DSPNode, EmptyEvent> a = testData.EmptyEventHandler;
            var handlerId = testData.Graph.AddNodeEventHandler(a);

            testData.Sync();
            Assert.That(testData.EmptyEventCallCount, Is.EqualTo(1));

            testData.Graph.RemoveNodeEventHandler(handlerId);
            testData.Sync();
            Assert.That(testData.EmptyEventCallCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void PostEventWithInt_WithAssociatedHandler_CorrectlyReceivesEvent()
    {
        using (var testData = TestData.Create())
        {
            Action<DSPNode, EventWithInt> a = testData.EventWithIntHandler;
            var handlerId = testData.Graph.AddNodeEventHandler(a);

            testData.Sync();
            Assert.That(testData.EventWithIntCallCount, Is.EqualTo(1));

            testData.Graph.RemoveNodeEventHandler(handlerId);
            testData.Sync();
            Assert.That(testData.EventWithIntCallCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void PostEventWithManyFields_WithAssociatedHandler_CorrectlyReceivesEvent()
    {
        using (var testData = TestData.Create())
        {
            Action<DSPNode, EventWithManyFields> a = testData.EventWithManyFieldsHandler;
            var handlerId = testData.Graph.AddNodeEventHandler(a);

            testData.Sync();
            Assert.That(testData.EventWithManyFieldsCallCount, Is.EqualTo(1));

            testData.Graph.RemoveNodeEventHandler(handlerId);
            testData.Sync();
            Assert.That(testData.EventWithManyFieldsCallCount, Is.EqualTo(1));
        }
    }
}
