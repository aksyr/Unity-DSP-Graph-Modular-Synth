using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

using UnityEngine;
using Random = UnityEngine.Random;

namespace Unity.Media.Utilities.Tests.Runtime
{
    internal class OwnedAtomicQueues
    {
        const int kWorkerCount = 100;

        [Test]
        [TestCase(AllocationMode.Ephemeral)]
        [TestCase(AllocationMode.Pooled)]
        public void Enqueue(AllocationMode allocationMode)
        {
            using var queue = OwnedAtomicQueue<int>.Create(allocationMode);
            int i = 0;
            Assert.That(queue.IsEmpty);
            queue.Enqueue(ref i);
            Assert.That(!queue.IsEmpty);
        }

        [Test]
        [TestCase(AllocationMode.Ephemeral)]
        [TestCase(AllocationMode.Pooled)]
        public void Dequeue(AllocationMode allocationMode)
        {
            using var queue = OwnedAtomicQueue<int>.Create(allocationMode);
            int value = 1;
            Assert.That(queue.IsEmpty);
            queue.Enqueue(ref value);
            Assert.That(!queue.IsEmpty);
            Assert.AreEqual(value, queue.Dequeue());
            Assert.That(queue.IsEmpty);
        }

        [Test]
        [TestCase(AllocationMode.Ephemeral)]
        [TestCase(AllocationMode.Pooled)]
        public void TryDequeue(AllocationMode allocationMode)
        {
            using var queue = OwnedAtomicQueue<int>.Create(allocationMode);
            int value = 1;
            Assert.That(queue.IsEmpty);
            queue.Enqueue(ref value);
            Assert.That(!queue.IsEmpty);
            Assert.That(queue.TryDequeue(out int result));
            Assert.AreEqual(value, result);
            Assert.That(queue.IsEmpty);
        }

        [Test]
        [TestCase(AllocationMode.Ephemeral)]
        [TestCase(AllocationMode.Pooled)]
        public void Peek(AllocationMode allocationMode)
        {
            using var queue = OwnedAtomicQueue<int>.Create(allocationMode);
            var value = Random.Range(1, int.MaxValue);
            int anotherValue = Random.Range(1, int.MaxValue);;
            Assert.That(queue.IsEmpty);
            queue.Enqueue(ref value);
            queue.Enqueue(ref anotherValue);
            Assert.That(!queue.IsEmpty);
            for (var i = 0; i < 3; ++i)
            {
                ref int peeked = ref queue.Peek();
                Assert.AreEqual(value, peeked);
            }
        }

        [Test]
        [TestCase(AllocationMode.Ephemeral)]
        [TestCase(AllocationMode.Pooled)]
        public void Empty_ThenReenqueue(AllocationMode allocationMode)
        {
            using var queue = OwnedAtomicQueue<int>.Create(allocationMode);
            var value = Random.Range(1, int.MaxValue);
            Assert.That(queue.IsEmpty);

            queue.Enqueue(ref value);
            Assert.That(!queue.IsEmpty);
            Assert.AreEqual(value, queue.Dequeue());

            Assert.That(queue.IsEmpty);
            queue.Enqueue(ref value);
            Assert.That(!queue.IsEmpty);

            Assert.AreEqual(value, queue.Dequeue());
            Assert.That(queue.IsEmpty);
        }

        [Test]
        [TestCase(AllocationMode.Ephemeral)]
        [TestCase(AllocationMode.Pooled)]
        public void Enqueue_OnMultipleThreads(AllocationMode allocationMode)
        {
            using var queue = OwnedAtomicQueue<int>.Create(allocationMode);
            Assert.That(queue.IsEmpty);
            int completed = 0;

            for (var i = 0; i < kWorkerCount; ++i)
            {
                ThreadPool.QueueUserWorkItem(o =>
                {
                    var item = i;
                    queue.Enqueue(ref item);
                    Interlocked.Increment(ref completed);
                });
            }

            while (completed < kWorkerCount)
                Utility.YieldProcessor();
            Assert.That(!queue.IsEmpty);
        }

