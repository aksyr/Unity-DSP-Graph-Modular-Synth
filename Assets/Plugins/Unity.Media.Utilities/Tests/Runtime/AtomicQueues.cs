using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

using Unity.Media.Utilities;
using UnityEngine;
using Random = UnityEngine.Random;

internal unsafe class AtomicQueues
{
    const int kWorkerCount = 100;

    [Test]
    [TestCase(AllocationMode.Ephemeral)]
    [TestCase(AllocationMode.Pooled)]
    public void Enqueue(AllocationMode allocationMode)
    {
        using (var queue = AtomicQueue<int>.Create(allocationMode))
        {
            Assert.That(queue.IsEmpty);
            queue.Enqueue((int*)1);
            Assert.That(!queue.IsEmpty);
        }
    }

    [Test]
    [TestCase(AllocationMode.Ephemeral)]
    [TestCase(AllocationMode.Pooled)]
    public void Dequeue(AllocationMode allocationMode)
    {
        using (var queue = AtomicQueue<int>.Create(allocationMode))
        {
            var value = (int*)Random.Range(1, int.MaxValue);
            Assert.That(queue.IsEmpty);
            queue.Enqueue(value);
            Assert.That(!queue.IsEmpty);
            Assert.That(queue.Dequeue() == value);
            Assert.That(queue.IsEmpty);
        }
    }

    [Test]
    [TestCase(AllocationMode.Ephemeral)]
    [TestCase(AllocationMode.Pooled)]
    public void TryDequeue(AllocationMode allocationMode)
    {
        using (var queue = AtomicQueue<int>.Create(allocationMode))
        {
            var value = (int*)Random.Range(1, int.MaxValue);
            Assert.That(queue.IsEmpty);
            queue.Enqueue(value);
            Assert.That(!queue.IsEmpty);
            Assert.That(queue.TryDequeue(out var result) && result == value);
            Assert.That(queue.IsEmpty);
        }
    }

    [Test]
    [TestCase(AllocationMode.Ephemeral)]
    [TestCase(AllocationMode.Pooled)]
    public void Peek(AllocationMode allocationMode)
    {
        using (var queue = AtomicQueue<int>.Create(allocationMode))
        {
            var value = (int*)Random.Range(1, int.MaxValue);
            Assert.That(queue.IsEmpty);
            queue.Enqueue(value);
            queue.Enqueue(null);
            Assert.That(!queue.IsEmpty);
            Assert.That(queue.Peek() == value);
            Assert.That(queue.Peek() == value);
            Assert.That(queue.Peek() == value);
        }
    }

    [Test]
    [TestCase(AllocationMode.Ephemeral)]
    [TestCase(AllocationMode.Pooled)]
    public void Empty_ThenReenqueue(AllocationMode allocationMode)
    {
        var value = (int*)Random.Range(1, int.MaxValue);
        using (var queue = AtomicQueue<int>.Create(allocationMode))
        {
            Assert.That(queue.IsEmpty);

            queue.Enqueue(value);
            Assert.That(!queue.IsEmpty);
            Assert.AreEqual((long)value, (long)queue.Dequeue());

            Assert.That(queue.IsEmpty);
            queue.Enqueue(value);
            Assert.That(!queue.IsEmpty);

            Assert.AreEqual((long)value, (long)queue.Dequeue());
            Assert.That(queue.IsEmpty);
        }
    }

    [Test]
    [TestCase(AllocationMode.Ephemeral)]
    [TestCase(AllocationMode.Pooled)]
    public void Enqueue_OnMultipleThreads(AllocationMode allocationMode)
    {
        using (var queue = AtomicQueue<int>.Create(allocationMode))
        {
            Assert.That(queue.IsEmpty);
            int completed = 0;

            for (int i = 0; i < kWorkerCount; ++i)
            {
                ThreadPool.QueueUserWorkItem(o =>
                {
                    queue.Enqueue((int*)i);
                    Interlocked.Increment(ref completed);
                });
            }

            while (completed < kWorkerCount)
                Utility.YieldProcessor();
            Assert.That(!queue.IsEmpty);
        }
    }

    [Test]
    [TestCase(AllocationMode.Ephemeral)]
    [TestCase(AllocationMode.Pooled)]
    public void Dequeue_OnMultipleThreads(AllocationMode allocationMode)
    {
        using (var queue = AtomicQueue<int>.Create(allocationMode))
        {
            Assert.That(queue.IsEmpty);
            int popped = 0;

            for (int i = 0; i < kWorkerCount; ++i)
                queue.Enqueue((int*)i);

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
    }

    [Test]
    [TestCase(AllocationMode.Ephemeral)]
    [TestCase(AllocationMode.Pooled)]
    public void PreservesItems(AllocationMode allocationMode)
    {
        using (var queue = AtomicQueue<int>.Create(allocationMode))
        {
            var count = 0;
            var items = new HashSet<int>();
            var threads = new List<Thread>();

            for (int i = 0; i < kWorkerCount; ++i)
            {
                var thread = new Thread((o) =>
                {
                    var item = Interlocked.Increment(ref count);
                    queue.Enqueue((int*)item);
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
                var item = (int)queue.Dequeue();
                Assert.That(items.Contains(item), $"Missing item {item} (iteration {i})");
                items.Remove(item);
            }

            Assert.That(queue.IsEmpty);
            Assert.That(items.Count == 0);
        }
    }

    [Test]
    [TestCase(AllocationMode.Ephemeral)]
    [TestCase(AllocationMode.Pooled)]
    public void PreservesItems_WithSimultaneousQueueDequeue(AllocationMode allocationMode)
    {
        using (var queue = AtomicQueue<int>.Create(allocationMode))
        {
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
                        queue.Enqueue((int*)item);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                });
                thread.Start();
                threads.Add(thread);
                if (queue.TryDequeue(out var result))
                    foundItems.Add((int) result);
            }

            foreach (var thread in threads)
                thread.Join();

            while (foundItems.Count < kWorkerCount)
            {
                if (queue.TryDequeue(out var item))
                    foundItems.Add((int) item);
                else
                {
                    Assert.Fail($"After adding was completed, queue was empty after popping only {foundItems.Count} items");
                    return;
                }
            }

            Assert.That(queue.IsEmpty);
            CollectionAssert.AreEquivalent(allItems, foundItems);
        }
    }

    [Test]
    public void Dequeue_FromEmptyQueue_Throws()
    {
        using (var queue = AtomicQueue<int>.Create())
        {
            Assert.Throws<InvalidOperationException>(() => { queue.Dequeue(); });

            queue.Enqueue((int*)1);
            queue.Dequeue();
            Assert.Throws<InvalidOperationException>(() => { queue.Dequeue(); });
        }
    }

    [Test]
    public void TryDequeue_FromEmptyQueue_IsOkay()
    {
        using (var queue = AtomicQueue<int>.Create())
        {
            Assert.False(queue.TryDequeue(out int* result));

            queue.Enqueue((int*)1);
            Assert.True(queue.TryDequeue(out result));
            Assert.False(queue.TryDequeue(out result));
        }
    }

    [Test]
    public void Peek_AtEmptyQueue_Throws()
    {
        using (var queue = AtomicQueue<int>.Create())
        {
            Assert.Throws<InvalidOperationException>(() => { queue.Peek(); });

            queue.Enqueue((int*)1);
            queue.Dequeue();
            Assert.Throws<InvalidOperationException>(() => { queue.Peek(); });
        }
    }

    [Test]
    public void DescriptionIsValidated()
    {
        Assert.Throws<ArgumentException>(() => AtomicQueue<int>.FromDescription(default));
        // FIXME burst
//        using (var queue = AtomicQueue<int>.Create())
//            Assert.Throws<ArgumentException>(() => AtomicQueue<AtomicQueueDescription>.FromDescription(queue.Description));
    }
}