        [Test]
        [TestCase(AllocationMode.Ephemeral)]
        [TestCase(AllocationMode.Pooled)]
        public void Dequeue_OnMultipleThreads(AllocationMode allocationMode)
        {
            using var queue = OwnedAtomicQueue<int>.Create(allocationMode);
            Assert.That(queue.IsEmpty);
            int popped = 0;

            for (int i = 0; i < kWorkerCount; ++i)
                queue.Enqueue(ref i);

            for (int i = 0; i < kWorkerCount; ++i)
            {
                ThreadPool.QueueUserWorkItem(o =>
                {
                    queue.Dequeue();
                    Interlocked.Increment(ref popped);
                });
            }

            while (popped < kWorkerCount)
                Utility.YieldProcessor();
            Assert.That(queue.IsEmpty);
        }

        [Test]
        [TestCase(AllocationMode.Ephemeral)]
        [TestCase(AllocationMode.Pooled)]
        public void PreservesItems(AllocationMode allocationMode)
        {
            using var queue = OwnedAtomicQueue<int>.Create(allocationMode);
            var count = 0;
            var items = new HashSet<int>();
            var threads = new List<Thread>();

            for (int i = 0; i < kWorkerCount; ++i)
            {
                var thread = new Thread((o) =>
                {
                    var item = Interlocked.Increment(ref count);
                    queue.Enqueue(ref item);
                });
                items.Add(i + 1);
                thread.Start();
                threads.Add(thread);
            }

            foreach (var thread in threads)
                thread.Join();
            Assert.That(!queue.IsEmpty);

            for (int i = 0; i < kWorkerCount; ++i)
            {
                var item = queue.Dequeue();
                Assert.That(items.Contains(item), $"Missing item {item} (iteration {i})");
                items.Remove(item);
            }

            Assert.That(queue.IsEmpty);
            Assert.That(items.Count == 0);
        }

        [Test]
        [TestCase(AllocationMode.Ephemeral)]
        [TestCase(AllocationMode.Pooled)]
        public void PreservesItems_WithSimultaneousQueueDequeue(AllocationMode allocationMode)
        {
            using var queue = OwnedAtomicQueue<int>.Create(allocationMode);
            var count = 0;
            var allItems = new HashSet<int>();
            var foundItems = new HashSet<int>();
            var threads = new List<Thread>();

            for (int i = 0; i < kWorkerCount; ++i)
            {
                allItems.Add(i + 1);
                var thread = new Thread((o) =>
                {
                    try
                    {
                        var item = Interlocked.Increment(ref count);
                        queue.Enqueue(ref item);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                });
                thread.Start();
                threads.Add(thread);
                if (queue.TryDequeue(out int result))
                    foundItems.Add(result);
            }

            foreach (var thread in threads)
                thread.Join();

            while (foundItems.Count < kWorkerCount)
            {
                if (queue.TryDequeue(out int item))
                    foundItems.Add(item);
                else
                {
                    Assert.Fail(
                        $"After adding was completed, queue was empty after popping only {foundItems.Count} items");
                    return;
                }
            }

            Assert.That(queue.IsEmpty);
            CollectionAssert.AreEquivalent(allItems, foundItems);
        }

        [Test]
        public void Dequeue_FromEmptyQueue_Throws()
        {
            using var queue = OwnedAtomicQueue<AtomicNode>.Create();
            Assert.Throws<InvalidOperationException>(() => { queue.Dequeue(); });

            AtomicNode node = default;
            queue.Enqueue(ref node);
            queue.Dequeue();
            Assert.Throws<InvalidOperationException>(() => { queue.Dequeue(); });
        }

        [Test]
        public void TryDequeue_FromEmptyQueue_IsOkay()
        {
            using var queue = OwnedAtomicQueue<AtomicNode>.Create();
            Assert.False(queue.TryDequeue(out AtomicNode result));

            AtomicNode node = default;
            queue.Enqueue(ref node);
            Assert.True(queue.TryDequeue(out result));
            Assert.False(queue.TryDequeue(out result));
        }

        [Test]
        public void Peek_AtEmptyQueue_Throws()
        {
            using var queue = OwnedAtomicQueue<AtomicNode>.Create();
            Assert.Throws<InvalidOperationException>(() => { queue.Peek(); });

            AtomicNode node = default;
            queue.Enqueue(ref node);
            queue.Dequeue();
            Assert.Throws<InvalidOperationException>(() => { queue.Peek(); });
        }
    }
}
